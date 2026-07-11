using System;
using System.Collections.Generic;
using System.Text;

namespace KanbanTasker.Model.Services
{
    public interface IDatabaseServices
    {
        void BackupDB(string destPath);

        /// <summary>Current data-file size versus the SQL Server Express per-database limit.</summary>
        DatabaseUsage GetDatabaseUsage();

        /// <summary>
        /// Moves all tasks in "Completed" columns to the KanbanTaskerArchive database
        /// (keeps the main database under the Express size limit). Returns the count moved.
        /// </summary>
        int ArchiveCompletedTasks();

        /// <summary>
        /// Restores a .bak into the KanbanTaskerImport staging database, then merges its
        /// boards into the live database as NEW boards (never overwrites existing data).
        /// If space is tight, completed tasks go straight to the archive database.
        /// Returns a human-readable summary.
        /// </summary>
        string ImportBackup(string bakPath);
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