using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using KanbanTasker.Model.Services;

namespace Kanban_WinUI.Services.Api
{
    /// <summary>Database maintenance triggered over the REST API (server-side operations).</summary>
    public class ApiDatabaseServices : IDatabaseServices
    {
        private readonly HttpClient _http;
        private readonly JsonSerializerOptions _json;

        public ApiDatabaseServices(HttpClient http, JsonSerializerOptions json)
        {
            _http = http;
            _json = json;
        }

        public async Task<DatabaseUsage> GetDatabaseUsageAsync() =>
            await _http.GetFromJsonAsync<DatabaseUsage>("api/database/usage", _json) ?? new();

        public async Task<int> ArchiveCompletedTasksAsync()
        {
            var resp = await _http.PostAsync("api/database/archive", null);
            var body = await resp.Content.ReadFromJsonAsync<ArchiveResponse>(_json);
            return body?.Archived ?? 0;
        }

        public async Task BackupDBAsync(string destPath) =>
            await _http.PostAsJsonAsync("api/database/backup", new PathRequest(destPath), _json);

        public async Task<string> ImportBackupAsync(string bakPath)
        {
            var resp = await _http.PostAsJsonAsync("api/database/import", new PathRequest(bakPath), _json);
            var body = await resp.Content.ReadFromJsonAsync<MessageResponse>(_json);
            return body?.Message ?? "";
        }

        private record PathRequest(string Path);
        private record ArchiveResponse(int Archived);
        private record MessageResponse(string Message);
    }
}
