using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using KanbanTasker.Model;
using KanbanTasker.Model.Dto;
using KanbanTasker.Model.Services;

namespace Kanban_WinUI.Services.Api
{
    /// <summary>Board/column operations over the REST API (replaces the direct EF service).</summary>
    public class ApiBoardServices : IBoardServices
    {
        private readonly HttpClient _http;
        private readonly JsonSerializerOptions _json;

        public ApiBoardServices(HttpClient http, JsonSerializerOptions json)
        {
            _http = http;
            _json = json;
        }

        public async Task<List<BoardDTO>> GetBoardsAsync() =>
            await _http.GetFromJsonAsync<List<BoardDTO>>("api/boards", _json) ?? new();

        public async Task<RowOpResult<BoardDTO>> SaveBoardAsync(BoardDTO board)
        {
            var resp = await _http.PostAsJsonAsync("api/boards", board, _json);
            return await resp.Content.ReadFromJsonAsync<RowOpResult<BoardDTO>>(_json)
                   ?? new RowOpResult<BoardDTO>(board) { Success = resp.IsSuccessStatusCode };
        }

        public async Task<RowOpResult> DeleteBoardAsync(int boardId) =>
            await _http.DeleteFromJsonAsync<RowOpResult>($"api/boards/{boardId}", _json) ?? new() { Success = true };

        public async Task<RowOpResult> CreateColumnsAsync(int boardId)
        {
            var resp = await _http.PostAsync($"api/boards/{boardId}/columns", null);
            return await resp.Content.ReadFromJsonAsync<RowOpResult>(_json) ?? new() { Success = resp.IsSuccessStatusCode };
        }

        public async Task<List<ColumnDTO>> GetColumnsAsync(int boardId) =>
            await _http.GetFromJsonAsync<List<ColumnDTO>>($"api/boards/{boardId}/columns", _json) ?? new();

        public async Task<RowOpResult<ColumnDTO>> SaveColumnAsync(ColumnDTO column)
        {
            var resp = await _http.PutAsJsonAsync("api/columns", column, _json);
            return await resp.Content.ReadFromJsonAsync<RowOpResult<ColumnDTO>>(_json)
                   ?? new RowOpResult<ColumnDTO>(column) { Success = resp.IsSuccessStatusCode };
        }

        public async Task<ColumnDTO> CreateColumnAsync(ColumnDTO column)
        {
            var resp = await _http.PostAsJsonAsync("api/columns", column, _json);
            return await resp.Content.ReadFromJsonAsync<ColumnDTO>(_json) ?? column;
        }

        public async Task<RowOpResult> DeleteColumnAsync(ColumnDTO column)
        {
            var resp = await _http.PostAsJsonAsync("api/columns/delete", column, _json);
            return await resp.Content.ReadFromJsonAsync<RowOpResult>(_json) ?? new() { Success = resp.IsSuccessStatusCode };
        }

        public async Task<RowOpResult> UpdateColumnIndexAsync(ColumnDTO column)
        {
            var resp = await _http.PutAsJsonAsync("api/columns/index", column, _json);
            return await resp.Content.ReadFromJsonAsync<RowOpResult>(_json) ?? new() { Success = resp.IsSuccessStatusCode };
        }

        // Pure in-memory validation stays client-side (the server validates again on save).
        public RowOpResult<BoardDTO> ValidateBoard(RowOpResult<BoardDTO> result)
        {
            var b = result.Entity;
            if (string.IsNullOrEmpty(b.Name)) result.ErrorMessage = "Name is required.";
            else if (b.Name.Length > 100) result.ErrorMessage = "Name is too long.";
            if (string.IsNullOrEmpty(b.Notes)) result.ErrorMessage = "Notes are required.";
            else if (b.Notes.Length > 1000) result.ErrorMessage = "Notes are too long.";
            result.Success = string.IsNullOrEmpty(result.ErrorMessage);
            return result;
        }
    }
}
