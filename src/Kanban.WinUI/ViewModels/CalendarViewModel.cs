using CommunityToolkit.Mvvm.ComponentModel;
using Kanban_WinUI.Models;
using KanbanTasker.Model.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Kanban_WinUI.ViewModels
{
    /// <summary>
    /// Backs the Task Calendar dialog: tracks the selected date and the tasks due on it.
    /// Ported from the original UWP CalendarViewModel (DueDate is now a DateTimeOffset?).
    /// </summary>
    public partial class CalendarViewModel : ObservableObject
    {
        private readonly ITimerService _timerService;

        [ObservableProperty]
        private DateTime _currentTime = DateTime.Now;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(SelectedDateDisplay))]
        private DateTimeOffset _selectedDate = DateTimeOffset.Now;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasNoResults))]
        private ObservableCollection<PresentationTask> _scheduledTasks = new();

        public string SelectedDateDisplay => SelectedDate.ToString("dddd, MMMM d");

        public bool HasNoResults => ScheduledTasks.Count == 0;

        public CalendarViewModel(ITimerService timerService)
        {
            _timerService = timerService;
            _timerService.Tick += OnTick;
            _timerService.Start();
        }

        private void OnTick(object? sender, EventArgs e) => CurrentTime = DateTime.Now;

        /// <summary>
        /// Detach from the shared timer. The timer itself keeps running because the
        /// board view models rely on it for reminder/toast checks.
        /// </summary>
        public void Cleanup() => _timerService.Tick -= OnTick;

        /// <summary>
        /// Fills <see cref="ScheduledTasks"/> with the board's tasks due on the
        /// selected date, sorted by time due (1:1 with original GetAvailableTasks).
        /// </summary>
        public void RefreshScheduledTasks(PresentationBoard? board)
        {
            var due = board?.Tasks?
                .Where(t => t.DueDate.HasValue &&
                            t.DueDate.Value.Year == SelectedDate.Year &&
                            t.DueDate.Value.Month == SelectedDate.Month &&
                            t.DueDate.Value.Day == SelectedDate.Day)
                .OrderBy(t => t.TimeDue)
                .ToList() ?? new List<PresentationTask>();

            ScheduledTasks = new ObservableCollection<PresentationTask>(due);
        }
    }
}
