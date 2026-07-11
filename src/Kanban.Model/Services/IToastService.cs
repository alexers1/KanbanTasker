using KanbanTasker.Model.Dto;
using System;

namespace KanbanTasker.Model.Services
{
    public interface IToastService
    {
        void ScheduleToast(TaskDTO taskDto, DateTimeOffset scheduledTime, DateTimeOffset dueDate);
        void RemoveScheduledToast(string tag);
        void ShowToastNotification(string title, string text);
        void CheckAndShowDueToasts();
    }
}