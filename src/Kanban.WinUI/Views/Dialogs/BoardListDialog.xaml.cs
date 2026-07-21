using System.Linq;
using Kanban_WinUI.ViewModels;
using KanbanTasker.Model;
using KanbanTasker.Model.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Kanban_WinUI.Views.Dialogs
{
    /// <summary>
    /// Board list editor: shows all boards with notes, supports multi-select delete
    /// (ported from the original UWP BoardListDialog).
    /// </summary>
    public sealed partial class BoardListDialog : ContentDialog
    {
        private const int MessageDuration = 3000;

        public MainViewModel? ViewModel => DataContext as MainViewModel;

        public BoardListDialog()
        {
            InitializeComponent();
        }

        private void BoardListDialog_Loaded(object sender, RoutedEventArgs e)
        {
            // Set here instead of x:Bind: DataContext is assigned after construction by DialogService
            BoardListView.ItemsSource = ViewModel?.BoardList;
            UpdateEmptyState();
        }

        private void UpdateEmptyState()
        {
            bool empty = (ViewModel?.BoardList?.Count ?? 0) == 0;
            NoBoardsTextBlock.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
            BoardListView.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
        }

        private async void DeleteItemsBtn_Click(object sender, RoutedEventArgs e)
        {
            if (DeleteItemsBtn.Flyout is Flyout flyout)
                flyout.Hide();

            var notifications = App.GetService<IAppNotificationService>();

            var selectedItems = BoardListView.SelectedItems.Cast<BoardViewModel>().ToList();
            if (ViewModel == null || selectedItems.Count == 0)
            {
                notifications.DisplayNotificationAsync("Select board item(s) to delete", MessageDuration);
                return;
            }

            var manifest = App.GetService<IServiceManifest>();
            bool currentDeleted = ViewModel.CurrentBoard != null && selectedItems.Contains(ViewModel.CurrentBoard);

            foreach (var boardVM in selectedItems)
            {
                await manifest.BoardServices.DeleteBoardAsync(boardVM.Board.Id);
                ViewModel.BoardList.Remove(boardVM);
            }

            if (currentDeleted)
            {
                ViewModel.CurrentBoard = null;
                ViewModel.CurrentBoard = ViewModel.BoardList.LastOrDefault();
            }

            UpdateEmptyState();
            notifications.DisplayNotificationAsync("Deletion of board(s) successful", MessageDuration);
        }

        private void CloseDialog_Click(object sender, RoutedEventArgs e)
        {
            Hide();
        }
    }
}
