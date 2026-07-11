using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KanbanTasker.Model;
using KanbanTasker.Model.Dto;
using KanbanTasker.Model.Services;
using Kanban_WinUI.Models;
using System;
using System.Collections.ObjectModel;
using System.Linq;

namespace Kanban_WinUI.ViewModels
{
    /// <summary>
    /// BoardViewModel: handles columns, tasks, editing, drag, tags, reminders, dates for a board.
    /// </summary>
    public partial class BoardViewModel : ObservableObject
    {
        private const int NOTIFICATION_DURATION = 3000;
        private const string DEFAULT_COLOR_KEY = "Low";
        private const string DEFAULT_REMINDER_TIME = "None";

        private readonly IServiceManifest _manifest;
        private readonly IAppNotificationService _appNotificationService;
        private readonly IToastService _toastService;
        private readonly ITimerService _timerService;
        private PresentationBoard _board;

        // For cancel revert 1:1
        public PresentationTask OriginalTask { get; set; }

        // For column editor 1:1 (used if flyout sets via VM instead of direct col)
        public string NewColumnName { get; set; } = "";
        public int NewColumnMax { get; set; }

        [ObservableProperty]
        private ObservableCollection<PresentationColumn> _columns = new();

        [ObservableProperty]
        private PresentationTask _currentTask;

        [ObservableProperty]
        private string _paneTitle = "New Task";

        [ObservableProperty]
        private bool _isPaneOpen;

        [ObservableProperty]
        private bool _isColumnCollapsed;

        [ObservableProperty]
        private bool _isPassedDue;

        public ObservableCollection<string> ColorKeys { get; } = new() { "Low", "Medium", "High" };

        public ObservableCollection<string> ReminderTimes { get; } = new() { "None", "At Time of Due Date", "5 Minutes Before", "10 Minutes Before", "15 Minutes Before", "1 Hour Before", "2 Hours Before", "1 Day Before", "2 Days Before" }; // from original

        public ObservableCollection<string> SuggestedTagsCollection { get; } = new();

        [ObservableProperty]
        private DateTimeOffset? _dueDate;

        [ObservableProperty]
        private DateTimeOffset? _startDate;

        [ObservableProperty]
        private DateTimeOffset? _finishDate;

        [ObservableProperty]
        private string _selectedReminderTime = "None";

        [ObservableProperty]
        private string _timeDue = "";  // port for original TimeDue in date info / pane

        [ObservableProperty]
        private bool _isEditingTask;

        [ObservableProperty]
        private bool _isProgressRingActive;

        [ObservableProperty]
        private bool _isPointerEntered;

        public BoardViewModel(PresentationBoard board, IServiceManifest manifest, IAppNotificationService appNotificationService, IToastService toastService, ITimerService timerService)
        {
            _board = board;
            _manifest = manifest;
            _appNotificationService = appNotificationService;
            _toastService = toastService;
            _timerService = timerService;
            CurrentTask = new PresentationTask(new TaskDTO());

            LoadColumnsAndTasks();

            // Timer for date info + due toast checks (wired to global TimerService)
            _timerService.Tick += (s, e) =>
            {
                UpdateDateInformation();
                _toastService.CheckAndShowDueToasts();
                foreach (var task in _board.Tasks)
                    task.NotifyDateDisplaysChanged();  // refresh overdue chips / card age
            };
            _timerService.Start();
        }

        public PresentationBoard Board => _board;

        public ObservableCollection<PresentationTask> Tasks => _board.Tasks;

