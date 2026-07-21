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
    /// <summary>Task (card) operations over the REST API.</summary>
    public class ApiTaskServices : ITaskServices
    {
        private readonly HttpClient _http;
        private readonly JsonSerializerOptions _json;

        public ApiTaskServices(HttpClient http, JsonSerializerOptions json)
        {
            _http = http;
            _json = json;
        }

        public async Task<List<TaskDTO>> GetTasksAsync() =>
            await _http.GetFromJsonAsync<List<TaskDTO>>("api/tasks", _json) ?? new();

        public async Task<RowOpResult<TaskDTO>> SaveTaskAsync(TaskDTO task)
        {
            var resp = await _http.PostAsJsonAsync("api/tasks", task, _json);
            return await resp.Content.ReadFromJsonAsync<RowOpResult<TaskDTO>>(_json)
                   ?? new RowOpResult<TaskDTO>(task) { Success = resp.IsSuccessStatusCode };
        }

        public async Task<RowOpResult> DeleteTaskAsync(int id) =>
            await _http.DeleteFromJsonAsync<RowOpResult>($"api/tasks/{id}", _json) ?? new() { Success = true };

        public async Task UpdateCardIndexAsync(int id, int currentCardIndex) =>
            await _http.PutAsJsonAsync($"api/tasks/{id}/card-index", currentCardIndex, _json);

        public async Task UpdateColumnDataAsync(TaskDTO task) =>
            await _http.PutAsJsonAsync("api/tasks/column-data", task, _json);

        public async Task UpdateColumnNameAsync(int id, string newColName) =>
            await _http.PutAsJsonAsync($"api/tasks/{id}/column-name", newColName, _json);

        public RowOpResult<TaskDTO> ValidateTask(RowOpResult<TaskDTO> result)
        {
            var t = result.Entity;
            if (string.IsNullOrEmpty(t.Title)) t.Title = "";
            else if (t.Title.Length > 100) result.ErrorMessage = "Title is too long.";
            t.Description ??= "";
            t.DueDate ??= "";
            t.FinishDate ??= "";
            t.ReminderTime ??= "";
            t.StartDate ??= "";
            result.Success = string.IsNullOrEmpty(result.ErrorMessage);
            return result;
        }
    }
}
