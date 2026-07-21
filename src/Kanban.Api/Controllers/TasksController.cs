using KanbanTasker.Model.Dto;
using KanbanTasker.Model.Services;
using Microsoft.AspNetCore.Mvc;

namespace Kanban.Api.Controllers;

[ApiController]
[Route("api/tasks")]
public class TasksController : ControllerBase
{
    private readonly ITaskServices _tasks;

    public TasksController(ITaskServices tasks) => _tasks = tasks;

    [HttpGet]
    public async Task<IEnumerable<TaskDTO>> GetTasks() => await _tasks.GetTasksAsync();

    /// <summary>Create (Id==0) or update a task.</summary>
    [HttpPost]
    public async Task<ActionResult> SaveTask([FromBody] TaskDTO task)
    {
        var result = await _tasks.SaveTaskAsync(task);
        return result.Success ? Ok(result) : BadRequest(result);
    }

    [HttpDelete("{id:int}")]
    public async Task<ActionResult> DeleteTask(int id) => Ok(await _tasks.DeleteTaskAsync(id));

    /// <summary>Reorder within a column.</summary>
    [HttpPut("{id:int}/card-index")]
    public async Task<ActionResult> UpdateCardIndex(int id, [FromBody] int index)
    {
        await _tasks.UpdateCardIndexAsync(id, index);
        return Ok();
    }

    /// <summary>Move a task to another column (category + index).</summary>
    [HttpPut("column-data")]
    public async Task<ActionResult> UpdateColumnData([FromBody] TaskDTO task)
    {
        await _tasks.UpdateColumnDataAsync(task);
        return Ok();
    }

    /// <summary>Propagate a column rename to a task's category.</summary>
    [HttpPut("{id:int}/column-name")]
    public async Task<ActionResult> UpdateColumnName(int id, [FromBody] string newColName)
    {
        await _tasks.UpdateColumnNameAsync(id, newColName);
        return Ok();
    }
}
