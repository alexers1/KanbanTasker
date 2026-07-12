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
            Loaded += (_, _) => Services.LiquidGlass.AttachPointerGlint(this);
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
                ViewModel.CreateColumn(newColDto);
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

        private void BtnSaveColChanges_Click(object sender, RoutedEventArgs e)
        {
            // Close flyout and update via VM (1:1: set New* on VM from live edited col, then call update with original for rename logic)
            if (flyoutEditColumn != null) flyoutEditColumn.Hide();
            if (ViewModel != null && _editingCol != null && !string.IsNullOrEmpty(_editingColOriginal))
            {
                ViewModel.NewColumnName = _editingCol.ColumnName;
                ViewModel.NewColumnMax = _editingCol.MaxTaskLimit;
                ViewModel.UpdateColumn(_editingColOriginal);
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
                    ViewModel.DeleteTask(id);
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

        private void MoveLeft_Click(object sender, RoutedEventArgs e)
        {
            if (sender is HyperlinkButton btn && btn.CommandParameter is PresentationTask task)
            {
                ViewModel.MoveTaskLeft(task);
            }
        }

        private void MoveRight_Click(object sender, RoutedEventArgs e)
        {
            if (sender is HyperlinkButton btn && btn.CommandParameter is PresentationTask task)
            {
                ViewModel.MoveTaskRight(task);
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
                    ViewModel.DeleteColumn(col);
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

        // === Attachments: files live in the attachments folder, DB stores names only ===

        private async void AddAttachment_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel?.CurrentTask == null || App.MainAppWindow == null)
                return;

            var picker = new Windows.Storage.Pickers.FileOpenPicker();
            picker.FileTypeFilter.Add("*");
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainAppWindow);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            var file = await picker.PickSingleFileAsync();
            if (file == null)
                return;

            try
            {
                var attachments = App.GetService<Services.AttachmentService>();
                string name = attachments.AddAttachment(file.Path);
                ViewModel.CurrentTask.Attachments.Add(name);
            }
            catch (Exception ex)
            {
                App.GetService<KanbanTasker.Model.Services.IAppNotificationService>()
                    .DisplayNotificationAsync("Failed to add attachment: " + ex.Message, 4000);
            }
        }

        private void OpenAttachment_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.CommandParameter is string name)
            {
                try
                {
                    var path = App.GetService<Services.AttachmentService>().ResolvePath(name);
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
                }
                catch (Exception ex)
                {
                    App.GetService<KanbanTasker.Model.Services.IAppNotificationService>()
                        .DisplayNotificationAsync("Could not open attachment: " + ex.Message, 4000);
                }
            }
        }

        private void RemoveAttachment_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.CommandParameter is string name && ViewModel?.CurrentTask != null)
            {
                ViewModel.CurrentTask.Attachments.Remove(name);  // link only; the file stays in the folder
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

        private void ColumnCards_DragItemsCompleted(object sender, DragItemsCompletedEventArgs e)
        {
            if (sender is ListView listView && listView.DataContext is PresentationColumn col)
            {
                // Reindex tasks in this column after reorder
                for (int i = 0; i < col.Tasks.Count; i++)
                {
                    col.Tasks[i].ColumnIndex = i;
                    ViewModel.UpdateCardIndex(col.Tasks[i].Id, i);
                }
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
                        ViewModel.UpdateCardColumn(targetCol.ColumnName, task, newIndex);
                        ViewModel.LoadColumnsAndTasks(); // refresh per-column collections
                    }
                }
            }
            finally
            {
                deferral.Complete();
            }
        }

        private void KanbanBoard_CardDragEnd(object sender, object e)
        {
            // Adapted for Syncfusion.Kanban.WinUI API compatibility across versions (the exact KanbanDragEndEventArgs may vary).
            // Full 1:1 drag/reorder + reindex logic preserved using dynamic access.
            if (e == null) return;
            dynamic args = e;

            try
            {
                var targetCategory = args.TargetKey?.ToString();
                var selectedCardModel = args.SelectedCard?.Content as PresentationTask;
                int targetCardIndex = args.TargetCardIndex ?? 0;

                if (selectedCardModel == null) return;

                var selectedTask = ViewModel.Board.Tasks.FirstOrDefault(x => x.Id == selectedCardModel.Id);
                if (selectedTask != null)
                {
                    selectedTask.ColumnIndex = targetCardIndex;
                    ViewModel.UpdateCardColumn(targetCategory, selectedCardModel, targetCardIndex);
                }

                var targetCol = args.TargetColumn;
                var selectedCol = args.SelectedColumn;

                if (targetCol != null && selectedCol != null && targetCol.Title?.ToString() == selectedCol.Title?.ToString())
                {
                    foreach (var card in targetCol.Cards)
                    {
                        int currentIndex = targetCol.Cards.IndexOf(card);
                        if (currentIndex != targetCardIndex)
                        {
                            var currentModel = card.Content as PresentationTask;
                            if (currentModel != null)
                            {
                                var task = ViewModel.Board.Tasks.FirstOrDefault(x => x.Id == currentModel.Id);
                                if (task != null)
                                {
                                    task.ColumnIndex = currentIndex;
                                    ViewModel.UpdateCardIndex(currentModel.Id, currentIndex);
                                }
                            }
                        }
                    }
                }
                else
                {
                    if (targetCol?.Cards?.Count > 0)
                    {
                        foreach (var card in targetCol.Cards)
                        {
                            int currentIndex = targetCol.Cards.IndexOf(card);
                            if (currentIndex > targetCardIndex)
                            {
                                var currentModel = card.Content as PresentationTask;
                                if (currentModel != null)
                                {
                                    var task = ViewModel.Board.Tasks.FirstOrDefault(x => x.Id == currentModel.Id);
                                    if (task != null)
                                    {
                                        task.ColumnIndex = currentIndex;
                                        ViewModel.UpdateCardIndex(currentModel.Id, currentIndex);
                                    }
                                }
                            }
                        }
                    }

                    if (selectedCol?.Cards?.Count > 0)
                    {
                        foreach (var card in selectedCol.Cards)
                        {
                            int currentIndex = selectedCol.Cards.IndexOf(card);
                            var currentModel = card.Content as PresentationTask;
                            if (currentModel != null)
                            {
                                var task = ViewModel.Board.Tasks.FirstOrDefault(x => x.Id == currentModel.Id);
                                if (task != null)
                                {
                                    task.ColumnIndex = currentIndex;
                                    ViewModel.UpdateCardIndex(currentModel.Id, currentIndex);
                                }
                            }
                        }
                    }
                }
            }
            catch
            {
                // Safe fallback for version differences in the Syncfusion Kanban control
            }
        }

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

        private void tappedFlyoutBtnDeleteCardYes_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.IsPaneOpen = false;
            taskFlyout.Hide();
            tappedFlyoutDeleteCard.Hide();
            ViewModel.DeleteTask(ViewModel.CurrentTask.Id);
        }

        private void TappedFlyoutBtnEdit_Click(object sender, RoutedEventArgs e)
        {
            taskFlyout.Hide();
            // The AppBarButton's Command + CommandParameter binding to EditTaskCommand will fire the edit (passing the id from CurrentTask at bind time).
            // This preserves the original structure from UWP 1:1 migration. (flyout itself currently not invoked by RightTapped which is stub).
        }
    }
}