using KanbanTasker.Model.Dto;
using KanbanTasker.Model.Services;
using Microsoft.AspNetCore.Mvc;

namespace Kanban.Api.Controllers;

[ApiController]
[Route("api/boards")]
public class BoardsController : ControllerBase
{
    private readonly IBoardServices _boards;

    public BoardsController(IBoardServices boards) => _boards = boards;

    /// <summary>All boards, each with its tasks (columns fetched separately).</summary>
    [HttpGet]
    public async Task<IEnumerable<BoardDTO>> GetBoards() => await _boards.GetBoardsAsync();

    /// <summary>Create (Id==0) or update a board.</summary>
    [HttpPost]
    public async Task<ActionResult> SaveBoard([FromBody] BoardDTO board)
    {
        var result = await _boards.SaveBoardAsync(board);
        return result.Success ? Ok(result) : BadRequest(result);
    }

    [HttpDelete("{id:int}")]
    public async Task<ActionResult> DeleteBoard(int id) => Ok(await _boards.DeleteBoardAsync(id));

    [HttpGet("{id:int}/columns")]
    public async Task<IEnumerable<ColumnDTO>> GetColumns(int id) => await _boards.GetColumnsAsync(id);

    /// <summary>Create the five default columns for a new board.</summary>
    [HttpPost("{id:int}/columns")]
    public async Task<ActionResult> CreateColumns(int id) => Ok(await _boards.CreateColumnsAsync(id));
}
