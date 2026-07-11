using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Kanban_WinUI.Services;
using KanbanTasker.Model.Services;

namespace Kanban_WinUI.ViewModels
{
    /// <summary>
    /// Settings: database backup export, Express size-limit monitoring with archive,
    /// and the attachments folder (paths in DB, files on disk).
    /// </summary>
    public partial class SettingsViewModel : ObservableObject
    {
        private readonly IDatabaseServices _databaseServices;
        private readonly AttachmentService _attachmentService;

        [ObservableProperty]
        private bool _isBusy;

        [ObservableProperty]
        private string _statusText = "";

        [ObservableProperty]
        private string _usageText = "Checking database size…";

        [ObservableProperty]
        private double _usagePercent;

        [ObservableProperty]
        private string _archiveStatusText = "";

        [ObservableProperty]
        private string _attachmentsRoot = "";

        [ObservableProperty]
        private string _attachmentStatusText = "";

        /// <summary>Active server/database, from the verified appsettings.json configuration.</summary>
        public string DatabaseInfo => KanbanTasker.Services.DatabaseConfig.Current is { } c
            ? $"SQL Server — {c.Server}, database {c.Database}"
            : "SQL Server";

        public SettingsViewModel(IDatabaseServices databaseServices, AttachmentService attachmentService)
        {
            _databaseServices = databaseServices;
            _attachmentService = attachmentService;
            AttachmentsRoot = _attachmentService.RootFolder;
            _ = LoadUsageAsync();
        }

        public async Task LoadUsageAsync()
        {
            try
            {
                var usage = await Task.Run(_databaseServices.GetDatabaseUsage);
                UsagePercent = usage.Percent;
                UsageText = $"{usage.SizeMB:N1} MB of {usage.LimitMB / 1024:N0} GB Express limit ({usage.Percent:F1}%)";
            }
            catch (Exception ex)
            {
                UsageText = "Could not read database size: " + ex.Message;
            }
        }

        /// <summary>Backs up the database to the given .bak file path.</summary>
        public async Task<bool> BackupToAsync(string destPath)
        {
            IsBusy = true;
            StatusText = "Backing up database…";
            try
            {
                await Task.Run(() => _databaseServices.BackupDB(destPath));
                StatusText = $"Backup saved to {destPath}";
                return true;
            }
            catch (Exception ex)
            {
                StatusText = "Backup failed: " + ex.Message;
                return false;
            }
            finally
            {
                IsBusy = false;
            }
        }

        /// <summary>Restores a .bak into staging and merges its boards as new boards (no data loss).</summary>
        public async Task ImportFromAsync(string bakPath)
        {
            IsBusy = true;
            StatusText = "Importing backup…";
            try
            {
                string summary = await Task.Run(() => _databaseServices.ImportBackup(bakPath));
                StatusText = summary + " Boards refresh when Settings closes.";
                await LoadUsageAsync();
            }
            catch (Exception ex)
            {
                StatusText = "Import failed: " + ex.Message;
            }
            finally
            {
                IsBusy = false;
            }
        }

        /// <summary>Moves all tasks in "Completed" columns to the KanbanTaskerArchive database.</summary>
        public async Task ArchiveCompletedAsync()
        {
            IsBusy = true;
            ArchiveStatusText = "Archiving completed tasks…";
            try
            {
                int moved = await Task.Run(_databaseServices.ArchiveCompletedTasks);
                ArchiveStatusText = moved > 0
                    ? $"Archived {moved} completed task(s) to the KanbanTaskerArchive database."
                    : "No completed tasks to archive.";
                await LoadUsageAsync();
            }
            catch (Exception ex)
            {
                ArchiveStatusText = "Archive failed: " + ex.Message;
            }
            finally
            {
                IsBusy = false;
            }
        }

        /// <summary>Copies all attachment files to a new folder and repoints the app at it.</summary>
        public async Task MigrateAttachmentsAsync(string newRoot)
        {
            IsBusy = true;
            AttachmentStatusText = "Moving attachment files…";
            try
            {
                int copied = await Task.Run(() => _attachmentService.MigrateTo(newRoot));
                AttachmentsRoot = _attachmentService.RootFolder;
                AttachmentStatusText = $"Moved {copied} file(s). Attachments now live in {newRoot}.";
            }
            catch (Exception ex)
            {
                AttachmentStatusText = "Move failed: " + ex.Message;
            }
            finally
            {
                IsBusy = false;
            }
        }
    }
}
