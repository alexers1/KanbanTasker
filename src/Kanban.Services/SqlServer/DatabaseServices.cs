using KanbanTasker.Model.Services;
using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.IO;

namespace KanbanTasker.Services.SqlServer
{
    /// <summary>
    /// Exports a native SQL Server backup (.bak) of the KanbanTasker database
    /// to a user-chosen file.
    /// </summary>
    public class DatabaseServices : IDatabaseServices
    {
        public void BackupDB(string destPath)
        {
            using var connection = new SqlConnection(ServiceCollectionExtensions.ConnectionString);
            connection.Open();

            try
            {
                RunBackup(connection, destPath);
            }
            catch (SqlException) when (IsValidBackupFile(destPath))
            {
                // The backup itself succeeded; the exception was only the msdb
                // history record (the kanban login has no msdb access). Ignore.
            }
            catch (SqlException)
            {
                // The SQL Server service account may not be able to write to the chosen
                // folder. Back up to the server's own backup directory, then copy the
                // file to the destination as the current user.
                string serverDir = GetServerBackupDirectory(connection);
                string intermediate = Path.Combine(serverDir, $"KanbanTasker_{DateTime.Now:yyyyMMdd_HHmmss}.bak");

                try
                {
                    RunBackup(connection, intermediate);
                }
                catch (SqlException) when (IsValidBackupFile(intermediate))
                {
                    // msdb history record failure again - backup file is fine
                }

                File.Copy(intermediate, destPath, overwrite: true);
                try { File.Delete(intermediate); } catch { /* server dir may deny delete */ }
            }
        }

        private static void RunBackup(SqlConnection connection, string path)
        {
            using var backup = new SqlCommand(
                "BACKUP DATABASE [KanbanTasker] TO DISK = @path WITH INIT, FORMAT", connection);
            backup.Parameters.AddWithValue("@path", path);
            backup.CommandTimeout = 120;
            backup.ExecuteNonQuery();
        }

        private static string GetServerBackupDirectory(SqlConnection connection)
        {
            using var cmd = new SqlCommand(
                "SELECT CAST(SERVERPROPERTY('InstanceDefaultBackupPath') AS nvarchar(4000))", connection);
            return cmd.ExecuteScalar() as string ?? Path.GetTempPath();
        }

