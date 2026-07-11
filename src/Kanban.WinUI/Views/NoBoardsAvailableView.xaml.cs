using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Linq;

namespace Kanban_WinUI.Views
{
    public sealed partial class NoBoardsAvailableView : Page
    {
        public NoBoardsAvailableView()
        {
            this.InitializeComponent();
        }

        private async void OnCreateBoard(object sender, RoutedEventArgs e)
        {
            if (App.MainAppWindow is not MainWindow main || main.ViewModel == null)
                return;

            await main.ViewModel.NewBoard();

            // If the user saved the new board, show it
            var board = main.ViewModel.BoardList.LastOrDefault();
            if (board != null)
            {
                main.ViewModel.CurrentBoard = board;
                Frame?.Navigate(typeof(BoardView), board);
            }
        }
    }
}