        public void LoadColumnsAndTasks()
        {
            Columns.Clear();
            var cols = _manifest.BoardServices.GetColumns(_board.Id);
            foreach (var colDto in cols.OrderBy(c => c.Position))
            {
                Columns.Add(new PresentationColumn(colDto));
            }

            // Ensure tasks are loaded on the board (in real, from service)
            if (_board.Tasks.Count == 0)
            {
                var allTasks = _manifest.TaskServices.GetTasks();
                foreach (var t in allTasks.Where(t => t.BoardId == _board.Id).OrderBy(t => t.ColumnIndex))
                {
                    _board.Tasks.Add(new PresentationTask(t));
                }
            }

            // Update per-column task counts for WIP in headers + per-column Tasks collection for native WinUI Kanban (no Syncfusion dependency)
            foreach (var col in Columns)
            {
                col.TaskCount = _board.Tasks.Count(t => t.Category == col.ColumnName);
                col.IsOverLimit = col.TaskCount >= col.MaxTaskLimit;

                col.Tasks.Clear();
                foreach (var t in _board.Tasks.Where(t => t.Category == col.ColumnName).OrderBy(t => t.ColumnIndex))
                    col.Tasks.Add(t);
            }

            RefreshSuggestedTags();
        }

        [RelayCommand]
        private void NewTask(PresentationColumn column)
        {
            // Enforce max task limit (original SfKanban respected column MaximumLimit / max task limit)
            if (column == null || column.TaskCount >= column.MaxTaskLimit) return;

            IsEditingTask = true;
            PaneTitle = "New Task";
            IsPaneOpen = true;

            CurrentTask = new PresentationTask(new TaskDTO { Category = column.ColumnName, BoardId = _board.Id })
            {
                BoardId = _board.Id
            };
            CurrentTask.DateCreated = DateTimeOffset.Now;

            DueDate = DateTimeOffset.Now.AddDays(7);
            StartDate = DateTimeOffset.Now;
            FinishDate = null;
            TimeDue = "12:00";
            SelectedReminderTime = DEFAULT_REMINDER_TIME;
            OriginalTask = null;
            RefreshSuggestedTags();
            UpdateDateInformation();
            PrepareAndScheduleToastNotification();
        }

        [RelayCommand]
        private void SaveTask()
        {
            IsEditingTask = false;
            PaneTitle = "";
            IsPaneOpen = false;

            // 1:1: stop any per-task timer (original had _dateCheckTimer?.Stop())
            // (global timer in this port keeps running for all)

            if (CurrentTask == null)
                return;

            // Sync dates from pane (VM observables) to task for DTO
            CurrentTask.DueDate = DueDate;
            CurrentTask.StartDate = StartDate;
            CurrentTask.FinishDate = FinishDate;
            CurrentTask.TimeDue = TimeDue ?? "";
            CurrentTask.ReminderTime = SelectedReminderTime ?? "";

            var dto = CurrentTask.ToTaskDTO();
            bool isNew = dto.Id == 0;
            if (isNew)
            {
                dto.ColumnIndex = _board.Tasks?.Where(x => x.Category == dto.Category).Count() ?? 0;
                dto.DateCreated = DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss");
            }

            var result = _manifest.TaskServices.SaveTask(dto);
            if (result.Success)
            {
                dto.Id = result.Entity.Id; // ensure
                if (isNew)
                {
                    CurrentTask.Id = dto.Id;
                    CurrentTask.ColumnIndex = dto.ColumnIndex;
                    _board.Tasks.Add(CurrentTask);
                }

                // Refresh counts etc (keeps in-mem consistent)
                LoadColumnsAndTasks();
            }

            if (IsReminderSet(CurrentTask))
                PrepareAndScheduleToastNotification();

            _appNotificationService?.DisplayNotificationAsync("Task saved successfully", NOTIFICATION_DURATION);
        }

        public bool IsReminderSet(PresentationTask task)
        {
            return !string.IsNullOrEmpty(task.DueDate?.ToString()) &&
                   task.TimeDue != null &&
                   task.ReminderTime != DEFAULT_REMINDER_TIME &&
                   task.ReminderTime != "";
        }

