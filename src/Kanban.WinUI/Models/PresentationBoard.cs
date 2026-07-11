using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;
using KanbanTasker.Model.Dto;

namespace Kanban_WinUI.Models
{
    public partial class PresentationBoard : ObservableObject
    {
        [ObservableProperty]
        private int id;

        [ObservableProperty]
        private string name;

        [ObservableProperty]
        private string notes;

        [ObservableProperty]
        private ObservableCollection<PresentationTask> _tasks = new();

        [ObservableProperty]
        private ObservableCollection<string> _tagsCollection = new();

        public PresentationBoard(BoardDTO dto)
        {
            Id = dto.Id;
            Name = dto.Name;
            Notes = dto.Notes;
        }

        public BoardDTO ToBoardDTO()
        {
            return new BoardDTO
            {
                Id = Id,
                Name = Name,
                Notes = Notes
            };
        }
    }
}