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
    /// MainViewModel: manages board list, current board, new/edit/save/delete board via dialog, navigation.
    /// </summary>
    public partial class MainViewModel : ObservableObject
    {
        private const int MessageDuration = 3000;
        private readonly IServiceManifest _manifest;
        private readonly IDialogService _dialogService;
        private readonly IAppNotificationService _appNotificationService;
        private readonly IToastService _toastService;
        private readonly ITimerService _timerService;

        // _tmpBoard is used to save the current board when a user clicks the Add button, than cancels.
        // Should be able to remove this property when this ticket is fixed: https://github.com/microsoft/microsoft-ui-xaml/issues/1200
        private BoardViewModel _tmpBoard;
        private string _oldBoardName;
        private string _oldBoardNotes;

        [ObservableProperty]
        private ObservableCollection<BoardViewModel> _boardList = new();

        [ObservableProperty]
        private BoardViewModel? _currentBoard;

        [ObservableProperty]
        private string _boardEditorTitle = "";

        public MainViewModel(IServiceManifest manifest, IDialogService dialogService, IAppNotificationService appNotificationService, IToastService toastService, ITimerService timerService)
        {
            _manifest = manifest ?? throw new ArgumentNullException(nameof(manifest));
            _dialogService = dialogService;
            _appNotificationService = appNotificationService;
            _toastService = toastService;
            _timerService = timerService;

            LoadBoards();
        }

        partial void OnCurrentBoardChanged(BoardViewModel? value)
        {
            // Property change side effects for CurrentBoard (navigation driven from MainWindow)
            if (value == null)
            {
                // _navigationService.NavigateToDefaultView(); // if wired
            }
            else
            {
                // _navigationService.NavigateToBoard(value);
            }
        }

        /// <summary>
        /// Initializes boards and tasks from services (tasks sorted by ColumnIndex for restore).
        /// </summary>
        public void LoadBoards()
        {
            BoardList.Clear();
            var boardDTOs = _manifest.BoardServices.GetBoards();

            foreach (var dto in boardDTOs)
            {
                var presBoard = new PresentationBoard(dto);

                // Fill tasks (copied logic)
                if (dto.Tasks?.Any() ?? false)
                {
                    foreach (var taskDTO in dto.Tasks.OrderBy(x => x.ColumnIndex))
                    {
                        presBoard.Tasks.Add(new PresentationTask(taskDTO));
                        foreach (var tag in (taskDTO.Tags ?? "").Split(','))
                        {
                            if (!string.IsNullOrEmpty(tag) && !presBoard.TagsCollection.Contains(tag))
                                presBoard.TagsCollection.Add(tag);
                        }
                    }
                }

                // Create per-board VM (like old boardViewModelFactory) - pass appNotif for 1:1 notifs
                var boardVM = new BoardViewModel(presBoard, _manifest, _appNotificationService, _toastService, _timerService);
                BoardList.Add(boardVM);
            }

            CurrentBoard = BoardList.Any() ? BoardList.First() : null;
        }

        [RelayCommand]
        public async Task NewBoard()
        {
            var dialogOpen = _dialogService.CheckForOpenDialog();
            if (!dialogOpen)
            {
                BoardEditorTitle = "New Board";

                BoardViewModel newBoard = new BoardViewModel(
                    new PresentationBoard(new BoardDTO()),
                    _manifest,
                    _appNotificationService,
                    _toastService,
                    _timerService);

                _tmpBoard = CurrentBoard;            // Workaround for this issue.  Don't remove this line till it's fixed. https://github.com/microsoft/microsoft-ui-xaml/issues/1200
                if (_tmpBoard != null)
                {
                    _oldBoardName = _tmpBoard.Board.Name;
                    _oldBoardNotes = _tmpBoard.Board.Notes;
                }

                CurrentBoard = null;                // Workaround for this issue.  Don't remove this line till it's fixed. https://github.com/microsoft/microsoft-ui-xaml/issues/1200
                CurrentBoard = newBoard;
                // Don't add to BoardList here.  Wait till user saves.

                await _dialogService.ShowEditBoardDialog(this);
            }
        }

        [RelayCommand]
        public async Task EditBoard()
        {
            if (CurrentBoard == null) return;
            BoardEditorTitle = "Edit Board";

            _tmpBoard = CurrentBoard;
            _oldBoardName = _tmpBoard.Board.Name;
            _oldBoardNotes = _tmpBoard.Board.Notes;

            await _dialogService.ShowEditBoardDialog(this);
        }

        [RelayCommand]
        public void SaveBoard()
        {
            if (CurrentBoard?.Board == null)
                return;
            if (string.IsNullOrEmpty(CurrentBoard.Board.Name))
                return;
            if (string.IsNullOrEmpty(CurrentBoard.Board.Notes))
                return;

            BoardDTO dto = CurrentBoard.Board.ToBoardDTO();
            bool isNew = dto.Id == 0;
            var result = _manifest.BoardServices.SaveBoard(dto);

            if (isNew && result.Success)
            {
                CurrentBoard.Board.Id = result.Entity.Id;
                BoardList.Add(CurrentBoard);
                _manifest.BoardServices.CreateColumns(CurrentBoard.Board.Id);

                // Set columns to collection (1:1)
                CurrentBoard.LoadColumnsAndTasks();
            }

            _appNotificationService.DisplayNotificationAsync(
                result.Success ? "Board saved successfully" : result.ErrorMessage,
                MessageDuration);
        }

        [RelayCommand]
        public void CancelSaveBoard()
        {
            // BUG: Currently _tmpBoard still holds edited version? (kept from original for 1:1)
            if (CurrentBoard?.Board != null)
            {
                CurrentBoard.Board.Name = "";
                CurrentBoard.Board.Notes = "";
            }
            CurrentBoard = null;
            CurrentBoard = _tmpBoard;

            // preserve original dialog cancel behavior (UWP ContentDialog + binding state hack)
            if (CurrentBoard != null)
            {
                CurrentBoard.Board.Name = _oldBoardName;
                CurrentBoard.Board.Notes = _oldBoardNotes;
            }
        }

        [RelayCommand(CanExecute = nameof(CanDeleteBoard))]
        public void DeleteBoard()
        {
            if (CurrentBoard == null)
                return;

            _manifest.BoardServices.DeleteBoard(CurrentBoard.Board.Id);
            BoardList.Remove(CurrentBoard);

            if (CurrentBoard?.Board != null)
            {
                CurrentBoard.Board.Name = ""; // preserve original dialog/selection state behavior
                CurrentBoard.Board.Notes = ""; // preserve original dialog/selection state behavior
            }
            CurrentBoard = null; // preserve original dialog/selection state behavior
            CurrentBoard = BoardList.LastOrDefault();
        }

        private bool CanDeleteBoard() => CurrentBoard != null;

        [RelayCommand]
        public async Task OpenSettingsDialog()
        {
            await _dialogService.ShowSettingsDialog();
        }

        [RelayCommand]
        public async Task OpenCalendarDialog()
        {
            if (CurrentBoard != null)
                await _dialogService.ShowCalendarDialog(this);
        }

        [RelayCommand]
        public async Task OpenBoardListDialog()
        {
            await _dialogService.ShowBoardListDialog(this);
        }

        internal void SetCurrentBoardOnClose()
        {
            if (BoardList.Count == 0)
                CurrentBoard = null; // Displays NoBoardsView after
            else
            {
                CurrentBoard = null;
                CurrentBoard = _tmpBoard;
            }
        }
    }
}