        /// <summary>
        /// Schedules a toast notification using <see cref="IToastService"/> if the current task 
        /// has a selected due date, time due, reminder time when called.
        /// </summary>
        private void PrepareAndScheduleToastNotification()
        {
            var dueDate = CurrentTask.DueDate;
            var timeDueStr = CurrentTask.TimeDue;
            string reminderTime = CurrentTask.ReminderTime ?? "None";

            if (reminderTime.Equals(DEFAULT_REMINDER_TIME) || string.IsNullOrEmpty(reminderTime))
            {
                _toastService.RemoveScheduledToast(CurrentTask.Id.ToString());
                return;
            }

            if (dueDate == null)
                return;

            DateTimeOffset? timeDue = null;
            if (!string.IsNullOrEmpty(timeDueStr) && TimeSpan.TryParse(timeDueStr, out var ts))
            {
                timeDue = dueDate.Value.Date + ts;
            }
            else
            {
                timeDue = dueDate;
            }

            if (timeDue == null)
                return;

            var taskDueDate = timeDue.Value;

            var scheduledTime = taskDueDate;
            switch (reminderTime)
            {
                case "At Time of Due Date":
                    break;
                case "5 Minutes Before":
                    scheduledTime = taskDueDate.AddMinutes(-5);
                    break;
                case "10 Minutes Before":
                    scheduledTime = taskDueDate.AddMinutes(-10);
                    break;
                case "15 Minutes Before":
                    scheduledTime = taskDueDate.AddMinutes(-15);
                    break;
                case "1 Hour Before":
                    scheduledTime = taskDueDate.AddHours(-1);
                    break;
                case "2 Hours Before":
                    scheduledTime = taskDueDate.AddHours(-2);
                    break;
                case "1 Day Before":
                    scheduledTime = taskDueDate.AddDays(-1);
                    break;
                case "2 Days Before":
                    scheduledTime = taskDueDate.AddDays(-2);
                    break;
            }

            if (scheduledTime < DateTimeOffset.Now)
                scheduledTime = DateTimeOffset.Now.AddSeconds(5); // avoid past

            _toastService.ScheduleToast(CurrentTask.ToTaskDTO(), scheduledTime, taskDueDate);
        }

        private bool CheckIfPastDue()
        {
            // Use the pane's live values (DueDate/TimeDue observables) so the
            // warning updates while the user is editing, before saving.
            var dueDate = DueDate ?? CurrentTask?.DueDate;
            if (dueDate == null) return false;

            var timeDueStr = string.IsNullOrEmpty(TimeDue) ? CurrentTask?.TimeDue : TimeDue;
            DateTimeOffset checkTime = dueDate.Value;
            if (!string.IsNullOrEmpty(timeDueStr) && TimeSpan.TryParse(timeDueStr, out var ts))
            {
                checkTime = dueDate.Value.Date + ts;
            }
            return checkTime < DateTimeOffset.Now;
        }

        partial void OnDueDateChanged(DateTimeOffset? value) => IsPassedDue = CheckIfPastDue();

        partial void OnTimeDueChanged(string value) => IsPassedDue = CheckIfPastDue();

        /// <summary>True when the pane's finish date is earlier than its start date (validation hint).</summary>
        public bool IsFinishBeforeStart =>
            StartDate.HasValue && FinishDate.HasValue && FinishDate.Value.Date < StartDate.Value.Date;

        partial void OnStartDateChanged(DateTimeOffset? value) => OnPropertyChanged(nameof(IsFinishBeforeStart));

        partial void OnFinishDateChanged(DateTimeOffset? value) => OnPropertyChanged(nameof(IsFinishBeforeStart));

