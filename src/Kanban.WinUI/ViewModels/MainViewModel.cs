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
    /// MainViewModel: board list, current board, new/edit/save/delete board via dialog.
    /// Board data is loaded asynchronously over the API through <see cref="InitializeAsync"/>
    /// (the constructor no longer performs network I/O).
    /// </summary>
    public partial class MainViewModel : ObservableObject
    {
        private const int MessageDuration = 3000;
        private readonly IServiceManifest _manifest;
        private readonly IDialogService _dialogService;
        private readonly IAppNotificationService _appNotificationService;
        private readonly IToastService _toastService;
        private readonly ITimerService _timerService;

        // _tmpBoard is the workaround for https://github.com/microsoft/microsoft-ui-xaml/issues/1200
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

            // Data is loaded via InitializeAsync (called from MainWindow) — not here.
        }

        partial void OnCurrentBoardChanged(BoardViewModel? value)
        {
            // Navigation is driven from MainWindow.
        }

        /// <summary>Loads all boards (and their columns/tasks) from the API.</summary>
        public Task InitializeAsync() => LoadBoardsAsync();

        public async Task LoadBoardsAsync()
        {
            BoardList.Clear();
            var boardDTOs = await _manifest.BoardServices.GetBoardsAsync();

            foreach (var dto in boardDTOs)
            {
                var presBoard = new PresentationBoard(dto);

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

                var boardVM = new BoardViewModel(presBoard, _manifest, _appNotificationService, _toastService, _timerService);
                await boardVM.InitializeAsync();   // load columns (tasks already filled from the DTO)
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

                _tmpBoard = CurrentBoard;
                if (_tmpBoard != null)
                {
                    _oldBoardName = _tmpBoard.Board.Name;
                    _oldBoardNotes = _tmpBoard.Board.Notes;
                }

                CurrentBoard = null;
                CurrentBoard = newBoard;

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
        public async Task SaveBoardAsync()
        {
            if (CurrentBoard?.Board == null)
                return;
            if (string.IsNullOrEmpty(CurrentBoard.Board.Name))
                return;
            if (string.IsNullOrEmpty(CurrentBoard.Board.Notes))
                return;

            BoardDTO dto = CurrentBoard.Board.ToBoardDTO();
            bool isNew = dto.Id == 0;
            var result = await _manifest.BoardServices.SaveBoardAsync(dto);

            if (isNew && result.Success)
            {
                CurrentBoard.Board.Id = result.Entity.Id;
                BoardList.Add(CurrentBoard);
                await _manifest.BoardServices.CreateColumnsAsync(CurrentBoard.Board.Id);
                await CurrentBoard.LoadColumnsAndTasksAsync();
            }

            _appNotificationService.DisplayNotificationAsync(
                result.Success ? "Board saved successfully" : result.ErrorMessage,
                MessageDuration);
        }

        [RelayCommand]
        public void CancelSaveBoard()
        {
            if (CurrentBoard?.Board != null)
            {
                CurrentBoard.Board.Name = "";
                CurrentBoard.Board.Notes = "";
            }
            CurrentBoard = null;
            CurrentBoard = _tmpBoard;

            if (CurrentBoard != null)
            {
                CurrentBoard.Board.Name = _oldBoardName;
                CurrentBoard.Board.Notes = _oldBoardNotes;
            }
        }

        [RelayCommand(CanExecute = nameof(CanDeleteBoard))]
        public async Task DeleteBoardAsync()
        {
            if (CurrentBoard == null)
                return;

            await _manifest.BoardServices.DeleteBoardAsync(CurrentBoard.Board.Id);
            BoardList.Remove(CurrentBoard);

            if (CurrentBoard?.Board != null)
            {
                CurrentBoard.Board.Name = "";
                CurrentBoard.Board.Notes = "";
            }
            CurrentBoard = null;
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
                CurrentBoard = null;
            else
            {
                CurrentBoard = null;
                CurrentBoard = _tmpBoard;
            }
        }
    }
}
