using KanbanTasker.Model.Services;
using Microsoft.UI.Xaml.Controls;
using System;

namespace Kanban_WinUI.Services
{
    /// <summary>
    /// Modern WinUI3 in-app notification service.
    /// Uses InfoBar (WinUI3 advantage over old InAppNotification control) for better integration and styling.
    /// In full impl, this would target a global InfoBar in the shell layout.
    /// </summary>
    public class AppNotificationService : IAppNotificationService
    {
        // In real app, inject or find the root InfoBar / notification host.
        // For skeleton, we can log or use a static for demo.
        public static Action<string, int>? ShowNotificationAction { get; set; }

        public void DisplayNotificationAsync(string message, int duration)
        {
            // If action registered (e.g. from MainWindow InfoBar), use it.
            // Otherwise fallback to debug.
            if (ShowNotificationAction != null)
            {
                ShowNotificationAction(message, duration);
            }
            else
            {
                // Fallback for early shell
                System.Diagnostics.Debug.WriteLine($"[AppNotification] {message} (duration: {duration}ms)");
                // In future: integrate with a global InfoBar in MainWindow.xaml
            }
        }
    }
}