        private void UpdateDateInformation()
        {
            IsPassedDue = CheckIfPastDue();

            DateTimeOffset? dateCreated = CurrentTask.DateCreated;
            DateTimeOffset? today = DateTimeOffset.Now;
            DateTimeOffset? startDate = CurrentTask.StartDate;
            DateTimeOffset? finishDate = CurrentTask.FinishDate;

            // 1:1 set DaysWorkedOn (from start) and full DaysSinceCreation (d,hrs,min)
            if (startDate != null)
            {
                TimeSpan? timeSpan = today - startDate;
                if (timeSpan != null)
                    CurrentTask.DaysWorkedOn = $"{timeSpan.Value.Days} day(s)";
            }

            if (dateCreated != null && today != null)
            {
                TimeSpan? timeSpan = today - dateCreated;
                if (timeSpan != null)
                {
                    CurrentTask.DaysSinceCreation = string.Format(
                        "{0}d, {1}hrs, {2}min",
                        timeSpan.Value.Days,
                        timeSpan.Value.Hours,
                        timeSpan.Value.Minutes);
                }
            }
        }

        [RelayCommand]
        public void UpdateColumn(string originalName)
        {
            // 1:1 full from original UpdateColumn
            string newColName = NewColumnName;
            int newColMax = NewColumnMax;

            var column = Columns.FirstOrDefault(c => c.ColumnName.Equals(originalName));
            if (column == null)
                column = Columns.FirstOrDefault(c => c.ColumnName.Equals(newColName)); // in case live bind already mutated the name on the col ref
            if (column == null) return;

            if (string.IsNullOrEmpty(newColName))
                newColName = originalName;

            // Set new column name and task limits (in case flyout/VM state used)
            column.ColumnName = newColName;
            column.MaxTaskLimit = newColMax;

            // Update column in database
            _manifest.BoardServices.SaveColumn(column.ToColumnDTO());

            // Update categories for tasks in the column (propagate rename)
            var tasksCopy = Board.Tasks.ToList();
            foreach (PresentationTask task in tasksCopy)
            {
                if (task.Category == originalName)
                {
                    task.Category = newColName;
                    _manifest.TaskServices.UpdateColumnName(task.Id, task.Category);
                }
            }
            Board.Tasks = new ObservableCollection<PresentationTask>(tasksCopy.OrderBy(x => x.ColumnIndex));

            // Update category if creating new task in pane
            if (IsPaneOpen && PaneTitle.Equals("New Task"))
            {
                if (CurrentTask.Category.Equals(originalName))
                    CurrentTask.Category = newColName;
            }

            NewColumnName = "";
            LoadColumnsAndTasks();
        }

        public void UpdateCardColumn(string targetCategory, PresentationTask selectedCardModel, int targetIndex)
        {
            var task = selectedCardModel; // or find in collection
            task.Category = targetCategory;
            task.ColumnIndex = targetIndex;

            _manifest.TaskServices.UpdateColumnData(task.ToTaskDTO());

            // Refresh
            LoadColumnsAndTasks();
        }

        public void UpdateCardIndex(int id, int currentCardIndex)
        {
            _manifest.TaskServices.UpdateCardIndex(id, currentCardIndex);
            // Refresh or update local
            LoadColumnsAndTasks();
        }

        // Move task to adjacent column (for non-drag buttons)
        public void MoveTaskToColumn(PresentationTask task, int direction)
        {
            if (task == null || Columns.Count == 0) return;

            var currentCol = Columns.FirstOrDefault(c => c.ColumnName == task.Category);
            if (currentCol == null) return;

            int currentPos = Columns.IndexOf(currentCol);
            int newPos = currentPos + direction;
            if (newPos < 0 || newPos >= Columns.Count) return;

            var newCol = Columns[newPos];
            task.Category = newCol.ColumnName;
            task.ColumnIndex = 0; // reset index for simplicity

            _manifest.TaskServices.UpdateColumnData(task.ToTaskDTO());
            LoadColumnsAndTasks();
        }

        // For column header buttons
        [RelayCommand]
        public void NewTaskForColumn(PresentationColumn column)
        {
            NewTask(column);
        }

        [RelayCommand]
        public void MoveTaskLeft(PresentationTask task)
        {
            MoveTaskToColumn(task, -1);
        }

