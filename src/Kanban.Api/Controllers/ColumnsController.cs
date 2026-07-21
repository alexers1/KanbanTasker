using KanbanTasker.Model.Dto;
using KanbanTasker.Model.Services;
using Microsoft.AspNetCore.Mvc;

namespace Kanban.Api.Controllers;

[ApiController]
[Route("api/columns")]
public class ColumnsController : ControllerBase
{
    private readonly IBoardServices _boards;

    public ColumnsController(IBoardServices boards) => _boards = boards;

    /// <summary>Create a single column, returns it with its new Id.</summary>
    [HttpPost]
    public async Task<ColumnDTO> Create([FromBody] ColumnDTO column) => await _boards.CreateColumnAsync(column);

    /// <summary>Create (Id==0) or update a column (name / max task limit).</summary>
    [HttpPut]
    public async Task<ActionResult> Save([FromBody] ColumnDTO column) => Ok(await _boards.SaveColumnAsync(column));

    /// <summary>Update just the column position/index.</summary>
    [HttpPut("index")]
    public async Task<ActionResult> UpdateIndex([FromBody] ColumnDTO column) => Ok(await _boards.UpdateColumnIndexAsync(column));

    /// <summary>Delete a column and its tasks (needs the whole DTO, so POST not DELETE).</summary>
    [HttpPost("delete")]
    public async Task<ActionResult> Delete([FromBody] ColumnDTO column) => Ok(await _boards.DeleteColumnAsync(column));
}
