using System;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace Kanban_WinUI.Services.Api
{
    /// <summary>How the client reaches the Kanban API.</summary>
    public enum ApiConnectionMode
    {
        /// <summary>Direct LAN connection to the server's IP:port — no public/internet path.</summary>
        LanIp = 0,

        /// <summary>Reach the server through a public address (e.g. a domain via Cloudflare
        /// Tunnel) when off the LAN. Not implemented yet — reserved as a sample.</summary>
        DefaultUrl = 1
    }

    /// <summary>
    /// The thin client's connection setting: how to reach the Kanban API. The client
    /// no longer knows anything about SQL Server; the API owns the database. Stored in
    /// appsettings.json (exe folder first, then %LOCALAPPDATA%\Kanban.WinUI).
    /// </summary>
    public class ApiConfig
    {
        /// <summary>Which connection mode is active. LAN-by-IP is the implemented one.</summary>
        public ApiConnectionMode Mode { get; set; } = ApiConnectionMode.LanIp;

        /// <summary>Server IP address for <see cref="ApiConnectionMode.LanIp"/> (e.g. 192.168.5.2).</summary>
        public string ServerIp { get; set; } = "localhost";

        /// <summary>Server port for <see cref="ApiConnectionMode.LanIp"/> (the API listens on 5000).</summary>
        public int ServerPort { get; set; } = 5000;

        /// <summary>Public URL for <see cref="ApiConnectionMode.DefaultUrl"/>. Sample only, unused.</summary>
        public string DefaultUrl { get; set; } = "";

        /// <summary>The effective base URL the HttpClient talks to. Derived from the mode on save.</summary>
        public string BaseUrl { get; set; } = "http://localhost:5000";

        /// <summary>The configuration the app is running with (set after a successful connect).</summary>
        public static ApiConfig? Current { get; set; }

        private class AppSettings
        {
            public ApiConfig? Api { get; set; }
        }

        private static string ExeSettingsPath => Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        private static string AppDataSettingsPath => Path.Combine(App.AppDataFolder, "appsettings.json");

        /// <summary>
        /// Builds the base URL from the current mode.
        /// LAN: http://ip:port (direct, skips any public/internet path).
        /// Default URL: the stored public URL — NOT IMPLEMENTED, kept as a sample.
        /// </summary>
        public string BuildBaseUrl() => Mode switch
        {
            // TODO: DefaultUrl (public/domain access via Cloudflare Tunnel) is not implemented yet.
            //       When added, validate the URL and reach the API through the tunnel here.
            ApiConnectionMode.DefaultUrl => string.IsNullOrWhiteSpace(DefaultUrl) ? BaseUrl : DefaultUrl.Trim(),
            _ => $"http://{ServerIp.Trim()}:{ServerPort}"
        };

        /// <summary>Best-effort host/port from the stored BaseUrl, for prefilling the LAN fields
        /// when an older config only had a raw URL.</summary>
        public (string Ip, int Port) HostPortFromBaseUrl()
        {
            try
            {
                var uri = new Uri(NormalizedBaseUrl());
                return (uri.Host, uri.Port);
            }
            catch { return ("localhost", 5000); }
        }

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
            // BaseUrl is set from BuildBaseUrl() by the caller (the settings dialog) before
            // saving; here we just persist. We do NOT recompute it, so a config loaded from
            // an older file that only had BaseUrl keeps working.
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

        /// <summary>The stored effective base URL, guaranteed to end with a single '/'
        /// (so relative paths resolve correctly). This is the authoritative runtime value.</summary>
        public string NormalizedBaseUrl() =>
            BaseUrl.TrimEnd('/') + "/";
    }
}