        [RelayCommand]
        public void MoveTaskRight(PresentationTask task)
        {
            MoveTaskToColumn(task, 1);
        }

        [RelayCommand]
        public void EditTask(int id)
        {
            IsEditingTask = true;
            PaneTitle = "Edit Task";
            IsPaneOpen = true;

            var task = Board.Tasks.FirstOrDefault(t => t.Id == id);
            if (task != null)
            {
                CurrentTask = task;
                // sync dates to pane
                DueDate = task.DueDate;
                StartDate = task.StartDate;
                FinishDate = task.FinishDate;
                TimeDue = task.TimeDue ?? "";
                SelectedReminderTime = task.ReminderTime ?? DEFAULT_REMINDER_TIME;

                RefreshSuggestedTags();
                UpdateDateInformation();

                // clone a copy of CurrentTask so we can restore if user cancels (1:1)
                OriginalTask = new PresentationTask(CurrentTask.ToTaskDTO());

                PrepareAndScheduleToastNotification();
            }
        }

        [RelayCommand]
        public void DeleteTask(int id)
        {
            // 1:1 stop timer if
            var task = Board.Tasks.FirstOrDefault(t => t.Id == id);
            if (task == null) return;

            var result = _manifest.TaskServices.DeleteTask(id);

            if (result.Success)
            {
                Board.Tasks.Remove(task);
                UpdateCardIndexesAfterDeletion(task);
                CurrentTask = Board.Tasks.LastOrDefault();

                if (IsReminderSet(task))
                    _toastService.RemoveScheduledToast(id.ToString());

                _appNotificationService?.DisplayNotificationAsync("Task deleted successfully", NOTIFICATION_DURATION);
            }
            else
            {
                _appNotificationService?.DisplayNotificationAsync("Task failed to be deleted. Please try again or restart the application.", NOTIFICATION_DURATION);
            }

            LoadColumnsAndTasks();
        }

        private void UpdateCardIndexesAfterDeletion(PresentationTask deletedTask)
        {
            int startIndex = deletedTask.ColumnIndex;

            // If we do not sort by ColumnIndex,
            // the tasks in Board.Tasks will be in unsorted order when assigning startIndex 
            var sortedTasks = Board.Tasks
                .Where(x => x.Category == deletedTask.Category && x.ColumnIndex > deletedTask.ColumnIndex)
                .OrderBy(x => x.ColumnIndex);

            foreach (PresentationTask otherTask in sortedTasks)
            {
                otherTask.ColumnIndex = startIndex++;
                UpdateCardIndex(otherTask.Id, otherTask.ColumnIndex);
            }
        }

        public void DeleteColumn(PresentationColumn col)
        {
            if (col == null) return;

            // 1:1: original DeleteColumn also reindexes positions of remaining columns
            var result = _manifest.BoardServices.DeleteColumn(col.ToColumnDTO());
            if (result.Success)
            {
                Columns.Remove(col);

                // Update other columns positions
                var deletedItemsPosition = col.Position;
                for (int i = deletedItemsPosition; i < Columns.Count; i++)
                {
                    var column = Columns[i];
                    column.Position -= 1;
                    _manifest.BoardServices.UpdateColumnIndex(column.ToColumnDTO());
                }

                _appNotificationService?.DisplayNotificationAsync("Column deleted successfully", NOTIFICATION_DURATION);
            }

            LoadColumnsAndTasks();
        }

        public PresentationColumn CreateColumn(KanbanTasker.Model.Dto.ColumnDTO colDto)
        {
            var col = new PresentationColumn(colDto);
            // Use service (old had CreateColumn)
            _manifest.BoardServices.CreateColumn(colDto);
            Columns.Add(col);
            return col;
        }

        [RelayCommand]
        private void DeleteCurrentTask()
        {
            if (CurrentTask != null)
            {
                DeleteTask(CurrentTask.Id);
                IsPaneOpen = false;
            }
        }