        public string ImportBackup(string bakPath)
        {
            // 1. Copy where the SQL Server service can read (user temp is proven accessible)
            string temp = Path.Combine(Path.GetTempPath(), $"kt_import_{DateTime.Now:yyyyMMddHHmmss}.bak");
            File.Copy(bakPath, temp, overwrite: true);
            try
            {
                string importConn = ServiceCollectionExtensions.ConnectionString.Replace("Database=KanbanTasker;", "Database=KanbanTaskerImport;");
                string dataPath, logPath;
                using (var import = new SqlConnection(importConn))
                {
                    import.Open();
                    using var c1 = new SqlCommand("SELECT physical_name FROM sys.database_files WHERE type=0", import);
                    dataPath = (string)c1.ExecuteScalar()!;
                    using var c2 = new SqlCommand("SELECT physical_name FROM sys.database_files WHERE type=1", import);
                    logPath = (string)c2.ExecuteScalar()!;
                }

                using var main = new SqlConnection(ServiceCollectionExtensions.ConnectionString);
                main.Open();

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
                    restore.ExecuteNonQuery();
                }
                catch (SqlException)
                {
                    // msdb history record failure throws after a successful restore - verify instead
                    using var check = new SqlCommand(
                        "SELECT CASE WHEN OBJECT_ID('KanbanTaskerImport.dbo.tblBoards') IS NULL THEN 0 ELSE 1 END", main);
                    if ((int)check.ExecuteScalar()! != 1) throw;
                }

                // 3. Space check: route completed tasks to the archive DB when the merge could crowd the limit
                var usage = GetDatabaseUsage();
                bool tight = usage.Percent >= 80;
                bool hasAttach;
                using (var col = new SqlCommand(
                    "SELECT CASE WHEN COL_LENGTH('KanbanTaskerImport.dbo.tblTasks','Attachments') IS NULL THEN 0 ELSE 1 END", main))
                    hasAttach = (int)col.ExecuteScalar()! == 1;
                string attachExpr = hasAttach ? "t.Attachments" : "N''";

                if (tight) ArchiveCompletedTasks(); // also makes room in the live database

                // 4. Merge staging boards into the live database as new boards (ID remap, nothing deleted)
                int boards = 0, tasks = 0, archived = 0;
                var stagedBoards = new List<(int Id, string Name, string Notes)>();
                using (var read = new SqlCommand("SELECT Id, Name, Notes FROM KanbanTaskerImport.dbo.tblBoards", main))
                using (var rd = read.ExecuteReader())
                    while (rd.Read()) stagedBoards.Add((rd.GetInt32(0), rd.GetString(1), rd.GetString(2)));

                foreach (var b in stagedBoards)
                {
                    using var insBoard = new SqlCommand(
                        "INSERT INTO tblBoards (Name, Notes) OUTPUT INSERTED.Id VALUES (@n, @o)", main);
                    insBoard.Parameters.AddWithValue("@n", b.Name);
                    insBoard.Parameters.AddWithValue("@o", b.Notes);
                    int newId = (int)insBoard.ExecuteScalar()!;
                    boards++;

                    using var insCols = new SqlCommand(@"
                        INSERT INTO tblColumns (BoardId, ColumnName, Position, MaxTaskLimit)
                        SELECT @nid, ColumnName, Position, MaxTaskLimit
                        FROM KanbanTaskerImport.dbo.tblColumns WHERE BoardId = @oid", main);
                    insCols.Parameters.AddWithValue("@nid", newId);
                    insCols.Parameters.AddWithValue("@oid", b.Id);
                    insCols.ExecuteNonQuery();

                    string activeFilter = tight ? "AND t.Category <> N'Completed'" : "";
                    using var insTasks = new SqlCommand($@"
                        INSERT INTO tblTasks (BoardId, Title, Description, Category, ColumnIndex, ColorKey, Tags,
                                              Attachments, DateCreated, DueDate, StartDate, FinishDate, TimeDue, ReminderTime)
                        SELECT @nid, t.Title, t.Description, t.Category, t.ColumnIndex, t.ColorKey, t.Tags,
                               {attachExpr}, t.DateCreated, t.DueDate, t.StartDate, t.FinishDate, t.TimeDue, t.ReminderTime
                        FROM KanbanTaskerImport.dbo.tblTasks t WHERE t.BoardId = @oid {activeFilter}", main);
                    insTasks.Parameters.AddWithValue("@nid", newId);
                    insTasks.Parameters.AddWithValue("@oid", b.Id);
                    tasks += insTasks.ExecuteNonQuery();

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
                        archived += insArch.ExecuteNonQuery();
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
        public DatabaseUsage GetDatabaseUsage()
        {
            using var connection = new SqlConnection(ServiceCollectionExtensions.ConnectionString);
            connection.Open();

            using var cmd = new SqlCommand(
                "SELECT CAST(SUM(CAST(size AS bigint)) * 8.0 / 1024 AS float) FROM sys.database_files WHERE type = 0",
                connection);
            double sizeMb = cmd.ExecuteScalar() is double d ? d : 0;

            double limitMb = 51200; // SQL Server 2025 Express: 50 GB
            try
            {
                using var ver = new SqlCommand(
                    "SELECT CAST(SERVERPROPERTY('ProductMajorVersion') AS nvarchar(10))", connection);
                if (int.TryParse(ver.ExecuteScalar() as string, out int major) && major < 17)
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
        public int ArchiveCompletedTasks()
        {
            using var connection = new SqlConnection(ServiceCollectionExtensions.ConnectionString);
            connection.Open();

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
                ensure.ExecuteNonQuery();
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
            int affected = move.ExecuteNonQuery();
            return affected / 2;
        }
    }
}
