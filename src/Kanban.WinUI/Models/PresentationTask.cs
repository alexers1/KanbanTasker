using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;
using KanbanTasker.Model.Dto;

namespace Kanban_WinUI.Models
{
    public partial class PresentationTask : ObservableObject
    {
        [ObservableProperty] private int id;
        [ObservableProperty] private int boardId;
        [ObservableProperty] private string title;
        [ObservableProperty] private string description;
        [ObservableProperty] private string category;
        [ObservableProperty] private int columnIndex;
        [ObservableProperty] private string colorKey;
        [ObservableProperty] private ObservableCollection<string> tags = new();

        /// <summary>Attachment file names, relative to the attachments folder (paths only, never file data).</summary>
        [ObservableProperty] private ObservableCollection<string> attachments = new();
        [ObservableProperty] private DateTimeOffset? dateCreated;
        [ObservableProperty] private DateTimeOffset? dueDate;
        [ObservableProperty] private DateTimeOffset? startDate;
        [ObservableProperty] private DateTimeOffset? finishDate;
        [ObservableProperty] private string reminderTime;

        [ObservableProperty] private string timeDue;  // from DTO TimeDue, for original time due in date info

        [ObservableProperty] private bool isActionsVisible;  // for hover show edit/delete like original Pointer behaviors

        [ObservableProperty]
        private string _daysWorkedOn = "";

        [ObservableProperty]
        private string _daysSinceCreation = "";

        public string TagsInput 
        {
            get => string.Join(", ", Tags);
            set 
            {
                Tags.Clear();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    foreach (var tag in value.Split(',').Select(t => t.Trim()).Where(t => !string.IsNullOrEmpty(t)))
                        Tags.Add(tag);
                }
                OnPropertyChanged(nameof(TagsInput));
            }
        }

        public void NotifyTagsInputChanged() => OnPropertyChanged(nameof(TagsInput));

        /// <summary>True when the task has a due date/time in the past (drives the red due chip).</summary>
        public bool IsOverdue
        {
            get
            {
                if (!DueDate.HasValue) return false;
                var due = DueDate.Value.Date;
                if (TimeSpan.TryParse(TimeDue, out var time))
                    return due + time < DateTimeOffset.Now;
                return due < DateTimeOffset.Now.Date;
            }
        }

        /// <summary>Card age, e.g. "3d" (from creation date).</summary>
        public string AgeDisplay => DateCreated.HasValue
            ? $"{Math.Max(0, (int)(DateTimeOffset.Now - DateCreated.Value).TotalDays)}d"
            : "";

        /// <summary>Raise change notifications for the time-dependent displays (called on timer ticks).</summary>
        public void NotifyDateDisplaysChanged()
        {
            OnPropertyChanged(nameof(IsOverdue));
            OnPropertyChanged(nameof(AgeDisplay));
        }

        public PresentationTask(TaskDTO dto)
        {
            Id = dto.Id;
            BoardId = dto.BoardId;
            Title = dto.Title;
            Description = dto.Description;
            Category = dto.Category;
            ColumnIndex = dto.ColumnIndex;
            ColorKey = dto.ColorKey;
            if (!string.IsNullOrEmpty(dto.Tags))
                Tags = new ObservableCollection<string>(dto.Tags.Split(','));
            if (!string.IsNullOrEmpty(dto.Attachments))
                Attachments = new ObservableCollection<string>(
                    dto.Attachments.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
            DueDate = ParseDate(dto.DueDate);
            StartDate = ParseDate(dto.StartDate);
            FinishDate = ParseDate(dto.FinishDate);
            ReminderTime = dto.ReminderTime;
            TimeDue = NormalizeTimeDue(dto.TimeDue);
            DateCreated = ParseDate(dto.DateCreated);
        }

        /// <summary>Tolerant parse: accepts both this app's format and the original UWP app's full DateTimeOffset strings.</summary>
        private static DateTimeOffset? ParseDate(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            return DateTimeOffset.TryParse(value, out var result) ? result : null;
        }

        /// <summary>Normalizes TimeDue to "HH:mm". Old databases stored a full DateTimeOffset string.</summary>
        private static string NormalizeTimeDue(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "";
            if (TimeSpan.TryParse(value, out var ts))
                return $"{ts.Hours:D2}:{ts.Minutes:D2}";
            if (DateTimeOffset.TryParse(value, out var dto))
                return dto.ToString("HH:mm");
            return "";
        }

        public TaskDTO ToTaskDTO()
        {
            return new TaskDTO
            {
                Id = Id,
                BoardId = BoardId,
                Title = Title,
                Description = Description,
                Category = Category,
                ColumnIndex = ColumnIndex,
                ColorKey = ColorKey,
                Tags = Tags == null ? string.Empty : string.Join(",", Tags),
                Attachments = Attachments == null ? string.Empty : string.Join(";", Attachments),
                // DateCreated keeps its time component so "days since creation" stays precise
                DateCreated = DateCreated?.ToString("yyyy-MM-dd HH:mm:ss") ?? "",
                DueDate = DueDate?.ToString("yyyy-MM-dd") ?? "",
                StartDate = StartDate?.ToString("yyyy-MM-dd") ?? "",
                FinishDate = FinishDate?.ToString("yyyy-MM-dd") ?? "",
                TimeDue = TimeDue ?? "",
                ReminderTime = ReminderTime ?? ""
            };
        }
    }
}