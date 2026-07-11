using System;
using System.Linq;
using Kanban_WinUI.Models;
using Kanban_WinUI.ViewModels;
using KanbanTasker.Model.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Kanban_WinUI.Views.Dialogs
{
    /// <summary>
    /// Task Calendar: shows the current board's tasks due on the selected date.
    /// Clicking a task closes the dialog and opens it in the board's edit pane (1:1 with original).
    /// </summary>
    public sealed partial class CalendarDialog : ContentDialog
    {
        public MainViewModel? ViewModel { get; }
        public CalendarViewModel CalendarViewModel { get; }

        private PresentationTask? _selectedTask;
        private DispatcherTimer? _clockTimer;

        public CalendarDialog(object? viewModel = null)
        {
            InitializeComponent();

            ViewModel = viewModel as MainViewModel;
            CalendarViewModel = new CalendarViewModel(App.GetService<ITimerService>());

            Closing += CalendarDialog_Closing;
        }

        private void CalendarDialog_Loaded(object sender, RoutedEventArgs e)
        {
            // Select today; triggers SelectedDatesChanged which fills the task list
            TaskCalendarView.SelectedDates.Add(DateTimeOffset.Now);

            // Live clock (the shared app timer only ticks once a minute)
            _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _clockTimer.Tick += (s, args) => CalendarViewModel.CurrentTime = DateTime.Now;
            _clockTimer.Start();
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Hide();
        }

        private void TaskCalendarView_SelectedDatesChanged(CalendarView sender, CalendarViewSelectedDatesChangedEventArgs args)
        {
            if (ViewModel?.CurrentBoard == null)
                return;

            if (sender.SelectedDates != null && sender.SelectedDates.Count != 0)
                CalendarViewModel.SelectedDate = sender.SelectedDates.First();

            CalendarViewModel.RefreshScheduledTasks(ViewModel.CurrentBoard.Board);
        }

        private void LstView_ItemClick(object sender, ItemClickEventArgs e)
        {
            _selectedTask = e.ClickedItem as PresentationTask;
            Hide();
        }

        private void CalendarDialog_Closing(ContentDialog sender, ContentDialogClosingEventArgs args)
        {
            _clockTimer?.Stop();
            CalendarViewModel.Cleanup();

            if (_selectedTask != null && ViewModel?.CurrentBoard != null)
            {
                // Open the clicked task in the board's edit pane
                ViewModel.CurrentBoard.EditTask(_selectedTask.Id);
                _selectedTask = null;
            }
        }
    }
}
