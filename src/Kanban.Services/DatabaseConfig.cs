using Microsoft.Data.SqlClient;
using System;
using System.IO;
using System.Text.Json;

namespace KanbanTasker.Services
{
    /// <summary>
    /// SQL Server connection settings, persisted to appsettings.json so the app can be
    /// deployed on any computer with SQL Server / SQL Server Express. Read order:
    /// the exe folder (deployment override) first, then %LOCALAPPDATA%\Kanban.WinUI.
    /// </summary>
    public class DatabaseConfig
    {
        public string Server { get; set; } = @".\SQLEXPRESS";
        public string Database { get; set; } = "KanbanTasker";
        public bool IntegratedSecurity { get; set; }
        public string UserId { get; set; } = "";
        public string Password { get; set; } = "";

        /// <summary>The configuration the app is currently running with (set after a successful connect).</summary>
        public static DatabaseConfig? Current { get; set; }

        private class AppSettings
        {
            public DatabaseConfig? Database { get; set; }
        }

        private static string ExeSettingsPath => Path.Combine(AppContext.BaseDirectory, "appsettings.json");

        private static string AppDataSettingsPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Kanban.WinUI", "appsettings.json");

        public string BuildConnectionString()
        {
            var sb = new SqlConnectionStringBuilder
            {
                DataSource = Server,
                InitialCatalog = Database,
                TrustServerCertificate = true,
                Encrypt = false
            };
            if (IntegratedSecurity)
            {
                sb.IntegratedSecurity = true;
            }
            else
            {
                sb.UserID = UserId;
                sb.Password = Password;
            }
            return sb.ConnectionString;
        }

        public static DatabaseConfig? Load()
        {
            foreach (var path in new[] { ExeSettingsPath, AppDataSettingsPath })
            {
                try
                {
                    if (!File.Exists(path)) continue;
                    var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path));
                    if (settings?.Database != null && !string.IsNullOrWhiteSpace(settings.Database.Server))
                        return settings.Database;

                    Log($"appsettings.json at {path} has no usable Database section - ignoring it.");
                }
                catch (Exception ex)
                {
                    // A corrupt/hand-edited file must not look like "no configuration": say so.
                    Log($"appsettings.json at {path} could not be read ({ex.Message}) - ignoring it.");
                }
            }
            return null;
        }

        private static void Log(string message)
        {
            try
            {
                File.AppendAllText(
                    Path.Combine(Path.GetTempPath(), "Kanban.WinUI.Startup.log"),
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}{Environment.NewLine}");
            }
            catch { /* logging must never block startup */ }
        }

        /// <summary>Writes appsettings.json next to the exe; falls back to app data if not writable.</summary>
        public void Save()
        {
            string json = JsonSerializer.Serialize(
                new AppSettings { Database = this },
                new JsonSerializerOptions { WriteIndented = true });
            try
            {
                File.WriteAllText(ExeSettingsPath, json);
            }
            catch
            {
                Directory.CreateDirectory(Path.GetDirectoryName(AppDataSettingsPath)!);
                File.WriteAllText(AppDataSettingsPath, json);
            }
        }

        /// <summary>Tries to connect. Returns null on success, otherwise the error message.</summary>
        public string? TestConnection()
        {
            try
            {
                var sb = new SqlConnectionStringBuilder(BuildConnectionString()) { ConnectTimeout = 5 };
                using var connection = new SqlConnection(sb.ConnectionString);
                connection.Open();
                using var cmd = new SqlCommand("SELECT 1", connection);
                cmd.ExecuteScalar();
                return null;
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        }
    }
}
