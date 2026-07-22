using System.Net;
using KanbanTasker.Model.Services;
using Microsoft.AspNetCore.Mvc;

namespace Kanban.Api.Controllers;

/// <summary>
/// Server-side database maintenance the clients trigger over REST.
///
/// Backup/import move the .bak over HTTP (download / upload) so a client can pull the whole
/// database to its own machine and push one back — but only on the LAN. A full .bak can be
/// large and would blow the tunnel's request limits, so these endpoints refuse any request
/// that arrives on the public host; they answer only to a private/loopback Host.
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

    // ===== Backup / import — LAN only =====

    /// <summary>Back up the whole database and stream the .bak back as a download.</summary>
    [HttpGet("backup/download")]
    public async Task<IActionResult> BackupDownload()
    {
        if (!IsLanRequest()) return LanOnly();

        var stamp = DateTime.Now.ToString("yyyy-MM-dd_HHmmss");
        string temp = Path.Combine(Path.GetTempPath(), $"KanbanTasker_{stamp}.bak");
        await _db.BackupDBAsync(temp);

        // DeleteOnClose removes the temp file once the response finishes streaming.
        var stream = new FileStream(temp, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.DeleteOnClose);
        return File(stream, "application/octet-stream", $"KanbanTasker_{DateTime.Now:yyyy-MM-dd}.bak");
    }

    /// <summary>Upload a .bak and merge it in (upsert by Uuid; overwrite = full restore).</summary>
    [HttpPost("import/upload")]
    [DisableRequestSizeLimit] // a .bak can be large; this is LAN-only anyway
    public async Task<IActionResult> ImportUpload(IFormFile? file, [FromQuery] bool overwrite = false)
    {
        if (!IsLanRequest()) return LanOnly();
        if (file == null || file.Length == 0) return BadRequest(new { message = "No backup file was uploaded." });

        string temp = Path.Combine(Path.GetTempPath(), $"kt_upload_{DateTime.Now:yyyyMMddHHmmss}.bak");
        try
        {
            await using (var fs = System.IO.File.Create(temp))
                await file.CopyToAsync(fs);

            var message = await _db.ImportBackupAsync(temp, overwrite);
            return Ok(new { message });
        }
        finally
        {
            try { System.IO.File.Delete(temp); } catch { }
        }
    }

    // Path-based variants (used when the client and server share a filesystem) — also LAN only.
    public record PathRequest(string Path);

    [HttpPost("backup")]
    public async Task<ActionResult> Backup([FromBody] PathRequest req)
    {
        if (!IsLanRequest()) return LanOnly();
        await _db.BackupDBAsync(req.Path);
        return Ok(new { path = req.Path });
    }

    [HttpPost("import")]
    public async Task<ActionResult> Import([FromBody] PathRequest req, [FromQuery] bool overwrite = false)
    {
        if (!IsLanRequest()) return LanOnly();
        return Ok(new { message = await _db.ImportBackupAsync(req.Path, overwrite) });
    }

    // ===== LAN gate =====

    private ActionResult LanOnly() =>
        StatusCode(403, new { message = "Backup and import are only available on the local network. Connect over the LAN (the desktop client, or the server's LAN address)." });

    /// <summary>True when the request's Host is a private/loopback address — i.e. it came in over
    /// the LAN (desktop client on :5001, or the server's LAN IP), not the public tunnel host.</summary>
    private bool IsLanRequest()
    {
        var host = Request.Host.Host;
        if (string.IsNullOrEmpty(host)) return false;
        if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)) return true;
        if (!IPAddress.TryParse(host, out var ip)) return false;   // a domain name -> public
        if (IPAddress.IsLoopback(ip)) return true;
        var b = ip.GetAddressBytes();
        if (b.Length != 4) return false;
        return b[0] == 10
            || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
            || (b[0] == 192 && b[1] == 168)
            || (b[0] == 169 && b[1] == 254);
    }
}
