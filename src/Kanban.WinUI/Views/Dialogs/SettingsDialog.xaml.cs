using System;
using Kanban_WinUI.ViewModels;
using KanbanTasker.Model.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;

namespace Kanban_WinUI.Views.Dialogs
{
    /// <summary>
    /// Settings: export a SQL Server backup (.bak) of the database to a chosen file.
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
        }

        private void BtnCloseSettings_Click(object sender, RoutedEventArgs e)
        {
            Hide();
        }

        private async void BtnImport_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel.IsBusy || App.MainAppWindow == null)
                return;

            var picker = new Windows.Storage.Pickers.FileOpenPicker();
            picker.FileTypeFilter.Add(".bak");
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainAppWindow);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            var file = await picker.PickSingleFileAsync();
            if (file != null)
                await ViewModel.ImportFromAsync(file.Path);
        }

        private async void BtnArchive_Click(object sender, RoutedEventArgs e)
        {
            if (!ViewModel.IsBusy)
                await ViewModel.ArchiveCompletedAsync();
        }

        private async void BtnMigrateAttachments_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel.IsBusy || App.MainAppWindow == null)
                return;

            var picker = new Windows.Storage.Pickers.FolderPicker();
            picker.FileTypeFilter.Add("*");
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainAppWindow);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            var folder = await picker.PickSingleFolderAsync();
            if (folder != null)
                await ViewModel.MigrateAttachmentsAsync(folder.Path);
        }

        private async void BtnBackup_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel.IsBusy || App.MainAppWindow == null)
                return;

            var picker = new FileSavePicker();
            picker.FileTypeChoices.Add("SQL Server backup", new[] { ".bak" });
            picker.SuggestedFileName = $"KanbanTasker_{DateTime.Now:yyyy-MM-dd}";
            picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;

            // WinUI 3 desktop: pickers must be bound to the window handle
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainAppWindow);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            var file = await picker.PickSaveFileAsync();
            if (file == null)
                return;

            await ViewModel.BackupToAsync(file.Path);
        }
    }
}