        [RelayCommand]
        public void CancelEdit()
        {
            // Revert CurrentTask to OriginalTask clone (for edit cancel), re-order, reschedule toast if had reminder
            IsProgressRingActive = true;
            IsEditingTask = false;
            PaneTitle = "";
            IsPaneOpen = false;

            if (OriginalTask != null)
            {
                int index = Board.Tasks.IndexOf(CurrentTask);
                if (index >= 0)
                {
                    var tempTask = new PresentationTask(OriginalTask.ToTaskDTO());

                    // Update category if column name was changed while creating/editing
                    if (CurrentTask.Category != OriginalTask.Category)
                        tempTask.Category = CurrentTask.Category;

                    Board.Tasks[index] = tempTask;
                    Board.Tasks = new ObservableCollection<PresentationTask>(
                        Board.Tasks.OrderBy(x => x.ColumnIndex));

                    // Check if a toast notification was deleted (restore)
                    if (OriginalTask.ReminderTime != DEFAULT_REMINDER_TIME)
                        PrepareAndScheduleToastNotification();
                }
            }

            IsProgressRingActive = false;
        }

        [RelayCommand]
        public void ToggleColumnCollapse()
        {
            IsColumnCollapsed = !IsColumnCollapsed;
            // In full with SfKanban or custom, would hide/show the column UI
        }

        [RelayCommand]
        public void DeleteTag(string tag)
        {
            if (CurrentTask == null)
            {
                _appNotificationService?.DisplayNotificationAsync("Tag failed to be deleted. CurrentTask is null. Please try again or restart the application.", NOTIFICATION_DURATION);
                return;
            }

            var result = CurrentTask.Tags.Remove(tag);
            var message = result ? "Tag deleted successfully" : "Tag failed to be deleted";
            _appNotificationService?.DisplayNotificationAsync(message, NOTIFICATION_DURATION);
            if (result)
            {
                CurrentTask.NotifyTagsInputChanged();
                RefreshSuggestedTags();
            }
        }

        /// <summary>
        /// 1:1 port of original AddTag: adds unique, shows notif, updates board collection + suggestions.
        /// </summary>
        public bool AddTag(string tag)
        {
            if (CurrentTask == null || string.IsNullOrWhiteSpace(tag)) return false;
            tag = tag.Trim();

            if (CurrentTask.Tags.Contains(tag))
            {
                _appNotificationService?.DisplayNotificationAsync("Tag already exists", NOTIFICATION_DURATION);
                return false;
            }

            CurrentTask.Tags.Add(tag);
            if (!_board.TagsCollection.Contains(tag))
                _board.TagsCollection.Add(tag);

            if (SuggestedTagsCollection.Contains(tag))
                SuggestedTagsCollection.Remove(tag);

            _appNotificationService?.DisplayNotificationAsync($"Tag {tag} added successfully", NOTIFICATION_DURATION);
            CurrentTask.NotifyTagsInputChanged();
            return true;
        }

        private void RefreshSuggestedTags()
        {
            SuggestedTagsCollection.Clear();
            var source = _board.TagsCollection.Any() ? _board.TagsCollection : _board.Tasks.SelectMany(t => t.Tags);
            foreach (var t in source.Where(tt => !string.IsNullOrWhiteSpace(tt)).Distinct().OrderBy(t => t))
            {
                if (CurrentTask?.Tags == null || !CurrentTask.Tags.Contains(t))
                    SuggestedTagsCollection.Add(t);
            }
            if (SuggestedTagsCollection.Count == 0)
            {
                // defaults for better UX in sim
                SuggestedTagsCollection.Add("bug");
                SuggestedTagsCollection.Add("feature");
                SuggestedTagsCollection.Add("urgent");
            }
        }

        // Add more as needed for full port (e.g., suggested tags, AutoSuggest handlers, full timers)
    }
}