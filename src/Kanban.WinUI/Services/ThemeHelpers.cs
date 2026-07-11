using Microsoft.UI;
using Microsoft.UI.Xaml;

namespace Kanban_WinUI.Services
{
    public static class ThemeHelpers
    {
        /// <summary>The app's effective theme, read from the main window's root element.</summary>
        public static ElementTheme CurrentTheme =>
            (App.MainAppWindow?.Content as FrameworkElement)?.ActualTheme ?? ElementTheme.Default;

        /// <summary>
        /// Colors the system caption buttons (minimize/maximize/close) to match the XAML
        /// theme. They are drawn by the OS and do NOT follow RequestedTheme on their own,
        /// so without this they turn invisible (black on black) in forced dark mode.
        /// </summary>
        public static void ApplyCaptionButtonColors(Window window, ElementTheme theme)
        {
            try
            {
                bool dark = theme == ElementTheme.Dark ||
                            (theme == ElementTheme.Default &&
                             Application.Current.RequestedTheme == ApplicationTheme.Dark);

                var titleBar = window.AppWindow.TitleBar;
                var foreground = dark ? Colors.White : Colors.Black;

                titleBar.ButtonBackgroundColor = Colors.Transparent;
                titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
                titleBar.ButtonForegroundColor = foreground;
                titleBar.ButtonInactiveForegroundColor = dark
                    ? Windows.UI.Color.FromArgb(0xFF, 0x9A, 0x9A, 0x9A)
                    : Windows.UI.Color.FromArgb(0xFF, 0x60, 0x60, 0x60);
                titleBar.ButtonHoverForegroundColor = foreground;
                titleBar.ButtonHoverBackgroundColor = dark
                    ? Windows.UI.Color.FromArgb(0x2F, 0xFF, 0xFF, 0xFF)
                    : Windows.UI.Color.FromArgb(0x2F, 0x00, 0x00, 0x00);
                titleBar.ButtonPressedForegroundColor = foreground;
                titleBar.ButtonPressedBackgroundColor = dark
                    ? Windows.UI.Color.FromArgb(0x4F, 0xFF, 0xFF, 0xFF)
                    : Windows.UI.Color.FromArgb(0x4F, 0x00, 0x00, 0x00);
            }
            catch { /* older systems without title bar customization */ }
        }
    }
}
