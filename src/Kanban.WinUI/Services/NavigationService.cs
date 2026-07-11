using KanbanTasker.Model.Services;
using Microsoft.UI.Xaml.Controls;
using System;

namespace Kanban_WinUI.Services
{
    /// <summary>
    /// Navigation service (Frame based, for BoardView etc).
    /// Uses Frame for navigation between pages like BoardView, etc.
    /// </summary>
    public class NavigationService : INavigationService
    {
        public object Frame { get; set; } = null!;  // match interface, initialized for nullability

        public void NavigateTo(Type type)
        {
            if (Frame is Frame frame)
            {
                frame.Navigate(type);
            }
        }

        public void NavigateTo(Type type, object parameter)
        {
            if (Frame is Frame frame)
            {
                frame.Navigate(type, parameter);
            }
        }

        public void NavigateToDefaultView()
        {
            if (Frame is Frame frame)
            {
                // Will create NoBoardsAvailableView equivalent
                frame.Navigate(typeof(Views.NoBoardsAvailableView));
            }
        }

        public void NavigateToBoard(object boardViewModel)
        {
            if (Frame is Frame frame)
            {
                frame.Navigate(typeof(Views.BoardView), boardViewModel);
            }
        }
    }
}