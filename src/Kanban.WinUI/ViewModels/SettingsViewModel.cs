using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Kanban_WinUI.Services;
using Kanban_WinUI.Services.Api;
using KanbanTasker.Model.Services;

namespace Kanban_WinUI.ViewModels
{
    /// <summary>
    /// Settings: API connection, database backup/import/archive (server-side over REST),
    /// and size-limit monitoring. (Attachments are path references now — nothing to manage here.)
    /// </summary>
    public partial class SettingsViewModel : ObservableObject
    {
        private readonly IDatabaseServices _databaseServices;

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
        private string _connectionStatusText = "";

        /// <summary>The API base URL the client is talking to.</summary>
        public string DatabaseInfo => ApiConfig.Current is { } c ? $"API — {c.BaseUrl}" : "API — (not configured)";

        /// <summary>This PC's name — the prefix used for new attachment references.</summary>
        public string ThisMachineName => $"This PC: {Environment.MachineName}";

        public SettingsViewModel(IDatabaseServices databaseServices)
        {
            _databaseServices = databaseServices;
            _ = LoadUsageAsync();
        }

        /// <summary>Pings the current API (for the Data saving page status line).</summary>
        public async Task LoadConnectionStatusAsync()
        {
            var cfg = ApiConfig.Current;
            if (cfg == null)
            {
                ConnectionStatusText = "Status: not configured";
                return;
            }
            ConnectionStatusText = "Status: checking API…";
            string? error = await cfg.TestConnectionAsync();
            ConnectionStatusText = error == null
                ? "Status: connected ✔"
                : "Status: NOT connected — " + error;
        }

        /// <summary>
        /// Verifies a new API URL and, on success, saves it. The base address is applied on
        /// the next launch (a running HttpClient keeps its base address), so we prompt to restart.
        /// </summary>
        public async Task<bool> TestAndSaveApiAsync(ApiConfig cfg)
        {
            IsBusy = true;
            ConnectionStatusText = "Status: testing API…";
            try
            {
                string? error = await cfg.TestConnectionAsync();
                if (error != null)
                {
                    ConnectionStatusText = "Status: connection failed — " + error;
                    return false;
                }

                cfg.Save();
                ApiConfig.Current = cfg;
                OnPropertyChanged(nameof(DatabaseInfo));
                ConnectionStatusText = "Status: connected ✔ — saved. Restart the app to use the new API address.";
                return true;
            }
            finally
            {
                IsBusy = false;
            }
        }

        public async Task LoadUsageAsync()
        {
            try
            {
                var usage = await _databaseServices.GetDatabaseUsageAsync();
                UsagePercent = usage.Percent;
                UsageText = $"{usage.SizeMB:N1} MB of {usage.LimitMB / 1024:N0} GB Express limit ({usage.Percent:F1}%)";
            }
            catch (Exception ex)
            {
                UsageText = "Could not read database size: " + ex.Message;
            }
        }

        /// <summary>Backs up the database to the given .bak file path (server-side).</summary>
        public async Task<bool> BackupToAsync(string destPath)
        {
            IsBusy = true;
            StatusText = "Backing up database…";
            try
            {
                await _databaseServices.BackupDBAsync(destPath);
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
                string summary = await _databaseServices.ImportBackupAsync(bakPath);
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
                int moved = await _databaseServices.ArchiveCompletedTasksAsync();
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
    }
}
