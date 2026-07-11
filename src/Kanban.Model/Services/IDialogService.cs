using System;
using System.Threading.Tasks;

namespace KanbanTasker.Model.Services
{
    public interface IDialogService
    {
        bool CheckForOpenDialog();
        Task ShowEditBoardDialog(object viewModel);
        Task ShowFirstRunDialog();
        Task ShowSettingsDialog();
        Task ShowCalendarDialog(object viewModel);
        Task ShowBoardListDialog(object viewModel);
    }
}