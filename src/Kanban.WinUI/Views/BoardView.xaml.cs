using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Kanban_WinUI.ViewModels;
using Kanban_WinUI.Models;
using Windows.ApplicationModel.DataTransfer;

namespace Kanban_WinUI.Views
{
    public sealed partial class BoardView : Page
    {
        public BoardViewModel ViewModel => (DataContext as BoardViewModel)!;  // ! to suppress nullable warning from 'as' cast (port artifact)
        private string _editingColOriginal = string.Empty;  // for column edit
        private PresentationColumn _editingCol;  // live ref for 1:1 edit (name may mutate via bind)

        public BoardView()
        {
            this.InitializeComponent();
            taskFlyout.Opening += TaskFlyout_Opening;
        }

        // Visual Layer + Win2D liquid glass behind columns and the edit pane
        private void GlassLayer_Loaded(object sender, RoutedEventArgs e)
        {
            Services.LiquidGlass.Attach((FrameworkElement)sender);
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            if (e.Parameter is BoardViewModel vm)
            {
                DataContext = vm;
            }
        }

        // Event handlers for column/task/kanban interactions
        private async void BtnNewColumn_Click(object sender, RoutedEventArgs e)
        {
            // Use ContentDialog to match original NewColumnDialog experience (name + max limit input, validation)
            if (ViewModel == null) return;

            var nameBox = new TextBox { Header = "Column Name:", Text = "New Column", MaxLength = 24 };
            var limitBox = new NumberBox { Header = "Max Task Limit:", Value = 10, Minimum = 0, Maximum = 100, SmallChange = 1, LargeChange = 5 };

            var panel = new StackPanel { Spacing = 8 };
            panel.Children.Add(nameBox);
            panel.Children.Add(limitBox);

            var dialog = new ContentDialog
            {
                Title = "New Column",
                Content = panel,
                PrimaryButtonText = "Create Column",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = this.XamlRoot,
                RequestedTheme = Services.ThemeHelpers.CurrentTheme
            };

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                string name = nameBox.Text?.Trim() ?? "";
                int limit = (int)Math.Max(0, limitBox.Value);
                if (string.IsNullOrEmpty(name))
                {
                    // simple skip; in full could show notif
                    return;
                }
                if (ViewModel.Columns.Any(c => string.Equals(c.ColumnName, name, StringComparison.OrdinalIgnoreCase)))
                {
                    // avoid duplicate like original checks
                    return;
                }

                var newColDto = new KanbanTasker.Model.Dto.ColumnDTO
                {
                    ColumnName = name,
                    Position = ViewModel.Columns.Count,
                    MaxTaskLimit = limit,
                    BoardId = ViewModel.Board.Id
                };
                await ViewModel.CreateColumnAsync(newColDto);
            }
        }

