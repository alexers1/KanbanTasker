using KanbanTasker.Model.Services;
using Microsoft.AspNetCore.Mvc;

namespace Kanban.Api.Controllers;

/// <summary>
/// Server-side database maintenance the thin client triggers over REST.
/// Backup/import currently take a server-local file path (client and API co-located);
/// file transfer will be added when the client is remote.
/// </summary>
[ApiController]
[Route("api/database")]
public class DatabaseController : ControllerBase
{
    private readonly IDatabaseServices _db;

    public DatabaseController(IDatabaseServices db) => _db = db;

    [HttpGet("usage")]
    public async Task<DatabaseUsage> Usage() => await _db.GetDatabaseUsageAsync();

    [HttpPost("archive")]
    public async Task<ActionResult> Archive() => Ok(new { archived = await _db.ArchiveCompletedTasksAsync() });

    public record PathRequest(string Path);

    [HttpPost("backup")]
    public async Task<ActionResult> Backup([FromBody] PathRequest req)
    {
        await _db.BackupDBAsync(req.Path);
        return Ok(new { path = req.Path });
    }

    [HttpPost("import")]
    public async Task<ActionResult> Import([FromBody] PathRequest req)
        => Ok(new { message = await _db.ImportBackupAsync(req.Path) });
}
