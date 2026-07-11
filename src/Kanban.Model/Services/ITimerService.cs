using System;

namespace KanbanTasker.Model.Services
{
    public interface ITimerService
    {
        event EventHandler? Tick;
        void Start();
        void Stop();
    }
}