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

            // Data saving page: prefill the API address + show live status
            CfgApiUrlBox.Text = ApiConfig.Current?.BaseUrl ?? "http://localhost:5000";
            _ = ViewModel.LoadConnectionStatusAsync();

            CategoryList.SelectedIndex = 0;
        }

        private void CategoryList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            string tag = (CategoryList.SelectedItem as ListViewItem)?.Tag?.ToString() ?? "DataSaving";
            PanelDataSaving.Visibility = tag == "DataSaving" ? Visibility.Visible : Visibility.Collapsed;
            PanelBackup.Visibility = tag == "Backup" ? Visibility.Visible : Visibility.Collapsed;
            PanelStorage.Visibility = tag == "Storage" ? Visibility.Visible : Visibility.Collapsed;
            PanelAttachments.Visibility = tag == "Attachments" ? Visibility.Visible : Visibility.Collapsed;
            PanelAbout.Visibility = tag == "About" ? Visibility.Visible : Visibility.Collapsed;
        }

        private void BtnCloseSettings_Click(object sender, RoutedEventArgs e)
        {
            Hide();
        }

        // === Data saving: API connection ===

        private async void BtnTestSaveApi_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel.IsBusy)
                return;

            var cfg = new ApiConfig
            {
                BaseUrl = string.IsNullOrWhiteSpace(CfgApiUrlBox.Text) ? "http://localhost:5000" : CfgApiUrlBox.Text.Trim()
            };

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
