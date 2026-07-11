using System;
using System.Collections.Generic;
using System.Text;

namespace KanbanTasker.Model.Services
{
    public interface IAppNotificationService
    {
        void DisplayNotificationAsync(string message, int duration);
    }
}