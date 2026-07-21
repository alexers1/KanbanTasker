using System;
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

            ViewModel = new SettingsViewModel(
                App.GetService<IDatabaseServices>(),
                App.GetService<Kanban_WinUI.Services.AttachmentService>());
            DataContext = ViewModel;

            // Connection page: prefill the mode + fields and show live status
            InitConnectionUi();

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

            // IP/port: prefer the stored fields; fall back to parsing an older raw BaseUrl.
            string ip = cfg.ServerIp;
            int port = cfg.ServerPort;
            if (string.IsNullOrWhiteSpace(ip) || ip == "localhost")
            {
                var (parsedIp, parsedPort) = cfg.HostPortFromBaseUrl();
                if (!string.IsNullOrWhiteSpace(parsedIp)) { ip = parsedIp; port = parsedPort; }
            }

            CfgServerIpBox.Text = ip;
            CfgServerPortBox.Text = port.ToString();
            CfgDefaultUrlBox.Text = cfg.DefaultUrl;

            SelectModeRadio(cfg.Mode);      // fires ConnMode_SelectionChanged -> panel visibility + preview
            _ = ViewModel.LoadConnectionStatusAsync();
        }

        private void SelectModeRadio(ApiConnectionMode mode)
        {
            foreach (var item in ConnModeRadios.Items)
            {
                if (item is RadioButton rb && rb.Tag?.ToString() == mode.ToString())
                {
                    ConnModeRadios.SelectedItem = rb;
                    return;
                }
            }
            ConnModeRadios.SelectedIndex = 0;   // default to LAN
        }

        private ApiConnectionMode SelectedMode() =>
            (ConnModeRadios.SelectedItem as RadioButton)?.Tag?.ToString() == "DefaultUrl"
                ? ApiConnectionMode.DefaultUrl
                : ApiConnectionMode.LanIp;

        private ApiConfig BuildConfigFromUi()
        {
            int port = int.TryParse(CfgServerPortBox.Text?.Trim(), out var p) ? p : 5000;
            return new ApiConfig
            {
                Mode = SelectedMode(),
                ServerIp = string.IsNullOrWhiteSpace(CfgServerIpBox.Text) ? "localhost" : CfgServerIpBox.Text.Trim(),
                ServerPort = port,
                DefaultUrl = CfgDefaultUrlBox.Text?.Trim() ?? ""
            };
        }

        private void ConnMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Guarded: this can fire while the page is still being built.
            if (PanelLanIp == null || PanelDefaultUrl == null) return;

            bool lan = SelectedMode() == ApiConnectionMode.LanIp;
            PanelLanIp.Visibility = lan ? Visibility.Visible : Visibility.Collapsed;
            PanelDefaultUrl.Visibility = lan ? Visibility.Collapsed : Visibility.Visible;
            UpdateConnPreview();
        }

        private void ConnField_Changed(object sender, TextChangedEventArgs e) => UpdateConnPreview();

        private void UpdateConnPreview()
        {
            if (ConnUrlPreview == null) return;
            ConnUrlPreview.Text = SelectedMode() == ApiConnectionMode.LanIp
                ? $"Will connect to: {BuildConfigFromUi().BuildBaseUrl()}"
                : "";
        }

        private async void BtnTestSaveConn_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel.IsBusy)
                return;

            var cfg = BuildConfigFromUi();
            cfg.BaseUrl = cfg.BuildBaseUrl();      // effective URL used to test + persist
            await ViewModel.TestAndSaveApiAsync(cfg);
        }

        // === Backup & import ===

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
            if (file != null)
                await ViewModel.BackupToAsync(file.Path);
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
            if (file != null)
                await ViewModel.ImportFromAsync(file.Path);
        }

        // === Storage ===

        private async void BtnArchive_Click(object sender, RoutedEventArgs e)
        {
            if (!ViewModel.IsBusy)
                await ViewModel.ArchiveCompletedAsync();
        }

        // === Attachments ===

        private async void BtnMigrateAttachments_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel.IsBusy || App.MainAppWindow == null)
                return;

            var picker = new FolderPicker();
            picker.FileTypeFilter.Add("*");
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainAppWindow);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            var folder = await picker.PickSingleFolderAsync();
            if (folder != null)
                await ViewModel.MigrateAttachmentsAsync(folder.Path);
        }
    }
}
