using System.Net.Http.Json;
using System.Text.Json;
using KanbanTasker.Model;
using KanbanTasker.Model.Dto;
using KanbanTasker.Model.Services;

namespace Kanban.Web.Services;

/// <summary>
/// The web client's single door to the Kanban REST API. It mirrors the endpoint
/// surface the WinUI thin client uses (see Kanban.WinUI/Services/Api), so both front-ends
/// speak to the same backend the same way — only the UI layer differs.
/// </summary>
public class KanbanApiClient
{
    private readonly HttpClient _http;

    // camelCase + tolerant of the API's cycle-broken payloads (Board <-> Task).
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles
    };

    public KanbanApiClient(HttpClient http) => _http = http;

    /// <summary>Ping the API root — used by the connection banner.</summary>
    public async Task<bool> IsReachableAsync()
    {
        try
        {
            var resp = await _http.GetAsync("/");
            return resp.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    // ===== Boards =====

    public async Task<List<BoardDTO>> GetBoardsAsync() =>
        await _http.GetFromJsonAsync<List<BoardDTO>>("api/boards", Json) ?? new();

    public async Task<RowOpResult<BoardDTO>> SaveBoardAsync(BoardDTO board)
    {
        var resp = await _http.PostAsJsonAsync("api/boards", board, Json);
        return await resp.Content.ReadFromJsonAsync<RowOpResult<BoardDTO>>(Json)
               ?? new RowOpResult<BoardDTO>(board) { Success = resp.IsSuccessStatusCode };
    }

    public async Task<RowOpResult> DeleteBoardAsync(int boardId) =>
        await _http.DeleteFromJsonAsync<RowOpResult>($"api/boards/{boardId}", Json) ?? new() { Success = true };

    /// <summary>Seed a new board with the default columns (Backlog / To Do / …).</summary>
    public async Task CreateDefaultColumnsAsync(int boardId) =>
        await _http.PostAsync($"api/boards/{boardId}/columns", null);

    // ===== Columns =====

    public async Task<List<ColumnDTO>> GetColumnsAsync(int boardId) =>
        await _http.GetFromJsonAsync<List<ColumnDTO>>($"api/boards/{boardId}/columns", Json) ?? new();

    public async Task<ColumnDTO> CreateColumnAsync(ColumnDTO column)
    {
        var resp = await _http.PostAsJsonAsync("api/columns", column, Json);
        return await resp.Content.ReadFromJsonAsync<ColumnDTO>(Json) ?? column;
    }

    public async Task<RowOpResult<ColumnDTO>> SaveColumnAsync(ColumnDTO column)
    {
        var resp = await _http.PutAsJsonAsync("api/columns", column, Json);
        return await resp.Content.ReadFromJsonAsync<RowOpResult<ColumnDTO>>(Json)
               ?? new RowOpResult<ColumnDTO>(column) { Success = resp.IsSuccessStatusCode };
    }

    public async Task<RowOpResult> DeleteColumnAsync(ColumnDTO column)
    {
        var resp = await _http.PostAsJsonAsync("api/columns/delete", column, Json);
        return await resp.Content.ReadFromJsonAsync<RowOpResult>(Json) ?? new() { Success = resp.IsSuccessStatusCode };
    }

    /// <summary>Persist a column's display order (Position) after a drag-reorder.</summary>
    public async Task UpdateColumnIndexAsync(ColumnDTO column) =>
        await _http.PutAsJsonAsync("api/columns/index", column, Json);

    // ===== Tasks =====

    public async Task<List<TaskDTO>> GetTasksAsync() =>
        await _http.GetFromJsonAsync<List<TaskDTO>>("api/tasks", Json) ?? new();

    public async Task<RowOpResult<TaskDTO>> SaveTaskAsync(TaskDTO task)
    {
        var resp = await _http.PostAsJsonAsync("api/tasks", task, Json);
        return await resp.Content.ReadFromJsonAsync<RowOpResult<TaskDTO>>(Json)
               ?? new RowOpResult<TaskDTO>(task) { Success = resp.IsSuccessStatusCode };
    }

    public async Task<RowOpResult> DeleteTaskAsync(int id) =>
        await _http.DeleteFromJsonAsync<RowOpResult>($"api/tasks/{id}", Json) ?? new() { Success = true };

    /// <summary>Move a card to another column (updates its stored column name + index).</summary>
    public async Task MoveTaskToColumnAsync(TaskDTO task) =>
        await _http.PutAsJsonAsync("api/tasks/column-data", task, Json);

    // ===== Database =====

    public async Task<DatabaseUsage> GetUsageAsync() =>
        await _http.GetFromJsonAsync<DatabaseUsage>("api/database/usage", Json) ?? new();

    /// <summary>Move completed tasks to the archive database (server-side). Returns the count moved.</summary>
    public async Task<int> ArchiveCompletedAsync()
    {
        var resp = await _http.PostAsync("api/database/archive", null);
        var body = await resp.Content.ReadFromJsonAsync<ArchiveResp>(Json);
        return body?.Archived ?? 0;
    }

    /// <summary>Back up the database to a .bak at a path on the SERVER (API + DB are co-located).</summary>
    public async Task<string> BackupAsync(string serverPath)
    {
        var resp = await _http.PostAsJsonAsync("api/database/backup", new { path = serverPath }, Json);
        if (!resp.IsSuccessStatusCode) return $"Backup failed (HTTP {(int)resp.StatusCode}).";
        var body = await resp.Content.ReadFromJsonAsync<PathResp>(Json);
        return string.IsNullOrEmpty(body?.Path) ? "Backup completed." : $"Backup written to {body!.Path}";
    }

    /// <summary>Import a .bak (server path): upsert by Uuid (idempotent). overwrite=true makes the
    /// backup win on every match (full restore); false keeps the newer row.</summary>
    public async Task<string> ImportAsync(string serverBakPath, bool overwrite = false)
    {
        var url = $"api/database/import?overwrite={(overwrite ? "true" : "false")}";
        var resp = await _http.PostAsJsonAsync(url, new { path = serverBakPath }, Json);
        if (!resp.IsSuccessStatusCode) return $"Import failed (HTTP {(int)resp.StatusCode}).";
        var body = await resp.Content.ReadFromJsonAsync<MsgResp>(Json);
        return string.IsNullOrEmpty(body?.Message) ? "Import completed." : body!.Message;
    }

    /// <summary>Relative URL that streams a fresh full backup as a download (LAN only).</summary>
    public const string BackupDownloadPath = "api/database/backup/download";

    /// <summary>Upload a .bak and import it (multipart). overwrite=true is a full restore.</summary>
    public async Task<string> UploadImportAsync(Stream stream, string fileName, bool overwrite)
    {
        using var content = new System.Net.Http.MultipartFormDataContent();
        var part = new StreamContent(stream);
        part.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
        content.Add(part, "file", fileName);

        var resp = await _http.PostAsync($"api/database/import/upload?overwrite={(overwrite ? "true" : "false")}", content);
        if (resp.StatusCode == System.Net.HttpStatusCode.Forbidden)
            return "Backup/import is only available on the local network.";
        if (!resp.IsSuccessStatusCode)
            return $"Import failed (HTTP {(int)resp.StatusCode}).";
        var body = await resp.Content.ReadFromJsonAsync<MsgResp>(Json);
        return string.IsNullOrEmpty(body?.Message) ? "Import complete." : body!.Message;
    }

    private record ArchiveResp(int Archived);
    private record PathResp(string Path);
    private record MsgResp(string Message);
}
