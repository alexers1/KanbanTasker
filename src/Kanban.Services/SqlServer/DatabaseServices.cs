using KanbanTasker.Model.Services;
using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace KanbanTasker.Services.SqlServer
{
    /// <summary>
    /// SQL Server maintenance: native .bak backup/import, size monitoring, and the
    /// archive-database strategy. Runs on the API/server side; the client triggers over REST.
    /// </summary>
    public class DatabaseServices : IDatabaseServices
    {
        public async Task BackupDBAsync(string destPath)
        {
            using var connection = new SqlConnection(ServiceCollectionExtensions.ConnectionString);
            await connection.OpenAsync();

            try
            {
                await RunBackupAsync(connection, destPath);
            }
            catch (SqlException) when (IsValidBackupFile(destPath))
            {
                // The backup itself succeeded; the exception was only the msdb
                // history record (the login has no msdb access). Ignore.
            }
            catch (SqlException)
            {
                // The SQL Server service account may not be able to write to the chosen
                // folder. Back up to the server's own backup directory, then copy the
                // file to the destination as the current user.
                string serverDir = await GetServerBackupDirectoryAsync(connection);
                string intermediate = Path.Combine(serverDir, $"KanbanTasker_{DateTime.Now:yyyyMMdd_HHmmss}.bak");

                try
                {
                    await RunBackupAsync(connection, intermediate);
                }
                catch (SqlException) when (IsValidBackupFile(intermediate))
                {
                    // msdb history record failure again - backup file is fine
                }

                File.Copy(intermediate, destPath, overwrite: true);
                try { File.Delete(intermediate); } catch { /* server dir may deny delete */ }
            }
        }

        private static async Task RunBackupAsync(SqlConnection connection, string path)
        {
            using var backup = new SqlCommand(
                "BACKUP DATABASE [KanbanTasker] TO DISK = @path WITH INIT, FORMAT", connection);
            backup.Parameters.AddWithValue("@path", path);
            backup.CommandTimeout = 120;
            await backup.ExecuteNonQueryAsync();
        }

        private static async Task<string> GetServerBackupDirectoryAsync(SqlConnection connection)
        {
            using var cmd = new SqlCommand(
                "SELECT CAST(SERVERPROPERTY('InstanceDefaultBackupPath') AS nvarchar(4000))", connection);
            return await cmd.ExecuteScalarAsync() as string ?? Path.GetTempPath();
        }

        public async Task<string> ImportBackupAsync(string bakPath, bool overwrite = false)
        {
            // 1. Copy the .bak into the SQL Server instance's own backup directory. The RESTORE
            //    reads the file AS the SQL Server service account, which cannot access the API
            //    process's temp folder when the two run under different accounts (e.g. the API as
            //    LocalSystem writes to C:\Windows\SystemTemp, which SQL Server can't read ->
            //    "Cannot open backup device ... operating system error 5"). The instance backup
            //    directory is always readable by the service account.
            string serverBackupDir;
            using (var probe = new SqlConnection(ServiceCollectionExtensions.ConnectionString))
            {
                await probe.OpenAsync();
                serverBackupDir = await GetServerBackupDirectoryAsync(probe);
            }
            string temp = Path.Combine(serverBackupDir, $"kt_import_{DateTime.Now:yyyyMMddHHmmss}.bak");
            File.Copy(bakPath, temp, overwrite: true);
            try
            {
                string importConn = ServiceCollectionExtensions.ConnectionString.Replace("Database=KanbanTasker;", "Database=KanbanTaskerImport;");
                string dataPath, logPath;
                using (var import = new SqlConnection(importConn))
                {
                    await import.OpenAsync();
                    using var c1 = new SqlCommand("SELECT physical_name FROM sys.database_files WHERE type=0", import);
                    dataPath = (string)(await c1.ExecuteScalarAsync())!;
                    using var c2 = new SqlCommand("SELECT physical_name FROM sys.database_files WHERE type=1", import);
                    logPath = (string)(await c2.ExecuteScalarAsync())!;
                }

                using var main = new SqlConnection(ServiceCollectionExtensions.ConnectionString);
                await main.OpenAsync();

                // 2. Restore into the staging database (its own Express size budget - never touches live data)
                try
                {
                    using var restore = new SqlCommand($@"
                        ALTER DATABASE KanbanTaskerImport SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                        RESTORE DATABASE KanbanTaskerImport FROM DISK = @bak WITH REPLACE,
                            MOVE N'KanbanTasker' TO N'{dataPath}', MOVE N'KanbanTasker_log' TO N'{logPath}';
                        ALTER DATABASE KanbanTaskerImport SET MULTI_USER;", main);
                    restore.Parameters.AddWithValue("@bak", temp);
                    restore.CommandTimeout = 300;
                    await restore.ExecuteNonQueryAsync();
                }
                catch (SqlException)
                {
                    // msdb history record failure throws after a successful restore - verify instead
                    using var check = new SqlCommand(
                        "SELECT CASE WHEN OBJECT_ID('KanbanTaskerImport.dbo.tblBoards') IS NULL THEN 0 ELSE 1 END", main);
                    if ((int)(await check.ExecuteScalarAsync())! != 1) throw;
                }

                // 3. Make the staging schema uniform with the live one (older backups may lack
                //    the Uuid / timestamp / Attachments columns) and backfill any blanks. ALTERs
                //    must run in the staging DB's own context (cross-database ALTER isn't allowed),
                //    and after the ALTER batch so the new columns resolve in the backfill batch.
                using (var stage = new SqlConnection(importConn))
                {
                    await stage.OpenAsync();
                    await ExecAsync(stage, StagingSchemaSql, 120);
                    await ExecAsync(stage, StagingBackfillSql, 120);
                }

                // 4. Upsert by Uuid — idempotent and self-healing. Re-importing the same backup
                //    changes nothing; a newer backup (higher UpdatedAt) repairs matching rows; new
                //    Uuids are inserted. Nothing is deleted. Boards first so columns/tasks can remap
                //    their BoardId through the board's Uuid.
                int boards = await ExecAsync(main, MergeBoardsSql(overwrite), 300);
                await ExecAsync(main, MergeColumnsSql(overwrite), 300);
                int tasks = await ExecAsync(main, MergeTasksSql(overwrite), 300);

                return overwrite
                    ? $"Restored (overwrite): {boards} board(s) and {tasks} task(s) applied from the backup."
                    : $"Imported/updated {boards} board(s) and {tasks} task(s).";
            }
            finally
            {
                try { File.Delete(temp); } catch { }
            }
        }

        private static async Task<int> ExecAsync(SqlConnection conn, string sql, int timeoutSec)
        {
            using var cmd = new SqlCommand(sql, conn) { CommandTimeout = timeoutSec };
            return await cmd.ExecuteNonQueryAsync();
        }

        // --- Import SQL (upsert by Uuid) -----------------------------------------------------

        // Normalize the staging (KanbanTaskerImport) schema — run in that DB's context.
        private const string StagingSchemaSql = @"
IF COL_LENGTH('tblBoards','Uuid')       IS NULL ALTER TABLE tblBoards  ADD Uuid uniqueidentifier NOT NULL DEFAULT NEWID();
IF COL_LENGTH('tblBoards','CreatedAt')  IS NULL ALTER TABLE tblBoards  ADD CreatedAt nvarchar(40) NOT NULL DEFAULT '';
IF COL_LENGTH('tblBoards','UpdatedAt')  IS NULL ALTER TABLE tblBoards  ADD UpdatedAt nvarchar(40) NOT NULL DEFAULT '';
IF COL_LENGTH('tblColumns','Uuid')      IS NULL ALTER TABLE tblColumns ADD Uuid uniqueidentifier NOT NULL DEFAULT NEWID();
IF COL_LENGTH('tblColumns','CreatedAt') IS NULL ALTER TABLE tblColumns ADD CreatedAt nvarchar(40) NOT NULL DEFAULT '';
IF COL_LENGTH('tblColumns','UpdatedAt') IS NULL ALTER TABLE tblColumns ADD UpdatedAt nvarchar(40) NOT NULL DEFAULT '';
IF COL_LENGTH('tblTasks','Uuid')        IS NULL ALTER TABLE tblTasks   ADD Uuid uniqueidentifier NOT NULL DEFAULT NEWID();
IF COL_LENGTH('tblTasks','CreatedAt')   IS NULL ALTER TABLE tblTasks   ADD CreatedAt nvarchar(40) NOT NULL DEFAULT '';
IF COL_LENGTH('tblTasks','UpdatedAt')   IS NULL ALTER TABLE tblTasks   ADD UpdatedAt nvarchar(40) NOT NULL DEFAULT '';
IF COL_LENGTH('tblTasks','Attachments') IS NULL ALTER TABLE tblTasks   ADD Attachments nvarchar(max) NOT NULL DEFAULT N'';";

        private const string StagingBackfillSql = @"
UPDATE tblBoards  SET CreatedAt = CONVERT(nvarchar(40), SYSUTCDATETIME(), 121) WHERE CreatedAt = '';
UPDATE tblBoards  SET UpdatedAt = CreatedAt WHERE UpdatedAt = '';
UPDATE tblColumns SET CreatedAt = CONVERT(nvarchar(40), SYSUTCDATETIME(), 121) WHERE CreatedAt = '';
UPDATE tblColumns SET UpdatedAt = CreatedAt WHERE UpdatedAt = '';
UPDATE tblTasks   SET CreatedAt = CONVERT(nvarchar(40), SYSUTCDATETIME(), 121) WHERE CreatedAt = '';
UPDATE tblTasks   SET UpdatedAt = CreatedAt WHERE UpdatedAt = '';";

        // "Newest wins" by default; with overwrite the backup wins on every Uuid match.
        private static string MatchCond(bool overwrite) => overwrite ? "" : "AND S.UpdatedAt > T.UpdatedAt";

        private static string MergeBoardsSql(bool overwrite) => $@"
MERGE tblBoards AS T
USING KanbanTaskerImport.dbo.tblBoards AS S ON T.Uuid = S.Uuid
WHEN MATCHED {MatchCond(overwrite)} THEN
    UPDATE SET T.Name = S.Name, T.Notes = S.Notes, T.UpdatedAt = S.UpdatedAt
WHEN NOT MATCHED BY TARGET THEN
    INSERT (Name, Notes, Uuid, CreatedAt, UpdatedAt)
    VALUES (S.Name, S.Notes, S.Uuid, S.CreatedAt, S.UpdatedAt);";

        private static string MergeColumnsSql(bool overwrite) => $@"
MERGE tblColumns AS T
USING (
    SELECT sc.ColumnName, sc.Position, sc.MaxTaskLimit, sc.Uuid, sc.CreatedAt, sc.UpdatedAt, lb.Id AS LiveBoardId
    FROM KanbanTaskerImport.dbo.tblColumns sc
    JOIN KanbanTaskerImport.dbo.tblBoards sb ON sb.Id = sc.BoardId
    JOIN tblBoards lb ON lb.Uuid = sb.Uuid
) AS S ON T.Uuid = S.Uuid
WHEN MATCHED {MatchCond(overwrite)} THEN
    UPDATE SET T.ColumnName = S.ColumnName, T.Position = S.Position, T.MaxTaskLimit = S.MaxTaskLimit,
               T.BoardId = S.LiveBoardId, T.UpdatedAt = S.UpdatedAt
WHEN NOT MATCHED BY TARGET THEN
    INSERT (BoardId, ColumnName, Position, MaxTaskLimit, Uuid, CreatedAt, UpdatedAt)
    VALUES (S.LiveBoardId, S.ColumnName, S.Position, S.MaxTaskLimit, S.Uuid, S.CreatedAt, S.UpdatedAt);";

        private static string MergeTasksSql(bool overwrite) => $@"
MERGE tblTasks AS T
USING (
    SELECT st.Title, st.Description, st.Category, st.ColumnIndex, st.ColorKey, st.Tags, st.Attachments,
           st.DateCreated, st.DueDate, st.StartDate, st.FinishDate, st.TimeDue, st.ReminderTime,
           st.Uuid, st.CreatedAt, st.UpdatedAt, lb.Id AS LiveBoardId
    FROM KanbanTaskerImport.dbo.tblTasks st
    JOIN KanbanTaskerImport.dbo.tblBoards sb ON sb.Id = st.BoardId
    JOIN tblBoards lb ON lb.Uuid = sb.Uuid
) AS S ON T.Uuid = S.Uuid
WHEN MATCHED {MatchCond(overwrite)} THEN
    UPDATE SET T.BoardId=S.LiveBoardId, T.Title=S.Title, T.Description=S.Description, T.Category=S.Category,
               T.ColumnIndex=S.ColumnIndex, T.ColorKey=S.ColorKey, T.Tags=S.Tags, T.Attachments=S.Attachments,
               T.DateCreated=S.DateCreated, T.DueDate=S.DueDate, T.StartDate=S.StartDate, T.FinishDate=S.FinishDate,
               T.TimeDue=S.TimeDue, T.ReminderTime=S.ReminderTime, T.UpdatedAt=S.UpdatedAt
WHEN NOT MATCHED BY TARGET THEN
    INSERT (BoardId, Title, Description, Category, ColumnIndex, ColorKey, Tags, Attachments, DateCreated,
            DueDate, StartDate, FinishDate, TimeDue, ReminderTime, Uuid, CreatedAt, UpdatedAt)
    VALUES (S.LiveBoardId, S.Title, S.Description, S.Category, S.ColumnIndex, S.ColorKey, S.Tags, S.Attachments,
            S.DateCreated, S.DueDate, S.StartDate, S.FinishDate, S.TimeDue, S.ReminderTime, S.Uuid, S.CreatedAt, S.UpdatedAt);";

        private static bool IsValidBackupFile(string path)
        {
            try
            {
                return File.Exists(path) && new FileInfo(path).Length > 0;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Data-file size of the KanbanTasker database versus the SQL Server Express
        /// per-database limit (log files do not count). The limit depends on the server
        /// version: 50 GB since SQL Server 2025 Express (v17), 10 GB before that.
        /// </summary>
        public async Task<DatabaseUsage> GetDatabaseUsageAsync()
        {
            using var connection = new SqlConnection(ServiceCollectionExtensions.ConnectionString);
            await connection.OpenAsync();

            using var cmd = new SqlCommand(
                "SELECT CAST(SUM(CAST(size AS bigint)) * 8.0 / 1024 AS float) FROM sys.database_files WHERE type = 0",
                connection);
            double sizeMb = (await cmd.ExecuteScalarAsync()) is double d ? d : 0;

            double limitMb = 51200; // SQL Server 2025 Express: 50 GB
            try
            {
                using var ver = new SqlCommand(
                    "SELECT CAST(SERVERPROPERTY('ProductMajorVersion') AS nvarchar(10))", connection);
                if (int.TryParse(await ver.ExecuteScalarAsync() as string, out int major) && major < 17)
                    limitMb = 10240; // older Express editions: 10 GB
            }
            catch { /* keep the 50 GB default */ }

            return new DatabaseUsage { SizeMB = Math.Round(sizeMb, 1), LimitMB = limitMb };
        }

        /// <summary>
        /// Multiple-database strategy for the Express size limit: completed tasks are
        /// moved to the separate KanbanTaskerArchive database, keeping the main
        /// database small. The archive schema is created on first use.
        /// </summary>
        public async Task<int> ArchiveCompletedTasksAsync()
        {
            using var connection = new SqlConnection(ServiceCollectionExtensions.ConnectionString);
            await connection.OpenAsync();

            using (var ensure = new SqlCommand(@"
                IF OBJECT_ID('KanbanTaskerArchive.dbo.tblArchivedTasks') IS NULL
                CREATE TABLE KanbanTaskerArchive.dbo.tblArchivedTasks (
                    Id int IDENTITY PRIMARY KEY,
                    OriginalTaskId int NOT NULL,
                    BoardName nvarchar(200) NOT NULL DEFAULT N'',
                    Title nvarchar(max) NOT NULL DEFAULT N'',
                    Description nvarchar(max) NOT NULL DEFAULT N'',
                    Category nvarchar(200) NOT NULL DEFAULT N'',
                    ColorKey nvarchar(50) NOT NULL DEFAULT N'',
                    Tags nvarchar(max) NOT NULL DEFAULT N'',
                    Attachments nvarchar(max) NOT NULL DEFAULT N'',
                    DateCreated nvarchar(50) NOT NULL DEFAULT N'',
                    DueDate nvarchar(50) NOT NULL DEFAULT N'',
                    StartDate nvarchar(50) NOT NULL DEFAULT N'',
                    FinishDate nvarchar(50) NOT NULL DEFAULT N'',
                    TimeDue nvarchar(20) NOT NULL DEFAULT N'',
                    ReminderTime nvarchar(50) NOT NULL DEFAULT N'',
                    ArchivedAt datetime2 NOT NULL DEFAULT SYSDATETIME())", connection))
            {
                await ensure.ExecuteNonQueryAsync();
            }

            using var move = new SqlCommand(@"
                INSERT INTO KanbanTaskerArchive.dbo.tblArchivedTasks
                    (OriginalTaskId, BoardName, Title, Description, Category, ColorKey, Tags, Attachments,
                     DateCreated, DueDate, StartDate, FinishDate, TimeDue, ReminderTime)
                SELECT t.Id, b.Name, t.Title, t.Description, t.Category, t.ColorKey, t.Tags, t.Attachments,
                       t.DateCreated, t.DueDate, t.StartDate, t.FinishDate, t.TimeDue, t.ReminderTime
                FROM tblTasks t
                JOIN tblBoards b ON b.Id = t.BoardId
                WHERE t.Category = N'Completed';

                DELETE FROM tblTasks WHERE Category = N'Completed';", connection);

            // rows affected = inserted + deleted; the archived count is half
            int affected = await move.ExecuteNonQueryAsync();
            return affected / 2;
        }
    }
}
