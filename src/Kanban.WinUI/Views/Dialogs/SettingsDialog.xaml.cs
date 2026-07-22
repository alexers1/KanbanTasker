using System;
using System.IO;
using Kanban_WinUI.ViewModels;
using Kanban_WinUI.Services.Api;
using KanbanTasker.Model.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;

namespace Kanban_WinUI.Views.Dialogs
{
    /// <summary>
    /// Settings with categories on the left and details on the right:
    /// Data saving (API connection), Backup &amp; import, Storage, Attachments, About.
    /// </summary>
    public sealed partial class SettingsDialog : ContentDialog
    {
        public SettingsViewModel ViewModel { get; }

        public SettingsDialog()
        {
            InitializeComponent();

            ViewModel = new SettingsViewModel(App.GetService<IDatabaseServices>());
            DataContext = ViewModel;

            // Connection page: prefill the fields and show live status
            InitConnectionUi();
            UpdateBackupLanState();   // backup/import are LAN-only

            CategoryList.SelectedIndex = 0;
        }

        private void CategoryList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            string tag = (CategoryList.SelectedItem as ListViewItem)?.Tag?.ToString() ?? "Connection";
            PanelConnection.Visibility = tag == "Connection" ? Visibility.Visible : Visibility.Collapsed;
            PanelBackup.Visibility = tag == "Backup" ? Visibility.Visible : Visibility.Collapsed;
            PanelStorage.Visibility = tag == "Storage" ? Visibility.Visible : Visibility.Collapsed;
            PanelAttachments.Visibility = tag == "Attachments" ? Visibility.Visible : Visibility.Collapsed;
            PanelAbout.Visibility = tag == "About" ? Visibility.Visible : Visibility.Collapsed;
        }

        private void BtnCloseSettings_Click(object sender, RoutedEventArgs e)
        {
            Hide();
        }

        // === Connection: how the client reaches the API ===

        /// <summary>Prefill the connection controls from the running config.</summary>
        private void InitConnectionUi()
        {
            var cfg = ApiConfig.Current ?? new ApiConfig();

            string ip = cfg.ServerIp;
            int port = cfg.ServerPort;
            if (string.IsNullOrWhiteSpace(ip))
            {
                var (parsedIp, parsedPort) = cfg.HostPortFromBaseUrl();
                if (!string.IsNullOrWhiteSpace(parsedIp)) { ip = parsedIp; port = parsedPort; }
            }

            CfgServerIpBox.Text = ip;
            CfgServerPortBox.Text = port.ToString();
            UpdateConnPreview();
            _ = ViewModel.LoadConnectionStatusAsync();
        }

        private ApiConfig BuildConfigFromUi()
        {
            int port = int.TryParse(CfgServerPortBox.Text?.Trim(), out var p) ? p : 5001;
            return new ApiConfig
            {
                ServerIp = string.IsNullOrWhiteSpace(CfgServerIpBox.Text) ? "192.168.5.2" : CfgServerIpBox.Text.Trim(),
                ServerPort = port
            };
        }

        private void ConnField_Changed(object sender, TextChangedEventArgs e) => UpdateConnPreview();

        private void UpdateConnPreview()
        {
            if (ConnUrlPreview == null) return;
            ConnUrlPreview.Text = $"Will connect to: {BuildConfigFromUi().BuildBaseUrl()}";
        }

