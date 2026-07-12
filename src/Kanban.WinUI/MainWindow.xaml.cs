using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Kanban_WinUI.Services;
using Kanban_WinUI.ViewModels;
using KanbanTasker.Model.Dto;
using KanbanTasker.Model.Services;
using Kanban_WinUI.Models;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml.Media;
using System.Linq;
using System.ComponentModel;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace Kanban_WinUI;

/// <summary>
/// The application window with NavigationView + Frame for boards, Mica backdrop.
/// </summary>
public sealed partial class MainWindow : Window
{
    public MainViewModel? ViewModel { get; }

    public MainWindow()
    {
        App.AppendStartupLog("MainWindow ctor started");

        InitializeComponent();
        App.AppendStartupLog("MainWindow: InitializeComponent done");

        // Extend content into the title bar so Mica flows through the whole window (WinUI 3 look)
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        ApplySavedTheme();
        App.AppendStartupLog("MainWindow: Custom titlebar enabled, theme applied");

        // Absolute paths: relative ones break when the app is launched from another directory.
        // The multi-size .ico lets Windows pick the exact resolution per DPI (taskbar, alt-tab);
        // the title-bar Image uses the 512px PNG downscaled by the compositor (crisp at any DPI).
        try { AppWindow.SetIcon(System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico")); }
        catch (Exception ex) { App.AppendStartupLog("SetIcon failed: " + ex.Message); }
        try
        {
            string iconPng = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.png");
            App.AppendStartupLog($"TitleBar icon: exists={System.IO.File.Exists(iconPng)} path={iconPng}");
            TitleBarIcon.Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(iconPng));
        }
        catch (Exception ex) { App.AppendStartupLog("TitleBar icon failed: " + ex.Message); }
        App.AppendStartupLog("MainWindow: Icon set (or skipped)");

        // Resolve services - wrapped to prevent ctor failure in direct exe (no identity) scenarios
        try
        {
            App.AppendStartupLog("MainWindow: About to resolve ViewModel");
            ViewModel = App.GetService<MainViewModel>();
            App.AppendStartupLog("MainWindow: ViewModel resolved, CurrentBoard=" + (ViewModel?.CurrentBoard != null ? "yes" : "no") + ", BoardList.Count=" + (ViewModel?.BoardList?.Count ?? 0));

            var navService = App.GetService<INavigationService>();
            navService.Frame = RootFrame;
            App.AppendStartupLog("MainWindow: NavService frame set");
        }
        catch (Exception svcEx)
        {
            App.AppendStartupLog("MainWindow: Service resolution failed (expected in raw exe): " + svcEx.Message);
            ViewModel = null; // fallback, nav will be minimal
        }

        // DesktopAcrylic (liquid glass) is set in XAML; fall back to Mica where unsupported
        if (!DesktopAcrylicController.IsSupported())
        {
            this.SystemBackdrop = new MicaBackdrop { Kind = MicaKind.BaseAlt };
            App.AppendStartupLog("MainWindow: DesktopAcrylic unsupported - using Mica backdrop");
        }

        // Wire global notification to InfoBar (WinUI3 modern replacement)
        var notifService = App.GetService<IAppNotificationService>() as AppNotificationService;
        if (notifService != null)
        {
            AppNotificationService.ShowNotificationAction = (msg, dur) =>
            {
                GlobalInfoBar.Title = "Kanban";
                GlobalInfoBar.Message = msg;
                GlobalInfoBar.IsOpen = true;
                // Auto close after duration (simplified)
                var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(dur) };
                timer.Tick += (s, e) => { GlobalInfoBar.IsOpen = false; timer.Stop(); };
                timer.Start();
            };
        }

        // Initial navigation handled below for first board

        // Auto load first board into the view
        App.AppendStartupLog("MainWindow: before navigation check, CurrentBoard=" + (ViewModel?.CurrentBoard != null) + ", Count=" + (ViewModel?.BoardList?.Count ?? 0));
        if (ViewModel != null && ViewModel.CurrentBoard != null && ViewModel.BoardList.Count > 0)
        {
            ViewModel.CurrentBoard = ViewModel.BoardList.First(); // ensure
            RootFrame.Navigate(typeof(Views.BoardView), ViewModel.CurrentBoard);
            App.AppendStartupLog("MainWindow: Navigated to BoardView");
        }
        else
        {
            RootFrame.Navigate(typeof(Views.NoBoardsAvailableView));
            App.AppendStartupLog("MainWindow: No boards - showing NoBoardsAvailableView");
        }

        // Populate NavigationView from ViewModel board list
        if (ViewModel != null)
        {
            ViewModel.BoardList.CollectionChanged += (s, e) =>
            {
                if (e.NewItems != null)
                {
                    foreach (BoardViewModel nb in e.NewItems)
                        AttachNameListener(nb);
                }
                RefreshNavItems();
            };
            foreach (var bvm in ViewModel.BoardList)
                AttachNameListener(bvm);
            RefreshNavItems();
            App.AppendStartupLog("MainWindow: Nav items refreshed, count=" + NavView.MenuItems.Count);

            // Show the current board's name in the title bar (like the original UWP title bar)
            ViewModel.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(MainViewModel.CurrentBoard))
                    UpdateTitleBarSubtitle();
            };
            UpdateTitleBarSubtitle();
        }

        // Force a usable window size and ensure it is shown. Helps when launching the raw exe directly (no package identity)
        // or in some dev scenarios where default WinUI window presentation is minimal/zero-sized.
        try
        {
            this.AppWindow.Resize(new Windows.Graphics.SizeInt32(1280, 800));
            this.AppWindow.Show();
        }
        catch { }
    }

    private void RefreshNavItems()
    {
        if (ViewModel == null) return;
        NavView.MenuItems.Clear();
        foreach (var boardVM in ViewModel.BoardList)
        {
            var item = new NavigationViewItem
            {
                Content = boardVM.Board.Name,
                Tag = boardVM,
                Icon = new SymbolIcon(Symbol.Library)
            };
            // Board notes preview on hover (original UWP feature)
            if (!string.IsNullOrWhiteSpace(boardVM.Board.Notes))
                ToolTipService.SetToolTip(item, boardVM.Board.Notes);
            NavView.MenuItems.Add(item);
        }
    }

    private void UpdateTitleBarSubtitle()
    {
        AppTitleBar.Subtitle = ViewModel?.CurrentBoard?.Board?.Name ?? "";
    }

    private void CompactOverlay_Click(object sender, RoutedEventArgs e)
    {
        // Picture-in-picture: small always-on-top window (original UWP compact overlay feature)
        if (AppWindow.Presenter.Kind == Microsoft.UI.Windowing.AppWindowPresenterKind.CompactOverlay)
        {
            AppWindow.SetPresenter(Microsoft.UI.Windowing.AppWindowPresenterKind.Default);
            CompactOverlayIcon.Glyph = "\uE8A7";  // enter-overlay icon
        }
        else
        {
            AppWindow.SetPresenter(Microsoft.UI.Windowing.AppWindowPresenterKind.CompactOverlay);
            CompactOverlayIcon.Glyph = "\uE944";  // exit-overlay icon
        }
    }

    private void AttachNameListener(BoardViewModel bvm)
    {
        if (bvm.Board is INotifyPropertyChanged npc)
            npc.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(PresentationBoard.Name)) RefreshNavItems(); };
    }

    // === Light/dark theme toggle with persistence (works unpackaged: plain file next to the DB) ===

    private static readonly string ThemePrefPath = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Kanban.WinUI", "theme.txt");

    private void ApplySavedTheme()
    {
        try
        {
            if (System.IO.File.Exists(ThemePrefPath) && Content is FrameworkElement fe &&
                Enum.TryParse<ElementTheme>(System.IO.File.ReadAllText(ThemePrefPath).Trim(), out var theme))
            {
                fe.RequestedTheme = theme;
            }
        }
        catch { }
        UpdateThemeToggleIcon();
        Services.ThemeHelpers.ApplyCaptionButtonColors(
            this, (Content as FrameworkElement)?.ActualTheme ?? ElementTheme.Default);
    }

    private void ThemeToggle_Click(object sender, RoutedEventArgs e)
    {
        if (Content is not FrameworkElement fe) return;

        var next = fe.ActualTheme == ElementTheme.Dark ? ElementTheme.Light : ElementTheme.Dark;
        fe.RequestedTheme = next;
        try
        {
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(ThemePrefPath)!);
            System.IO.File.WriteAllText(ThemePrefPath, next.ToString());
        }
        catch { }
        UpdateThemeToggleIcon();
        Services.ThemeHelpers.ApplyCaptionButtonColors(this, next);
    }

    private void UpdateThemeToggleIcon()
    {
        // Sun glyph in dark mode ("switch to light"), moon glyph in light mode ("switch to dark")
        if (Content is FrameworkElement fe)
            ThemeToggleIcon.Glyph = fe.ActualTheme == ElementTheme.Dark ? "" : "";
    }

    private async void NavView_ItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
    {
        if (args.IsSettingsInvoked)
        {
            if (ViewModel != null)
            {
                await ViewModel.OpenSettingsDialog();

                // Settings may have archived tasks away or imported new boards —
                // rebuild everything from the database and show the current board.
                ViewModel.LoadBoards();
                NavigateToCurrentBoard();
            }
            return;
        }

        var item = args.InvokedItemContainer;
        if (item?.Tag is BoardViewModel boardVM)
        {
            // Selection drives navigation to BoardView, passing the VM
            RootFrame.Navigate(typeof(Views.BoardView), boardVM);
            ViewModel!.CurrentBoard = boardVM;
            return;
        }

        if (ViewModel == null)
            return;

        switch (item?.Tag?.ToString())
        {
            case "NewBoard":
                await ViewModel.NewBoard();  // awaits the edit dialog (name/notes input + save/cancel)
                RefreshNavItems();
                // After new, select the last (or restored)
                if (ViewModel.BoardList.Count > 0)
                {
                    var last = ViewModel.BoardList.Last();
                    RootFrame.Navigate(typeof(Views.BoardView), last);
                    ViewModel.CurrentBoard = last;
                }
                break;

            case "EditBoard":
                if (ViewModel.CurrentBoard != null)
                    await ViewModel.EditBoard();  // nav item text auto-updates via name listeners
                break;

            case "DeleteBoard":
                await ConfirmAndDeleteCurrentBoard();
                break;

            case "BoardList":
                await ViewModel.OpenBoardListDialog();
                NavigateToCurrentBoard();  // boards may have been deleted in the dialog
                break;

            case "Calendar":
                if (ViewModel.CurrentBoard != null)
                    await ViewModel.OpenCalendarDialog();
                break;
        }
    }

    private void NavigateToCurrentBoard()
    {
        if (ViewModel?.CurrentBoard != null)
            RootFrame.Navigate(typeof(Views.BoardView), ViewModel.CurrentBoard);
        else
            RootFrame.Navigate(typeof(Views.NoBoardsAvailableView));
    }

    private async System.Threading.Tasks.Task ConfirmAndDeleteCurrentBoard()
    {
        if (ViewModel?.CurrentBoard == null)
            return;

        var dialog = new ContentDialog
        {
            Title = "Delete board",
            Content = $"Delete \"{ViewModel.CurrentBoard.Board.Name}\" and all of its tasks?",
            PrimaryButtonText = "Delete",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = Content.XamlRoot,
            RequestedTheme = Services.ThemeHelpers.CurrentTheme
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            ViewModel.DeleteBoard();  // removes from list (nav refreshes via CollectionChanged) and picks a new CurrentBoard
            NavigateToCurrentBoard();
        }
    }
}
