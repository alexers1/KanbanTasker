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

    // ===== Chunked import — the browser splits the .bak into parts; we reassemble, verify, import.
    //        Keeps each HTTP request small (no huge single upload), lets a dropped chunk be retried,
    //        and never caps total size. LAN only, like the other import routes. =====

    private static string ChunkRoot => Path.Combine(Path.GetTempPath(), "kt_chunks");

    public record ChunkInit(string FileName, long TotalSize, int TotalChunks, bool Overwrite);

    /// <summary>Open an upload session; returns an id the client tags every chunk with.</summary>
    [HttpPost("import/chunk/init")]
    public IActionResult ImportChunkInit([FromBody] ChunkInit req)
    {
        if (!IsLanRequest()) return LanOnly();
        if (req.TotalChunks <= 0 || req.TotalSize <= 0) return BadRequest(new { message = "Invalid upload descriptor." });

        SweepStaleSessions();

        var uploadId = Guid.NewGuid().ToString("N");
        var dir = Path.Combine(ChunkRoot, uploadId);
        Directory.CreateDirectory(dir);
        System.IO.File.WriteAllText(Path.Combine(dir, "meta.json"), System.Text.Json.JsonSerializer.Serialize(req));
        return Ok(new { uploadId });
    }

    /// <summary>Store one chunk (raw body). Idempotent per index — a retry just overwrites it.</summary>
    [HttpPost("import/chunk")]
    [DisableRequestSizeLimit]
    public async Task<IActionResult> ImportChunk([FromQuery] string uploadId, [FromQuery] int index)
    {
        if (!IsLanRequest()) return LanOnly();
        var dir = SessionDir(uploadId);
        if (dir == null) return NotFound(new { message = "Upload session not found." });
        if (index < 0) return BadRequest(new { message = "Invalid chunk index." });

        var part = Path.Combine(dir, $"part_{index:D6}");
        await using (var fs = System.IO.File.Create(part))
            await Request.Body.CopyToAsync(fs);
        return Ok(new { received = index });
    }

    /// <summary>Which chunk indices are already stored — lets the client resend only the gaps.</summary>
    [HttpGet("import/chunk/status")]
    public IActionResult ImportChunkStatus([FromQuery] string uploadId)
    {
        if (!IsLanRequest()) return LanOnly();
        var dir = SessionDir(uploadId);
        if (dir == null) return NotFound(new { message = "Upload session not found." });
        var have = Directory.GetFiles(dir, "part_*")
            .Select(f => int.Parse(Path.GetFileName(f)["part_".Length..]))
            .OrderBy(i => i).ToArray();
        return Ok(new { received = have });
    }

    /// <summary>All chunks in — reassemble, verify size + SHA-256, then run the normal import.</summary>
    [HttpPost("import/chunk/complete")]
    public async Task<IActionResult> ImportChunkComplete([FromQuery] string uploadId, [FromQuery] string sha256)
    {
        if (!IsLanRequest()) return LanOnly();
        var dir = SessionDir(uploadId);
        if (dir == null) return NotFound(new { message = "Upload session not found." });

        var meta = System.Text.Json.JsonSerializer.Deserialize<ChunkInit>(
            await System.IO.File.ReadAllTextAsync(Path.Combine(dir, "meta.json")));
        if (meta == null) return BadRequest(new { message = "Upload session is corrupted." });

        var parts = Directory.GetFiles(dir, "part_*")
            .Select(f => (idx: int.Parse(Path.GetFileName(f)["part_".Length..]), path: f))
            .OrderBy(p => p.idx).ToList();
        if (parts.Count != meta.TotalChunks)
            return BadRequest(new { message = $"Upload incomplete — have {parts.Count} of {meta.TotalChunks} chunks." });

        var assembled = Path.Combine(dir, "assembled.bak");
        await using (var outFs = System.IO.File.Create(assembled))
            foreach (var p in parts)
            {
                await using var inFs = System.IO.File.OpenRead(p.path);
                await inFs.CopyToAsync(outFs);
            }

        var actualSize = new FileInfo(assembled).Length;
        if (actualSize != meta.TotalSize)
            return BadRequest(new { message = $"Size mismatch (expected {meta.TotalSize}, got {actualSize}) — nothing was restored." });

        string actualHash;
        await using (var hs = System.IO.File.OpenRead(assembled))
            actualHash = Convert.ToHexString(await System.Security.Cryptography.SHA256.HashDataAsync(hs)).ToLowerInvariant();
        if (!string.IsNullOrEmpty(sha256) && !string.Equals(actualHash, sha256, StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { message = "Checksum mismatch — the upload was corrupted; nothing was restored." });

        try
        {
            var message = await _db.ImportBackupAsync(assembled, meta.Overwrite);
            return Ok(new { message });
        }
        finally { TryDeleteDir(dir); }
    }

    /// <summary>Give up on a session and clean its parts.</summary>
    [HttpPost("import/chunk/abort")]
    public IActionResult ImportChunkAbort([FromQuery] string uploadId)
    {
        if (!IsLanRequest()) return LanOnly();
        var dir = SessionDir(uploadId);
        if (dir != null) TryDeleteDir(dir);
        return Ok(new { aborted = true });
    }

    // uploadId is a 32-char hex GUID; reject anything else so it can't escape ChunkRoot.
    private static string? SessionDir(string uploadId)
    {
        if (string.IsNullOrEmpty(uploadId) || uploadId.Length != 32 || !uploadId.All(Uri.IsHexDigit)) return null;
        var dir = Path.Combine(ChunkRoot, uploadId);
        return Directory.Exists(dir) ? dir : null;
    }

    private static void SweepStaleSessions()
    {
        try
        {
            if (!Directory.Exists(ChunkRoot)) return;
            foreach (var d in Directory.GetDirectories(ChunkRoot))
                if (Directory.GetLastWriteTimeUtc(d) < DateTime.UtcNow.AddHours(-24)) TryDeleteDir(d);
        }
        catch { }
    }

    private static void TryDeleteDir(string dir)
    {
        try { Directory.Delete(dir, recursive: true); } catch { }
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
