using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using KanbanTasker.Model.Services;
using KanbanTasker.Services;
using System;
using System.IO;
using System.Threading.Tasks;

// Explicit Windows App SDK runtime initialization (DeploymentManager) for stable
// unpackaged/direct-exe runs, combined with <WindowsAppSDKSelfContained>true in the csproj.
using Microsoft.Windows.ApplicationModel.WindowsAppRuntime;

// Win32 MessageBox for fatal startup errors (visible even when no XAML window exists yet).
using System.Runtime.InteropServices;

namespace Kanban_WinUI;

/// <summary>
/// Single-user Kanban app backed by a local SQL Server Express database.
/// </summary>
public partial class App : Application
{
    private Window? _window;

    public static ServiceProvider? ServiceProvider { get; private set; }

    /// <summary>
    /// The main application window. Dialogs need its XamlRoot in WinUI 3.
    /// </summary>
    public static Window? MainAppWindow { get; private set; }

    /// <summary>
    /// Local per-user app data folder (settings, scheduled reminders).
    /// Plain folder — no MSIX ApplicationData dependency.
    /// </summary>
    public static readonly string AppDataFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Kanban.WinUI");

    public App()
    {
        AppendStartupLog("App() constructor entered");

        // Manual WindowsAppRuntime init (auto-initializer disabled in csproj); never fatal in dev.
        try
        {
            var initResult = DeploymentManager.Initialize(new DeploymentInitializeOptions { OnErrorShowUI = false });
            AppendStartupLog($"DeploymentManager.Initialize: Status={initResult.Status}");
        }
        catch (Exception ex)
        {
            AppendStartupLog($"WindowsAppRuntime init exception (non-fatal): {ex.Message}");
        }

        InitializeComponent();

        try
        {
            Directory.CreateDirectory(AppDataFolder);

            var services = new ServiceCollection();

            // Data layer: SQL Server Express (the app's only backend)
            services.AddKanbanServices();

            // UI services
            services.AddSingleton<INavigationService, Services.NavigationService>();
            services.AddSingleton<IDialogService, Services.DialogService>();
            services.AddSingleton<IAppNotificationService, Services.AppNotificationService>();
            services.AddSingleton<IToastService, Services.ToastService>();
            services.AddSingleton<ITimerService, Services.TimerService>();
            services.AddSingleton<Services.AttachmentService>();

            services.AddTransient<ViewModels.MainViewModel>();

            ServiceProvider = services.BuildServiceProvider();
            AppendStartupLog("DI ServiceProvider built successfully");
        }
        catch (Exception diEx)
        {
            AppendStartupLog("FATAL: Exception during DI setup in App ctor: " + diEx);
            LogAndShowError("Failed to initialize services in the App constructor.", diEx);
        }
    }

    /// <summary>
    /// Invoked when the application is launched.
    /// </summary>
    protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        AppendStartupLog("OnLaunched entered");

