using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using KanbanTasker.Model;
using KanbanTasker.Model.Services;
using Kanban_WinUI.Services.Api;
using System;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;

// Win32 MessageBox for fatal startup errors (visible even when no XAML window exists yet).
using System.Runtime.InteropServices;

namespace Kanban_WinUI;

/// <summary>
/// Thin Kanban client. All data goes through the REST API (see Services/Api); the client
/// holds no database connection — it only knows the API base URL (ApiConfig).
/// </summary>
public partial class App : Application
{
    private Window? _window;

    public static ServiceProvider? ServiceProvider { get; private set; }

    /// <summary>The main application window. Dialogs need its XamlRoot in WinUI 3.</summary>
    public static Window? MainAppWindow { get; private set; }

    /// <summary>Local per-user app data folder (settings, reminders, first-run marker).</summary>
    public static readonly string AppDataFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Kanban.WinUI");

    /// <summary>Shared HttpClient for the API services (base address set from ApiConfig).</summary>
    private static HttpClient? _http;

    public App()
    {
        // The Windows App Runtime was verified and bootstrapped by Program.Main's pre-check.
        AppendStartupLog("App() constructor entered");

        InitializeComponent();

        try
        {
            Directory.CreateDirectory(AppDataFolder);

            ApiConfig.Current = ApiConfig.Load() ?? new ApiConfig();
            AppendStartupLog("API base URL: " + ApiConfig.Current.BaseUrl);

            var json = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                ReferenceHandler = ReferenceHandler.IgnoreCycles
            };
            _http = new HttpClient { BaseAddress = new Uri(ApiConfig.Current.NormalizedBaseUrl()) };

            var services = new ServiceCollection();

            // Data layer: REST API over HttpClient (no SQL/EF in the client)
            services.AddSingleton(_http);
            services.AddSingleton(json);
            services.AddSingleton<IBoardServices>(_ => new ApiBoardServices(_http, json));
            services.AddSingleton<ITaskServices>(_ => new ApiTaskServices(_http, json));
            services.AddSingleton<IDatabaseServices>(_ => new ApiDatabaseServices(_http, json));
            services.AddSingleton<IServiceManifest>(sp => new ApiServiceManifest(
                sp.GetRequiredService<ITaskServices>(),
                sp.GetRequiredService<IBoardServices>(),
                sp.GetRequiredService<IDatabaseServices>()));

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

    protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        AppendStartupLog("OnLaunched entered");

        try
        {
            _window = new MainWindow();
            MainAppWindow = _window;
            _window.Activate();
            AppendStartupLog("MainWindow created and activated");

            // Load boards from the API off the constructor (network I/O), then populate the shell.
            _window.DispatcherQueue.TryEnqueue(async () =>
            {
                try
                {
                    await ((MainWindow)_window).LoadBoardsAndShowAsync();
                }
                catch (Exception ex)
                {
                    AppendStartupLog("Board load failed: " + ex.Message);
                    try
                    {
                        ServiceProvider!.GetRequiredService<IAppNotificationService>()
                            .DisplayNotificationAsync(
                                $"Could not reach the API at {ApiConfig.Current?.BaseUrl} — {ex.Message}. " +
                                "Make sure the Kanban API is running.", 10000);
                    }
                    catch { }
                }
            });
        }
        catch (Exception ex)
        {
            AppendStartupLog("FATAL: Exception during startup: " + ex);
            LogAndShowError("Failed to start Kanban Tasker.", ex);
        }
    }

    public static T GetService<T>() where T : class
    {
        if (ServiceProvider == null) throw new InvalidOperationException("ServiceProvider not initialized");
        return ServiceProvider.GetRequiredService<T>();
    }

    /// <summary>
    /// Point the shared HttpClient at the current ApiConfig address. Only safe before the first
    /// request (used by the startup connection dialog when the user corrects the IP/port).
    /// </summary>
    public static void ApplyApiBaseAddress()
    {
        try
        {
            if (_http != null && ApiConfig.Current != null)
                _http.BaseAddress = new Uri(ApiConfig.Current.NormalizedBaseUrl());
        }
        catch (Exception ex) { AppendStartupLog("ApplyApiBaseAddress failed: " + ex.Message); }
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
