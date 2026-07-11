using KanbanTasker.Model.Services;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Threading.Tasks;
using Kanban_WinUI.Views.Dialogs;

namespace Kanban_WinUI.Services
{
    /// <summary>
    /// Dialog service for FirstRun, AppUpdated, Settings, EditBoard, Calendar, BoardList.
    /// Uses ContentDialog which is modern in WinUI3.
    /// </summary>
    public class DialogService : IDialogService
    {
        private bool _isDialogOpen;

        public bool CheckForOpenDialog()
        {
            return _isDialogOpen;
        }

        public async Task ShowDialog(ContentDialog dialog)
        {
            if (_isDialogOpen)
                return;

            // WinUI 3: a ContentDialog must have a XamlRoot or ShowAsync throws.
            dialog.XamlRoot ??= App.MainAppWindow?.Content?.XamlRoot;
            if (dialog.XamlRoot == null)
                return;

            // Dialogs do not follow runtime theme changes on their own — without this
            // they stay light after the user switches the app to dark mode.
            dialog.RequestedTheme = ThemeHelpers.CurrentTheme;

            _isDialogOpen = true;
            try
            {
                await dialog.ShowAsync();
            }
            finally
            {
                _isDialogOpen = false;
            }
        }

        public async Task ShowEditBoardDialog(object viewModel)
        {
            var dialog = new Views.Dialogs.EditBoardDialog() { DataContext = viewModel };
            await ShowDialog(dialog);
        }

        public async Task ShowFirstRunDialog()
        {
            var dialog = new Views.Dialogs.FirstRunDialog();
            await ShowDialog(dialog);
        }

        public async Task ShowSettingsDialog()
        {
            var dialog = new Views.Dialogs.SettingsDialog();
            await ShowDialog(dialog);
        }

        public async Task ShowCalendarDialog(object viewModel)
        {
            var dialog = new Views.Dialogs.CalendarDialog(viewModel);
            await ShowDialog(dialog);
        }

        public async Task ShowBoardListDialog(object viewModel)
        {
            var dialog = new Views.Dialogs.BoardListDialog() { DataContext = viewModel };
            await ShowDialog(dialog);
        }
    }
}