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

        public async Task<string> ImportBackupAsync(string bakPath)
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

                // 3. Space check: route completed tasks to the archive DB when the merge could crowd the limit
                var usage = await GetDatabaseUsageAsync();
                bool tight = usage.Percent >= 80;
                bool hasAttach;
                using (var col = new SqlCommand(
                    "SELECT CASE WHEN COL_LENGTH('KanbanTaskerImport.dbo.tblTasks','Attachments') IS NULL THEN 0 ELSE 1 END", main))
                    hasAttach = (int)(await col.ExecuteScalarAsync())! == 1;
                string attachExpr = hasAttach ? "t.Attachments" : "N''";

                if (tight) await ArchiveCompletedTasksAsync(); // also makes room in the live database

                // 4. Merge staging boards into the live database as new boards (ID remap, nothing deleted)
                int boards = 0, tasks = 0, archived = 0;
                var stagedBoards = new List<(int Id, string Name, string Notes)>();
                using (var read = new SqlCommand("SELECT Id, Name, Notes FROM KanbanTaskerImport.dbo.tblBoards", main))
                using (var rd = await read.ExecuteReaderAsync())
                    while (await rd.ReadAsync()) stagedBoards.Add((rd.GetInt32(0), rd.GetString(1), rd.GetString(2)));

                foreach (var b in stagedBoards)
                {
                    using var insBoard = new SqlCommand(
                        "INSERT INTO tblBoards (Name, Notes) OUTPUT INSERTED.Id VALUES (@n, @o)", main);
                    insBoard.Parameters.AddWithValue("@n", b.Name);
                    insBoard.Parameters.AddWithValue("@o", b.Notes);
                    int newId = (int)(await insBoard.ExecuteScalarAsync())!;
                    boards++;

                    using var insCols = new SqlCommand(@"
                        INSERT INTO tblColumns (BoardId, ColumnName, Position, MaxTaskLimit)
                        SELECT @nid, ColumnName, Position, MaxTaskLimit
                        FROM KanbanTaskerImport.dbo.tblColumns WHERE BoardId = @oid", main);
                    insCols.Parameters.AddWithValue("@nid", newId);
                    insCols.Parameters.AddWithValue("@oid", b.Id);
                    await insCols.ExecuteNonQueryAsync();

                    string activeFilter = tight ? "AND t.Category <> N'Completed'" : "";
                    using var insTasks = new SqlCommand($@"
                        INSERT INTO tblTasks (BoardId, Title, Description, Category, ColumnIndex, ColorKey, Tags,
                                              Attachments, DateCreated, DueDate, StartDate, FinishDate, TimeDue, ReminderTime)
                        SELECT @nid, t.Title, t.Description, t.Category, t.ColumnIndex, t.ColorKey, t.Tags,
                               {attachExpr}, t.DateCreated, t.DueDate, t.StartDate, t.FinishDate, t.TimeDue, t.ReminderTime
                        FROM KanbanTaskerImport.dbo.tblTasks t WHERE t.BoardId = @oid {activeFilter}", main);
                    insTasks.Parameters.AddWithValue("@nid", newId);
                    insTasks.Parameters.AddWithValue("@oid", b.Id);
                    tasks += await insTasks.ExecuteNonQueryAsync();

                    if (tight)
                    {
                        // Completed tasks from the backup go straight to the archive database
                        using var insArch = new SqlCommand($@"
                            INSERT INTO KanbanTaskerArchive.dbo.tblArchivedTasks
                                (OriginalTaskId, BoardName, Title, Description, Category, ColorKey, Tags, Attachments,
                                 DateCreated, DueDate, StartDate, FinishDate, TimeDue, ReminderTime)
                            SELECT t.Id, @bn, t.Title, t.Description, t.Category, t.ColorKey, t.Tags, {attachExpr},
                                   t.DateCreated, t.DueDate, t.StartDate, t.FinishDate, t.TimeDue, t.ReminderTime
                            FROM KanbanTaskerImport.dbo.tblTasks t
                            WHERE t.BoardId = @oid AND t.Category = N'Completed'", main);
                        insArch.Parameters.AddWithValue("@bn", b.Name);
                        insArch.Parameters.AddWithValue("@oid", b.Id);
                        archived += await insArch.ExecuteNonQueryAsync();
                    }
                }

                return archived > 0
                    ? $"Imported {boards} board(s) with {tasks} task(s); {archived} completed task(s) went to the archive database (space saving)."
                    : $"Imported {boards} board(s) with {tasks} task(s).";
            }
            finally
            {
                try { File.Delete(temp); } catch { }
            }
        }

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
