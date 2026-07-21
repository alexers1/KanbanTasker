using KanbanTasker.Model.Dto;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace KanbanTasker.Model.Services
{
    /// <summary>
    /// Task (card) operations. Async for the same I/O reason as <see cref="IBoardServices"/>.
    /// Validate* stays synchronous (pure in-memory validation).
    /// </summary>
    public interface ITaskServices
    {
        Task<RowOpResult<TaskDTO>> SaveTaskAsync(TaskDTO task);
        Task<RowOpResult> DeleteTaskAsync(int id);
        Task<List<TaskDTO>> GetTasksAsync();
        Task UpdateCardIndexAsync(int id, int currentCardIndex);
        Task UpdateColumnDataAsync(TaskDTO task);
        Task UpdateColumnNameAsync(int id, string newColName);
        RowOpResult<TaskDTO> ValidateTask(RowOpResult<TaskDTO> result);
    }
}
