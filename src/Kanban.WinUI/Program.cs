using System;
using System.Runtime.InteropServices;

namespace Kanban_WinUI
{
    /// <summary>
    /// Entry point (replaces the auto-generated XAML Main via DISABLE_XAML_GENERATED_MAIN).
    ///
    /// This build is framework-dependent, so the machine must supply the .NET Desktop Runtime
    /// and the Windows App Runtime. The environment scan therefore runs FIRST — before any
    /// WinUI type is touched — and the app simply does not start if something is missing:
    /// the user gets a plain list of what to install instead of a crash.
    /// </summary>
    public static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            // Background prerequisite scan (no UI of its own). On success it has also
            // loaded the Windows App Runtime for this process.
            if (!Services.EnvironmentCheck.TryValidate(out string missing))
            {
                ReportMissing(missing);
                return 1;   // do not start
            }

            WinRT.ComWrappersSupport.InitializeComWrappers();
            Microsoft.UI.Xaml.Application.Start(p =>
            {
                var context = new Microsoft.UI.Dispatching.DispatcherQueueSynchronizationContext(
                    Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread());
                System.Threading.SynchronizationContext.SetSynchronizationContext(context);
                _ = new App();
            });
            return 0;
        }

        private static void ReportMissing(string missing)
        {
            string text =
                "Kanban Tasker can't start — this computer is missing required components:" +
                Environment.NewLine + Environment.NewLine + missing + Environment.NewLine + Environment.NewLine +
                "Install the components above, then start the app again." + Environment.NewLine +
                @"Details were written to %TEMP%\Kanban.WinUI.Startup.log";

            try { MessageBoxW(IntPtr.Zero, text, "Kanban Tasker — missing prerequisites", MB_OK | MB_ICONERROR); }
            catch { /* headless session: the log still has the report */ }
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int MessageBoxW(IntPtr hWnd, string lpText, string lpCaption, uint uType);

        private const uint MB_OK = 0x0;
        private const uint MB_ICONERROR = 0x10;
    }
}
