using KanbanTasker.Model.Services;
using Microsoft.UI.Xaml;
using System;

namespace Kanban_WinUI.Services
{
    /// <summary>
    /// Timer service using DispatcherTimer (for date updates and toast checks).
    /// </summary>
    public class TimerService : ITimerService
    {
        private DispatcherTimer? _timer;

        public event EventHandler? Tick;

        public void Start()
        {
            if (_timer == null)
            {
                _timer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
                _timer.Tick += (s, e) => Tick?.Invoke(this, EventArgs.Empty);
            }
            _timer.Start();
        }

        public void Stop()
        {
            _timer?.Stop();
        }
    }
}