        try
        {
            // Load connection settings from appsettings.json and verify them.
            // Missing or failing settings open the database setup window instead
            // of the app, so this deploys to any computer with SQL Server.
            var config = KanbanTasker.Services.DatabaseConfig.Load();
            string? connectError = config == null
                ? "No saved configuration was found."
                : config.TestConnection();

            if (config != null && connectError == null)
            {
                ApplyConfigAndStart(config);
            }
            else
            {
                AppendStartupLog("DB config missing or not working: " + connectError);
                var configWindow = new Views.DatabaseConfigWindow(config, config == null ? null : connectError);
                configWindow.ConfigSaved += (_, savedConfig) =>
                {
                    ApplyConfigAndStart(savedConfig);
                    configWindow.Close();
                };
                configWindow.Activate();
            }
        }
        catch (Exception ex)
        {
            AppendStartupLog("FATAL: Exception during startup: " + ex);
            LogAndShowError("Failed to start Kanban Tasker.", ex);
        }
    }

    /// <summary>Runs the normal startup once a verified database configuration exists.</summary>
    private void ApplyConfigAndStart(KanbanTasker.Services.DatabaseConfig config)
    {
        KanbanTasker.Services.DatabaseConfig.Current = config;
        ServiceCollectionExtensions.ConnectionString = config.BuildConnectionString();
        AppendStartupLog($"DB config applied: {config.Server} / {config.Database}");

        try
        {
            // Ensure the SQL Server schema exists BEFORE MainWindow: its ctor resolves
            // MainViewModel, which immediately queries boards (tblBoards/tblColumns/tblTasks).
            try
            {
                using var scope = ServiceProvider!.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<KanbanTasker.Services.Database.Db>();
                db.Database.EnsureCreated();

                // Lightweight schema upgrade: EnsureCreated never alters existing tables
                Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions.ExecuteSqlRaw(
                    db.Database,
                    "IF COL_LENGTH('tblTasks','Attachments') IS NULL " +
                    "ALTER TABLE tblTasks ADD Attachments nvarchar(max) NOT NULL DEFAULT N''");

                AppendStartupLog("DB EnsureCreated + schema upgrade completed (SQL Server)");
            }
            catch (Exception dbEx)
            {
                AppendStartupLog("DB EnsureCreated failed: " + dbEx.Message);
                LogAndShowError(
                    "Could not reach the SQL Server database.\n\n" +
                    @"Check that SQL Server Express is running (service 'SQL Server (SQLEXPRESS)') " +
                    "and that the KanbanTasker database is accessible.",
                    dbEx);
            }

            _window = new MainWindow();
            MainAppWindow = _window;
            _window.Activate();

            AppendStartupLog("MainWindow created and activated");

            // SQL Server Express has a per-database size limit (50 GB since 2025, 10 GB
            // before): check usage in the background and warn so the user can archive
            // completed tasks in Settings.
            _ = Task.Run(() =>
            {
                try
                {
                    var usage = ServiceProvider!.GetRequiredService<IDatabaseServices>().GetDatabaseUsage();
                    AppendStartupLog($"DB usage: {usage.SizeMB} MB ({usage.Percent:F1}% of the {usage.LimitMB / 1024:F0} GB Express limit)");
                    if (usage.Percent >= 80)
                    {
                        _window?.DispatcherQueue.TryEnqueue(() =>
                            ServiceProvider!.GetRequiredService<IAppNotificationService>().DisplayNotificationAsync(
                                $"Database is at {usage.Percent:F0}% of the SQL Server Express {usage.LimitMB / 1024:F0} GB limit — " +
                                "archive completed tasks in Settings to free space.", 8000));
                    }
                }
                catch (Exception usageEx)
                {
                    AppendStartupLog("DB usage check failed (non-fatal): " + usageEx.Message);
                }
            });

            // First-run welcome dialog (simple file marker, works unpackaged).
            // Must run on the UI thread: ContentDialog.ShowAsync requires it.
            _window.DispatcherQueue.TryEnqueue(async () =>
            {
                try
                {
                    string firstRunMarker = Path.Combine(AppDataFolder, "firstrun.done");
                    if (!File.Exists(firstRunMarker))
                    {
                        File.WriteAllText(firstRunMarker, DateTime.Now.ToString("o"));
                        var dialogService = ServiceProvider!.GetRequiredService<IDialogService>();
                        await dialogService.ShowFirstRunDialog();
                    }
                }
                catch (Exception asyncEx)
                {
                    AppendStartupLog("First-run dialog error (non-fatal): " + asyncEx.Message);
                }
            });
        }
        catch (Exception ex)
        {
            AppendStartupLog("FATAL: Exception while creating/activating MainWindow: " + ex);
            LogAndShowError("Failed to create or activate the main application window.", ex);
        }
    }

    public static T GetService<T>() where T : class
    {
        if (ServiceProvider == null) throw new InvalidOperationException("ServiceProvider not initialized");
        return ServiceProvider.GetRequiredService<T>();
    }

    // === Startup diagnostics helpers ===

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int MessageBoxW(IntPtr hWnd, string lpText, string lpCaption, uint uType);

    private const uint MB_ICONERROR = 0x10;
    private const uint MB_OK = 0x0;

    private static readonly string StartupLogPath = Path.Combine(
        Path.GetTempPath(), "Kanban.WinUI.Startup.log");

    public static void AppendStartupLog(string message)
    {
        try
        {
            var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}{Environment.NewLine}";
            File.AppendAllText(StartupLogPath, line);
        }
        catch { /* ignore log failures */ }
    }

    private static void LogAndShowError(string context, Exception? ex = null)
    {
        string msg = ex == null ? context : $"{context}\n\n{ex.GetType().Name}: {ex.Message}";
        AppendStartupLog("ERROR: " + msg.Replace("\n", " | "));
        try
        {
            MessageBoxW(IntPtr.Zero, msg, "Kanban Startup Error", MB_OK | MB_ICONERROR);
        }
        catch { }
    }
}
