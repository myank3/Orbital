using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Orbital.Data;
using Orbital.DTOs;
using Orbital.Models;

namespace Orbital.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class KeysController : ControllerBase
{
    private readonly ApplicationDbContext _db;

    public KeysController(ApplicationDbContext db) => _db = db;

    private string CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    /// <summary>
    /// Upload or rotate the caller's public key.
    /// Public key is base64 of a 32-byte Curve25519 key (44 chars).
    /// </summary>
    [HttpPost("upload")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Upload([FromBody] UploadPublicKeyDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.PublicKey))
            return BadRequest(new { message = "Public key is required." });

        var key = dto.PublicKey.Trim();

        // base64 of 32 bytes = 44 chars with padding. Allow some slack.
        if (key.Length < 40 || key.Length > 64)
            return BadRequest(new { message = "Invalid public key length." });

        var existing = await _db.UserPublicKeys.FindAsync(CurrentUserId);

        if (existing == null)
        {
            _db.UserPublicKeys.Add(new UserPublicKey
            {
                UserId = CurrentUserId,
                PublicKey = key,
                CreatedAt = DateTime.UtcNow,
            });
        }
        else if (existing.PublicKey != key)
        {
            existing.PublicKey = key;
            existing.UpdatedAt = DateTime.UtcNow;
        }
        else
        {
            return Ok(new { message = "Key unchanged." });
        }

        await _db.SaveChangesAsync();
        return Ok(new { message = "Key uploaded." });
    }

    /// <summary>Returns the caller's own public key, if uploaded.</summary>
    [HttpGet("me")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMine()
    {
        var k = await _db.UserPublicKeys
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.UserId == CurrentUserId);

        return Ok(new { publicKey = k?.PublicKey });
    }

    /// <summary>
    /// Returns another user's public key. Only allowed if you're accepted
    /// friends with them — prevents key enumeration.
    /// </summary>
    [HttpGet("{userId}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetForUser(string userId)
    {
        if (userId != CurrentUserId)
        {
            var areFriends = await _db.Friendships.AnyAsync(f =>
                ((f.RequesterId == CurrentUserId && f.AddresseeId == userId) ||
                 (f.RequesterId == userId && f.AddresseeId == CurrentUserId)) &&
                f.Status == FriendshipStatus.Accepted);

            if (!areFriends)
                return Forbid();
        }

        var k = await _db.UserPublicKeys
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.UserId == userId);

        if (k == null)
            return NotFound(new { message = "User has not uploaded a public key." });

        return Ok(new { publicKey = k.PublicKey });
    }
}