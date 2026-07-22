using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace Kanban_WinUI.Services.Api
{
    /// <summary>
    /// The thin client's only setting: the LAN IP:port of the Kanban API. There is deliberately
    /// no public / web-login mode — the desktop client always connects directly to the server on
    /// the local network, which keeps it simple and sidesteps TLS/certificate hassles. Stored in
    /// appsettings.json (exe folder first, then %LOCALAPPDATA%\Kanban.WinUI); defaults to the
    /// deployed server so a fresh install connects with no configuration.
    /// </summary>
    public class ApiConfig
    {
        public string ServerIp { get; set; } = "192.168.5.2";
        public int ServerPort { get; set; } = 5001;

        /// <summary>Effective base URL; persisted so an older config that only had BaseUrl still works.</summary>
        public string BaseUrl { get; set; } = "http://192.168.5.2:5001";

        /// <summary>The configuration the app is running with.</summary>
        public static ApiConfig? Current { get; set; }

        private class AppSettings { public ApiConfig? Api { get; set; } }

        private static string ExeSettingsPath => Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        private static string AppDataSettingsPath => Path.Combine(App.AppDataFolder, "appsettings.json");

        /// <summary>The direct LAN address: http://ip:port.</summary>
        public string BuildBaseUrl() => $"http://{ServerIp.Trim()}:{ServerPort}";

        /// <summary>True when the server address is on the local network (private IPv4 or loopback).
        /// Backup/import are only offered on the LAN.</summary>
        public bool IsLan => IsPrivateOrLoopback(ServerIp);

        public static bool IsPrivateOrLoopback(string? host)
        {
            host = (host ?? "").Trim();
            if (host.Length == 0) return false;
            if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase)) return true;
            if (!IPAddress.TryParse(host, out var ip)) return false;
            if (IPAddress.IsLoopback(ip)) return true;
            var b = ip.GetAddressBytes();
            if (b.Length != 4) return false;
            return b[0] == 10
                || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
                || (b[0] == 192 && b[1] == 168)
                || (b[0] == 169 && b[1] == 254); // link-local
        }

        /// <summary>Best-effort host/port from the stored BaseUrl, for prefilling the fields.</summary>
        public (string Ip, int Port) HostPortFromBaseUrl()
        {
            try { var uri = new Uri(NormalizedBaseUrl()); return (uri.Host, uri.Port); }
            catch { return ("192.168.5.2", 5001); }
        }

        public static ApiConfig? Load()
        {
            foreach (var path in new[] { ExeSettingsPath, AppDataSettingsPath })
            {
                try
                {
                    if (!File.Exists(path)) continue;
                    // Old files may carry extra fields (a removed Mode/DefaultUrl) — ignored on read.
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
            BaseUrl = BuildBaseUrl();
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
                return resp.IsSuccessStatusCode ? null : $"API at {BuildBaseUrl()} returned HTTP {(int)resp.StatusCode}.";
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        }

        /// <summary>The stored effective base URL, guaranteed to end with a single '/'.</summary>
        public string NormalizedBaseUrl() => BaseUrl.TrimEnd('/') + "/";
    }
}
