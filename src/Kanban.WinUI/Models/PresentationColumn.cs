using CommunityToolkit.Mvvm.ComponentModel;
using KanbanTasker.Model.Dto;
using System.Collections.ObjectModel;

namespace Kanban_WinUI.Models
{
    public partial class PresentationColumn : ObservableObject
    {
        [ObservableProperty] private int _id;
        [ObservableProperty] private int _boardId;
        [ObservableProperty] private string _columnName;
        [ObservableProperty] private int _position;
        [ObservableProperty] private int _maxTaskLimit;

        [ObservableProperty]
        private bool _isCollapsed;

        [ObservableProperty]
        private int _taskCount;  // for WIP display in column header, updated on load/ops

        [ObservableProperty]
        private bool _isOverLimit;  // visual indicator if tasks >= max limit (closer to original WIP handling)

        public bool CanAddNewTask => !IsOverLimit;  // for binding + button IsEnabled to enforce limit visually

        partial void OnIsOverLimitChanged(bool value)
        {
            OnPropertyChanged(nameof(CanAddNewTask));
        }

        public PresentationColumn(ColumnDTO columnDto)
        {
            Id = columnDto.Id;
            BoardId = columnDto.BoardId;
            ColumnName = columnDto.ColumnName;
            Position = columnDto.Position;
            MaxTaskLimit = columnDto.MaxTaskLimit;
        }

        // For native column implementation (no SfKanban dependency)
        public ObservableCollection<PresentationTask> Tasks { get; } = new();

        public ColumnDTO ToColumnDTO()
        {
            return new ColumnDTO
            {
                Id = Id,
                BoardId = BoardId,
                ColumnName = ColumnName,
                Position = Position,
                MaxTaskLimit = MaxTaskLimit
            };
        }
    }
}