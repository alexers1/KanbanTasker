using System;

namespace KanbanTasker.Model.Services
{
    public interface INavigationService
    {
        object Frame { get; set; }
        void NavigateTo(Type type);
        void NavigateTo(Type type, object parameter);
        void NavigateToDefaultView();
        void NavigateToBoard(object boardViewModel);
    }
}