        private async void BtnTestSaveConn_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel.IsBusy)
                return;

            var cfg = BuildConfigFromUi();
            cfg.BaseUrl = cfg.BuildBaseUrl();      // effective URL used to test + persist
            await ViewModel.TestAndSaveApiAsync(cfg);
        }

        // === Backup & import (LAN only) — the .bak streams down to / up from this PC ===

        /// <summary>Grey out backup/import unless the client is on the LAN.</summary>
        private void UpdateBackupLanState()
        {
            bool lan = ApiConfig.Current?.IsLan ?? false;
            if (BtnBackup != null) BtnBackup.IsEnabled = lan;
            if (BtnImport != null) BtnImport.IsEnabled = lan;
            if (ChkOverwrite != null) ChkOverwrite.IsEnabled = lan;
            if (BackupLanNote != null) BackupLanNote.Visibility = lan ? Visibility.Collapsed : Visibility.Visible;
        }

        private async void BtnBackup_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel.IsBusy || App.MainAppWindow == null)
                return;

            var picker = new FileSavePicker();
            picker.FileTypeChoices.Add("SQL Server backup", new[] { ".bak" });
            picker.SuggestedFileName = $"KanbanTasker_{DateTime.Now:yyyy-MM-dd}";
            picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;

            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainAppWindow);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            var file = await picker.PickSaveFileAsync();
            if (file == null) return;

            ViewModel.IsBusy = true;
            ViewModel.StatusText = "Backing up the server database and downloading…";
            try
            {
                var http = App.GetService<System.Net.Http.HttpClient>();
                using var resp = await http.GetAsync("api/database/backup/download",
                    System.Net.Http.HttpCompletionOption.ResponseHeadersRead);
                if (!resp.IsSuccessStatusCode)
                {
                    ViewModel.StatusText = resp.StatusCode == System.Net.HttpStatusCode.Forbidden
                        ? "Backup is only available on the local network."
                        : $"Backup failed: HTTP {(int)resp.StatusCode}.";
                    return;
                }
                using var src = await resp.Content.ReadAsStreamAsync();
                using var dst = await file.OpenStreamForWriteAsync();
                dst.SetLength(0);
                await src.CopyToAsync(dst);
                ViewModel.StatusText = $"Backup saved to {file.Path}";
            }
            catch (Exception ex) { ViewModel.StatusText = "Backup failed: " + ex.Message; }
            finally { ViewModel.IsBusy = false; }
        }

        private async void BtnImport_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel.IsBusy || App.MainAppWindow == null)
                return;

            var picker = new FileOpenPicker();
            picker.FileTypeFilter.Add(".bak");
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainAppWindow);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            var file = await picker.PickSingleFileAsync();
            if (file == null) return;

            bool overwrite = ChkOverwrite?.IsChecked == true;
            ViewModel.IsBusy = true;
            ViewModel.StatusText = overwrite ? "Uploading and restoring (overwrite)…" : "Uploading and importing…";
            try
            {
                var http = App.GetService<System.Net.Http.HttpClient>();
                using var src = await file.OpenStreamForReadAsync();
                using var content = new System.Net.Http.MultipartFormDataContent();
                var part = new System.Net.Http.StreamContent(src);
                part.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
                content.Add(part, "file", file.Name);

                string url = $"api/database/import/upload?overwrite={(overwrite ? "true" : "false")}";
                using var resp = await http.PostAsync(url, content);
                string bodyText = await resp.Content.ReadAsStringAsync();
                if (!resp.IsSuccessStatusCode)
                {
                    ViewModel.StatusText = resp.StatusCode == System.Net.HttpStatusCode.Forbidden
                        ? "Import is only available on the local network."
                        : $"Import failed: HTTP {(int)resp.StatusCode}.";
                    return;
                }
                try
                {
                    using var doc = System.Text.Json.JsonDocument.Parse(bodyText);
                    ViewModel.StatusText = doc.RootElement.GetProperty("message").GetString() ?? "Import complete.";
                }
                catch { ViewModel.StatusText = "Import complete."; }
                await ViewModel.LoadUsageAsync();
            }
            catch (Exception ex) { ViewModel.StatusText = "Import failed: " + ex.Message; }
            finally { ViewModel.IsBusy = false; }
        }

        // === Storage ===

        private async void BtnArchive_Click(object sender, RoutedEventArgs e)
        {
            if (!ViewModel.IsBusy)
                await ViewModel.ArchiveCompletedAsync();
        }

    }
}
