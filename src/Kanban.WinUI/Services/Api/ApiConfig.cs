using System;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace Kanban_WinUI.Services.Api
{
    /// <summary>
    /// The thin client's only setting: the Kanban API base URL. Stored in appsettings.json
    /// (exe folder first, then %LOCALAPPDATA%\Kanban.WinUI). Replaces the old DatabaseConfig —
    /// the client no longer knows anything about SQL Server; the API owns the database.
    /// </summary>
    public class ApiConfig
    {
        public string BaseUrl { get; set; } = "http://localhost:5000";

        /// <summary>The configuration the app is running with (set after a successful connect).</summary>
        public static ApiConfig? Current { get; set; }

        private class AppSettings
        {
            public ApiConfig? Api { get; set; }
        }

        private static string ExeSettingsPath => Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        private static string AppDataSettingsPath => Path.Combine(App.AppDataFolder, "appsettings.json");

        public static ApiConfig? Load()
        {
            foreach (var path in new[] { ExeSettingsPath, AppDataSettingsPath })
            {
                try
                {
                    if (!File.Exists(path)) continue;
                    var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path));
                    if (settings?.Api != null && !string.IsNullOrWhiteSpace(settings.Api.BaseUrl))
                        return settings.Api;
                }
                catch { /* corrupt/unreadable - try the next location */ }
            }
            return null;
        }

        public void Save()
        {
            string json = JsonSerializer.Serialize(
                new AppSettings { Api = this },
                new JsonSerializerOptions { WriteIndented = true });
            try
            {
                File.WriteAllText(ExeSettingsPath, json);
            }
            catch
            {
                Directory.CreateDirectory(App.AppDataFolder);
                File.WriteAllText(AppDataSettingsPath, json);
            }
        }

        /// <summary>Pings the API root. Returns null on success, otherwise the error message.</summary>
        public async Task<string?> TestConnectionAsync()
        {
            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(6) };
                var resp = await http.GetAsync(NormalizedBaseUrl());
                return resp.IsSuccessStatusCode ? null : $"API returned HTTP {(int)resp.StatusCode}.";
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        }

        /// <summary>Base URL guaranteed to end with a single '/' (so relative paths resolve correctly).</summary>
        public string NormalizedBaseUrl() =>
            BaseUrl.TrimEnd('/') + "/";
    }
}
