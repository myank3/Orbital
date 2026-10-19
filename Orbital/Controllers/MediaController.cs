using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Orbital.Data;
using Orbital.DTOs;
using Orbital.Service;

namespace Orbital.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class MediaController : ControllerBase
{
    private readonly R2StorageService _r2;
    private readonly ApplicationDbContext _db;

    public MediaController(R2StorageService r2, ApplicationDbContext db)
    {
        _r2 = r2;
        _db = db;
    }

    private string CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    private static readonly Dictionary<string, string> AllowedContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["image/jpeg"] = ".jpg",
        ["image/png"] = ".png",
        ["image/webp"] = ".webp",
        ["image/gif"] = ".gif",
        ["video/mp4"] = ".mp4",
        ["video/webm"] = ".webm",
        ["video/quicktime"] = ".mov",
        ["audio/mpeg"] = ".mp3",
        ["audio/ogg"] = ".ogg",
        ["audio/wav"] = ".wav",
        ["audio/webm"] = ".weba",
        ["audio/mp4"] = ".m4a",
        ["application/pdf"] = ".pdf",
        ["application/zip"] = ".zip",
    };

    private const long MaxUploadBytes = 50L * 1024 * 1024;

    /// <summary>
    /// Returns a presigned PUT URL for direct browser → R2 upload.
    /// Client then PUTs the file bytes straight to R2 with the same Content-Type.
    /// For group media, the caller must be a member of the group.
    /// </summary>
    [HttpPost("presign-upload")]
    [ProducesResponseType(typeof(PresignUploadResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> PresignUpload([FromBody] PresignUploadRequestDto dto)
    {
        if (!AllowedContentTypes.TryGetValue(dto.ContentType, out var ext))
            return BadRequest(new { message = "Content type not allowed." });

        if (dto.SizeBytes <= 0 || dto.SizeBytes > MaxUploadBytes)
            return BadRequest(new { message = "File size out of range." });

        string prefix;

        switch (dto.Kind)
        {
            case "avatar":
                prefix = $"avatars/{CurrentUserId}";
                break;

            case "group":
                if (dto.GroupId is null || dto.GroupId <= 0)
                    return BadRequest(new { message = "GroupId is required for group media." });

                var isMember = await _db.GroupMembers.AnyAsync(gm =>
                    gm.GroupId == dto.GroupId && gm.UserId == CurrentUserId);
                if (!isMember)
                    return BadRequest(new { message = "You are not a member of this group." });

                prefix = $"group/{dto.GroupId}";
                break;

            default: // "dm"
                prefix = $"dm/{CurrentUserId}";
                break;
        }

        var key = $"{prefix}/{Guid.NewGuid():N}{ext}";
        var presigned = _r2.PresignPut(key, dto.ContentType, TimeSpan.FromMinutes(5));

        return Ok(new PresignUploadResponseDto(presigned.Key, presigned.Url, presigned.ExpiresAt));
    }

    /// <summary>
    /// Returns presigned GET URLs for a batch of media keys.
    /// Phase 2: no ownership check yet (keys are unguessable GUIDs and only
    /// handed out via history responses). Ownership check lands in Phase 3.
    /// </summary>
    [HttpPost("presign-download")]
    [ProducesResponseType(typeof(Dictionary<string, string>), StatusCodes.Status200OK)]
    public IActionResult PresignDownload([FromBody] PresignDownloadRequestDto dto)
    {
        if (dto.Keys == null || dto.Keys.Count == 0)
            return Ok(new Dictionary<string, string>());

        if (dto.Keys.Count > 100)
            return BadRequest(new { message = "Too many keys (max 100 per request)." });

        return Ok(_r2.PresignGetBatch(dto.Keys, TimeSpan.FromHours(4)));
    }
}