using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace KanbanTasker.Model.Services
{
    /// <summary>
    /// Database-level maintenance. These run on the server/API side (SQL Server operations);
    /// the WinUI client triggers them over REST.
    /// </summary>
    public interface IDatabaseServices
    {
        Task BackupDBAsync(string destPath);

        /// <summary>Current data-file size versus the SQL Server Express per-database limit.</summary>
        Task<DatabaseUsage> GetDatabaseUsageAsync();

        /// <summary>
        /// Moves all tasks in "Completed" columns to the KanbanTaskerArchive database
        /// (keeps the main database under the Express size limit). Returns the count moved.
        /// </summary>
        Task<int> ArchiveCompletedTasksAsync();

        /// <summary>
        /// Restores a .bak into the KanbanTaskerImport staging database, then merges it into the
        /// live database by Uuid (idempotent — re-importing the same backup changes nothing, and
        /// missing rows are restored). With <paramref name="overwrite"/> = false the newer row
        /// wins (by UpdatedAt); with true the backup wins on every match (a full restore that
        /// repairs even newer-but-corrupted rows). Nothing is ever deleted. Returns a summary.
        /// </summary>
        Task<string> ImportBackupAsync(string bakPath, bool overwrite = false);
    }

    public class DatabaseUsage
    {
        public double SizeMB { get; set; }

        /// <summary>
        /// Express per-database limit, set by server version at query time:
        /// 50 GB on SQL Server 2025 (v17+), 10 GB on older Express editions.
        /// </summary>
        public double LimitMB { get; set; } = 51200;

        public double Percent => LimitMB <= 0 ? 0 : Math.Min(100, SizeMB / LimitMB * 100);
    }
}