        private void BtnNewTaskCurrentColumn_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.CommandParameter is PresentationColumn col)
            {
                ViewModel.NewTaskForColumn(col);
            }
        }

        private void BtnEditColumn_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.CommandParameter is PresentationColumn col)
            {
                _editingColOriginal = col.ColumnName;
                _editingCol = col;
                // Show the flyout (defined in resources)
                var flyout = this.FindName("flyoutEditColumn") as Flyout;
                if (flyout != null)
                {
                    flyout.ShowAt(btn);
                    // Set data context for flyout to the column for editing (two-way binding in flyout)
                    var content = flyout.Content as FrameworkElement;
                    if (content != null) content.DataContext = col;
                }
            }
        }

        private async void BtnSaveColChanges_Click(object sender, RoutedEventArgs e)
        {
            // Close flyout and update via VM (set New* on VM from live edited col, then update with original for rename logic)
            if (flyoutEditColumn != null) flyoutEditColumn.Hide();
            if (ViewModel != null && _editingCol != null && !string.IsNullOrEmpty(_editingColOriginal))
            {
                ViewModel.NewColumnName = _editingCol.ColumnName;
                ViewModel.NewColumnMax = _editingCol.MaxTaskLimit;
                await ViewModel.UpdateColumnAsync(_editingColOriginal);
            }
            _editingCol = null;
        }

        // Right-click / touch-hold / Shift+F10 context menu on cards: the framework opens
        // the ContextFlyout; we point its commands at the card that raised it.
        private void TaskFlyout_Opening(object? sender, object e)
        {
            if (taskFlyout.Target is FrameworkElement fe && fe.DataContext is PresentationTask task && ViewModel != null)
            {
                ViewModel.CurrentTask = task;  // the flyout commands target CurrentTask
            }
        }

        private void CardBtnEdit_Click(object sender, RoutedEventArgs e)
        {
            if (sender is HyperlinkButton btn && btn.CommandParameter is int id)
            {
                ViewModel.EditTask(id);
            }
        }

        private async void CardBtnDelete_Click(object sender, RoutedEventArgs e)
        {
            if (sender is HyperlinkButton btn && btn.CommandParameter is int id)
            {
                // Confirmation dialog to match original delete behavior (flyout confirm with "Are you sure you wish to delete this task?")
                var dialog = new ContentDialog
                {
                    Title = "Delete Confirmation",
                    Content = "Are you sure you wish to delete this task?",
                    PrimaryButtonText = "Yes, delete this task",
                    CloseButtonText = "Cancel",
                    DefaultButton = ContentDialogButton.Close,
                    XamlRoot = this.XamlRoot,
                    RequestedTheme = Services.ThemeHelpers.CurrentTheme
                };
                var result = await dialog.ShowAsync();
                if (result == ContentDialogResult.Primary && ViewModel != null)
                {
                    await ViewModel.DeleteTaskAsync(id);
                }
            }
        }

        private void ClosePaneButton_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel != null) ViewModel.IsPaneOpen = false;
        }

        private void PaneCancelEdit_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel != null)
            {
                ViewModel.CancelEditCommand.Execute(null);
            }
        }

        private async void PaneDelete_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel == null || ViewModel.CurrentTask == null) return;
            // Confirmation to match original delete confirm flyouts/behavior
            var dialog = new ContentDialog
            {
                Title = "Delete Task Confirmation",
                Content = "Are you sure you wish to delete this task?",
                PrimaryButtonText = "Yes, delete this",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = this.XamlRoot,
                RequestedTheme = Services.ThemeHelpers.CurrentTheme
            };
            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                ViewModel.DeleteCurrentTaskCommand.Execute(null);
            }
        }

        private void PaneBtnDeleteTaskYes_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel != null) ViewModel.IsPaneOpen = false;
            // Additional delete logic
        }

        private async void MoveLeft_Click(object sender, RoutedEventArgs e)
        {
            if (sender is HyperlinkButton btn && btn.CommandParameter is PresentationTask task)
            {
                await ViewModel.MoveTaskLeftAsync(task);
            }
        }

        private async void MoveRight_Click(object sender, RoutedEventArgs e)
        {
            if (sender is HyperlinkButton btn && btn.CommandParameter is PresentationTask task)
            {
                await ViewModel.MoveTaskRightAsync(task);
            }
        }

        private async void BtnDeleteColumn_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.CommandParameter is PresentationColumn col)
            {
                var dialog = new ContentDialog
                {
                    Title = "Delete Column",
                    Content = "Delete this column and its tasks? (Original had confirm)",
                    PrimaryButtonText = "Yes, delete",
                    CloseButtonText = "Cancel",
                    DefaultButton = ContentDialogButton.Close,
                    XamlRoot = this.XamlRoot,
                    RequestedTheme = Services.ThemeHelpers.CurrentTheme
                };
                var result = await dialog.ShowAsync();
                if (result == ContentDialogResult.Primary && ViewModel != null)
                {
                    await ViewModel.DeleteColumnAsync(col);
                }
            }
        }

        private void ToggleCollapse_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.CommandParameter is PresentationColumn col)
            {
                col.IsCollapsed = !col.IsCollapsed;
                // For visual, bind the border Visibility to !IsCollapsed in full
            }
        }

        // Tag AutoSuggest handlers (for exact original behavior)
        private void autoSuggestBoxTags_GotFocus(object sender, RoutedEventArgs e)
        {
            if (sender is AutoSuggestBox asb)
            {
                asb.IsSuggestionListOpen = true;
            }
        }

        private void autoSuggestBoxTags_SuggestionChosen(AutoSuggestBox sender, AutoSuggestBoxSuggestionChosenEventArgs args)
        {
            // When navigating suggestions with keyboard; we auto-add in QuerySubmitted
        }

        private void autoSuggestBoxTags_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
        {
            if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput && ViewModel != null)
            {
                var query = (sender.Text ?? string.Empty).ToLowerInvariant();
                // Filter suggestions dynamically (original pattern: set ItemsSource to filtered view)
                sender.ItemsSource = ViewModel.SuggestedTagsCollection
                    .Where(t => t.ToLowerInvariant().Contains(query))
                    .ToList();
            }
        }

        private void autoSuggestBoxTags_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
        {
            if (ViewModel == null) return;

            string? toAdd = null;
            if (args.ChosenSuggestion != null)
            {
                toAdd = args.ChosenSuggestion.ToString();
            }
            else if (!string.IsNullOrWhiteSpace(args.QueryText))
            {
                toAdd = args.QueryText;
            }

            if (!string.IsNullOrWhiteSpace(toAdd))
            {
                if (ViewModel.AddTag(toAdd))
                {
                    sender.Text = string.Empty;
                }
                // Refresh suggestions list (in case global)
                sender.ItemsSource = ViewModel.SuggestedTagsCollection;
            }
        }

        // === Attachments: path references only (a file or a whole folder), opened in Explorer.
        //     Nothing is uploaded/copied — the DB stores a "MACHINE\path" pointer. ===

        private async void AddAttachment_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel?.CurrentTask == null || App.MainAppWindow == null)
                return;

            var picker = new Windows.Storage.Pickers.FileOpenPicker();
            picker.FileTypeFilter.Add("*");
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainAppWindow);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            var file = await picker.PickSingleFileAsync();
            if (file != null)
                AddReference(file.Path);
        }

        private async void AddAttachmentFolder_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel?.CurrentTask == null || App.MainAppWindow == null)
                return;

            var picker = new Windows.Storage.Pickers.FolderPicker();
            picker.FileTypeFilter.Add("*");
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainAppWindow);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            var folder = await picker.PickSingleFolderAsync();
            if (folder != null)
                AddReference(folder.Path);
        }

        private void AddReference(string fullPath)
        {
            var reference = App.GetService<Services.AttachmentService>().MakeReference(fullPath);
            if (!ViewModel!.CurrentTask.Attachments.Contains(reference))
                ViewModel.CurrentTask.Attachments.Add(reference);
        }

        private void OpenAttachment_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.CommandParameter is string reference)
            {
                string? error = App.GetService<Services.AttachmentService>().OpenInExplorer(reference);
                if (error != null)
                    App.GetService<KanbanTasker.Model.Services.IAppNotificationService>()
                        .DisplayNotificationAsync("Could not open attachment: " + error, 4000);
            }
        }

        private void RemoveAttachment_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.CommandParameter is string reference && ViewModel?.CurrentTask != null)
            {
                ViewModel.CurrentTask.Attachments.Remove(reference);  // reference only; the file/folder is untouched
            }
        }

        // Hover for card action buttons (edit/delete) to match original Interactivity PointerEntered/Exited + visibility toggle
        private void Card_PointerEntered(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is PresentationTask task)
            {
                task.IsActionsVisible = true;
            }
        }

        private void Card_PointerExited(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is PresentationTask task)
            {
                task.IsActionsVisible = false;
            }
        }

        // === Native WinUI drag handlers for custom Kanban (column-to-column + within reorder) ===
        private void ColumnCards_DragItemsStarting(object sender, DragItemsStartingEventArgs e)
        {
            if (e.Items.FirstOrDefault() is PresentationTask task)
            {
                e.Data.SetText(task.Id.ToString());
                e.Data.RequestedOperation = DataPackageOperation.Move;
            }
        }

        private async void ColumnCards_DragItemsCompleted(object sender, DragItemsCompletedEventArgs e)
        {
            if (sender is ListView listView && listView.DataContext is PresentationColumn col)
            {
                // Reindex tasks in this column after reorder (batch: save each, refresh once)
                for (int i = 0; i < col.Tasks.Count; i++)
                {
                    col.Tasks[i].ColumnIndex = i;
                    await ViewModel.SaveCardIndexAsync(col.Tasks[i].Id, i);
                }
                await ViewModel.LoadColumnsAndTasksAsync();
            }
        }

        private void Column_DragOver(object sender, DragEventArgs e)
        {
            e.AcceptedOperation = DataPackageOperation.Move;
        }

        private async void Column_Drop(object sender, DragEventArgs e)
        {
            var deferral = e.GetDeferral();
            try
            {
                string idStr = await e.DataView.GetTextAsync();
                if (int.TryParse(idStr, out int taskId) && sender is FrameworkElement fe && fe.DataContext is PresentationColumn targetCol)
                {
                    var task = ViewModel.Board.Tasks.FirstOrDefault(t => t.Id == taskId);
                    if (task != null && task.Category != targetCol.ColumnName)
                    {
                        int newIndex = targetCol.Tasks.Count;
                        await ViewModel.UpdateCardColumnAsync(targetCol.ColumnName, task, newIndex); // moves + refreshes
                    }
                }
            }
            finally
            {
                deferral.Complete();
            }
        }

        // (Removed) KanbanBoard_CardDragEnd — Syncfusion-era dead code (no XAML binding, pre-async APIs).

        private void btnSaveColChanges_Click(object sender, RoutedEventArgs e)
        {
            // delegate to main handler for consistency after 1:1 column edit port
            BtnSaveColChanges_Click(sender, e);
        }

        private void txtBoxColName_TextChanged(object sender, TextChangedEventArgs e)
        {
            // Binding handles in our port
        }

        private void txtBoxColName_Loaded(object sender, RoutedEventArgs e)
        {
            (sender as TextBox).Select((sender as TextBox).Text.Length, 0);
        }

        private void maxLimitNumberBox_ValueChanged(Microsoft.UI.Xaml.Controls.NumberBox sender, Microsoft.UI.Xaml.Controls.NumberBoxValueChangedEventArgs args)
        {
            // Binding handles
        }

        private void FlyoutDeleteCardBtnYes_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.IsPaneOpen = false;
        }

        private async void tappedFlyoutBtnDeleteCardYes_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.IsPaneOpen = false;
            taskFlyout.Hide();
            tappedFlyoutDeleteCard.Hide();
            await ViewModel.DeleteTaskAsync(ViewModel.CurrentTask.Id);
        }

        private void TappedFlyoutBtnEdit_Click(object sender, RoutedEventArgs e)
        {
            taskFlyout.Hide();
            // The AppBarButton's Command + CommandParameter binding to EditTaskCommand will fire the edit (passing the id from CurrentTask at bind time).
            // This preserves the original structure from UWP 1:1 migration. (flyout itself currently not invoked by RightTapped which is stub).
        }
    }
}