using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KanbanTasker.Model;
using KanbanTasker.Model.Dto;
using KanbanTasker.Model.Services;
using Kanban_WinUI.Models;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace Kanban_WinUI.ViewModels
{
    /// <summary>
    /// BoardViewModel: columns, tasks, editing, drag, tags, reminders, dates for a board.
    /// Data access is async over the REST API — <see cref="InitializeAsync"/> loads the
    /// board's columns and tasks (the constructor no longer performs network I/O).
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

        public PresentationTask OriginalTask { get; set; }

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

        public ObservableCollection<string> ReminderTimes { get; } = new() { "None", "At Time of Due Date", "5 Minutes Before", "10 Minutes Before", "15 Minutes Before", "1 Hour Before", "2 Hours Before", "1 Day Before", "2 Days Before" };

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
        private string _timeDue = "";

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

            // No network I/O in the constructor — the owner calls InitializeAsync().

            _timerService.Tick += (s, e) =>
            {
                UpdateDateInformation();
                _toastService.CheckAndShowDueToasts();
                foreach (var task in _board.Tasks)
                    task.NotifyDateDisplaysChanged();
            };
            _timerService.Start();
        }

        public PresentationBoard Board => _board;

        public ObservableCollection<PresentationTask> Tasks => _board.Tasks;

        /// <summary>Loads columns and tasks for this board from the API.</summary>
        public Task InitializeAsync() => LoadColumnsAndTasksAsync();

        public async Task LoadColumnsAndTasksAsync()
        {
            Columns.Clear();
            var cols = await _manifest.BoardServices.GetColumnsAsync(_board.Id);
            foreach (var colDto in cols.OrderBy(c => c.Position))
                Columns.Add(new PresentationColumn(colDto));

            if (_board.Tasks.Count == 0)
            {
                var allTasks = await _manifest.TaskServices.GetTasksAsync();
                foreach (var t in allTasks.Where(t => t.BoardId == _board.Id).OrderBy(t => t.ColumnIndex))
                    _board.Tasks.Add(new PresentationTask(t));
            }

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
        private async Task SaveTaskAsync()
        {
            IsEditingTask = false;
            PaneTitle = "";
            IsPaneOpen = false;

            if (CurrentTask == null)
                return;

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

            var result = await _manifest.TaskServices.SaveTaskAsync(dto);
            if (result.Success)
            {
                dto.Id = result.Entity.Id;
                if (isNew)
                {
                    CurrentTask.Id = dto.Id;
                    CurrentTask.ColumnIndex = dto.ColumnIndex;
                    _board.Tasks.Add(CurrentTask);
                }

                await LoadColumnsAndTasksAsync();
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

            DateTimeOffset? timeDue;
            if (!string.IsNullOrEmpty(timeDueStr) && TimeSpan.TryParse(timeDueStr, out var ts))
                timeDue = dueDate.Value.Date + ts;
            else
                timeDue = dueDate;

            if (timeDue == null)
                return;

            var taskDueDate = timeDue.Value;
            var scheduledTime = taskDueDate;
            switch (reminderTime)
            {
                case "At Time of Due Date": break;
                case "5 Minutes Before": scheduledTime = taskDueDate.AddMinutes(-5); break;
                case "10 Minutes Before": scheduledTime = taskDueDate.AddMinutes(-10); break;
                case "15 Minutes Before": scheduledTime = taskDueDate.AddMinutes(-15); break;
                case "1 Hour Before": scheduledTime = taskDueDate.AddHours(-1); break;
                case "2 Hours Before": scheduledTime = taskDueDate.AddHours(-2); break;
                case "1 Day Before": scheduledTime = taskDueDate.AddDays(-1); break;
                case "2 Days Before": scheduledTime = taskDueDate.AddDays(-2); break;
            }

            if (scheduledTime < DateTimeOffset.Now)
                scheduledTime = DateTimeOffset.Now.AddSeconds(5);

            _toastService.ScheduleToast(CurrentTask.ToTaskDTO(), scheduledTime, taskDueDate);
        }

        private bool CheckIfPastDue()
        {
            var dueDate = DueDate ?? CurrentTask?.DueDate;
            if (dueDate == null) return false;

            var timeDueStr = string.IsNullOrEmpty(TimeDue) ? CurrentTask?.TimeDue : TimeDue;
            DateTimeOffset checkTime = dueDate.Value;
            if (!string.IsNullOrEmpty(timeDueStr) && TimeSpan.TryParse(timeDueStr, out var ts))
                checkTime = dueDate.Value.Date + ts;
            return checkTime < DateTimeOffset.Now;
        }

        partial void OnDueDateChanged(DateTimeOffset? value) => IsPassedDue = CheckIfPastDue();

        partial void OnTimeDueChanged(string value) => IsPassedDue = CheckIfPastDue();

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
                    CurrentTask.DaysSinceCreation = string.Format(
                        "{0}d, {1}hrs, {2}min",
                        timeSpan.Value.Days, timeSpan.Value.Hours, timeSpan.Value.Minutes);
            }
        }

        [RelayCommand]
        public async Task UpdateColumnAsync(string originalName)
        {
            string newColName = NewColumnName;
            int newColMax = NewColumnMax;

            var column = Columns.FirstOrDefault(c => c.ColumnName.Equals(originalName))
                         ?? Columns.FirstOrDefault(c => c.ColumnName.Equals(newColName));
            if (column == null) return;

            if (string.IsNullOrEmpty(newColName))
                newColName = originalName;

            column.ColumnName = newColName;
            column.MaxTaskLimit = newColMax;

            // The server persists the rename and cascades it to this column's tasks
            // (their Category) in one operation — no per-task calls from the client.
            await _manifest.BoardServices.SaveColumnAsync(column.ToColumnDTO());

            // Keep the in-memory cache consistent with the server's cascade.
            foreach (PresentationTask task in Board.Tasks.Where(t => t.Category == originalName).ToList())
                task.Category = newColName;

            if (IsPaneOpen && PaneTitle.Equals("New Task") && CurrentTask.Category.Equals(originalName))
                CurrentTask.Category = newColName;

            NewColumnName = "";
            await LoadColumnsAndTasksAsync();
        }

        /// <summary>Move a card to another column (drag drop across columns), then refresh.</summary>
        public async Task UpdateCardColumnAsync(string targetCategory, PresentationTask selectedCardModel, int targetIndex)
        {
            selectedCardModel.Category = targetCategory;
            selectedCardModel.ColumnIndex = targetIndex;

            await _manifest.TaskServices.UpdateColumnDataAsync(selectedCardModel.ToTaskDTO());
            await LoadColumnsAndTasksAsync();
        }

        /// <summary>Persist a card's index without reloading (used for batch reorder).</summary>
        public Task SaveCardIndexAsync(int id, int currentCardIndex) =>
            _manifest.TaskServices.UpdateCardIndexAsync(id, currentCardIndex);

        private async Task MoveTaskToColumnAsync(PresentationTask task, int direction)
        {
            if (task == null || Columns.Count == 0) return;

            var currentCol = Columns.FirstOrDefault(c => c.ColumnName == task.Category);
            if (currentCol == null) return;

            int currentPos = Columns.IndexOf(currentCol);
            int newPos = currentPos + direction;
            if (newPos < 0 || newPos >= Columns.Count) return;

            var newCol = Columns[newPos];
            task.Category = newCol.ColumnName;
            task.ColumnIndex = 0;

            await _manifest.TaskServices.UpdateColumnDataAsync(task.ToTaskDTO());
            await LoadColumnsAndTasksAsync();
        }

        [RelayCommand]
        public void NewTaskForColumn(PresentationColumn column) => NewTask(column);

        [RelayCommand]
        public Task MoveTaskLeftAsync(PresentationTask task) => MoveTaskToColumnAsync(task, -1);

        [RelayCommand]
        public Task MoveTaskRightAsync(PresentationTask task) => MoveTaskToColumnAsync(task, 1);

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
                DueDate = task.DueDate;
                StartDate = task.StartDate;
                FinishDate = task.FinishDate;
                TimeDue = task.TimeDue ?? "";
                SelectedReminderTime = task.ReminderTime ?? DEFAULT_REMINDER_TIME;

                RefreshSuggestedTags();
                UpdateDateInformation();

                OriginalTask = new PresentationTask(CurrentTask.ToTaskDTO());
                PrepareAndScheduleToastNotification();
            }
        }

        [RelayCommand]
        public async Task DeleteTaskAsync(int id)
        {
            var task = Board.Tasks.FirstOrDefault(t => t.Id == id);
            if (task == null) return;

            var result = await _manifest.TaskServices.DeleteTaskAsync(id);

            if (result.Success)
            {
                // The server renumbers the column's remaining ColumnIndex values.
                Board.Tasks.Remove(task);
                CurrentTask = Board.Tasks.LastOrDefault();

                if (IsReminderSet(task))
                    _toastService.RemoveScheduledToast(id.ToString());

                _appNotificationService?.DisplayNotificationAsync("Task deleted successfully", NOTIFICATION_DURATION);
            }
            else
            {
                _appNotificationService?.DisplayNotificationAsync("Task failed to be deleted. Please try again or restart the application.", NOTIFICATION_DURATION);
            }

            await LoadColumnsAndTasksAsync();
        }

        public async Task DeleteColumnAsync(PresentationColumn col)
        {
            if (col == null) return;

            // The server deletes the column's tasks and renumbers the remaining columns'
            // positions; LoadColumnsAndTasksAsync then reloads the authoritative state.
            var result = await _manifest.BoardServices.DeleteColumnAsync(col.ToColumnDTO());
            if (result.Success)
                _appNotificationService?.DisplayNotificationAsync("Column deleted successfully", NOTIFICATION_DURATION);

            await LoadColumnsAndTasksAsync();
        }

        public async Task<PresentationColumn> CreateColumnAsync(ColumnDTO colDto)
        {
            var created = await _manifest.BoardServices.CreateColumnAsync(colDto);
            var col = new PresentationColumn(created);
            Columns.Add(col);
            return col;
        }

        [RelayCommand]
        private async Task DeleteCurrentTaskAsync()
        {
            if (CurrentTask != null)
            {
                await DeleteTaskAsync(CurrentTask.Id);
                IsPaneOpen = false;
            }
        }

        [RelayCommand]
        public void CancelEdit()
        {
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

                    if (CurrentTask.Category != OriginalTask.Category)
                        tempTask.Category = CurrentTask.Category;

                    Board.Tasks[index] = tempTask;
                    Board.Tasks = new ObservableCollection<PresentationTask>(
                        Board.Tasks.OrderBy(x => x.ColumnIndex));

                    if (OriginalTask.ReminderTime != DEFAULT_REMINDER_TIME)
                        PrepareAndScheduleToastNotification();
                }
            }

            IsProgressRingActive = false;
        }

        [RelayCommand]
        public void ToggleColumnCollapse() => IsColumnCollapsed = !IsColumnCollapsed;

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
                SuggestedTagsCollection.Add("bug");
                SuggestedTagsCollection.Add("feature");
                SuggestedTagsCollection.Add("urgent");
            }
        }
    }
}
