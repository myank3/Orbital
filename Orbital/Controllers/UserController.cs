using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Orbital.DTOs;
using Orbital.Service;

namespace Orbital.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class UserController : ControllerBase
{
    private readonly UserService _userService;

    public UserController(UserService userService)
    {
        _userService = userService;
    }

    private string CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    /// <summary>
    /// Retrieve own profile identity.
    /// </summary>
    [HttpGet("profile")]
    [ProducesResponseType(typeof(UserProfileDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetCurrentProfile()
    {
        var profile = await _userService.GetProfileByIdAsync(CurrentUserId);
        if (profile == null)
            return NotFound(new { message = "User profile not found." });

        return Ok(profile);
    }

    /// <summary>
    /// Retrieve public profile info for any user by internal GUID.
    /// </summary>
    [HttpGet("profile/{userId}")]
    [ProducesResponseType(typeof(UserProfileDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetProfileById(string userId)
    {
        var profile = await _userService.GetProfileByIdAsync(userId);
        if (profile == null)
            return NotFound(new { message = "User profile not found." });

        return Ok(profile);
    }

    /// <summary>
    /// Search all users (excluding the caller) by display name, handle, or email.
    /// Requires at least 2 characters. Returns up to 30 results by default.
    /// </summary>
    [HttpGet("search")]
    [ProducesResponseType(typeof(List<UserProfileDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> SearchUsers(
        [FromQuery] string q,
        [FromQuery] int take = 30)
    {
        if (string.IsNullOrWhiteSpace(q) || q.Trim().Length < 2)
            return Ok(new List<UserProfileDto>());

        var results = await _userService.SearchUsersAsync(CurrentUserId, q.Trim(), take);
        return Ok(results);
    }

    /// <summary>
    /// Update display name or avatar URL.
    /// </summary>
    [HttpPut("profile")]
    [ProducesResponseType(typeof(UserProfileDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UpdateProfile([FromBody] UpdateProfileDto dto)
    {
        var updated = await _userService.UpdateProfileAsync(CurrentUserId, dto);
        if (updated == null)
            return BadRequest(new { message = "Failed to update profile." });

        return Ok(updated);
    }

    /// <summary>
    /// Upload a new avatar image (max 5 MB). Returns the updated profile
    /// with an absolute URL to the stored image.
    /// </summary>
    [HttpPost("avatar")]
    [RequestSizeLimit(5_000_000)]
    [ProducesResponseType(typeof(UserProfileDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UploadAvatar(IFormFile? file)
    {
        if (file == null || file.Length == 0)
            return BadRequest(new { message = "No file provided." });

        var updated = await _userService.UpdateAvatarAsync(CurrentUserId, file);
        if (updated == null)
            return BadRequest(new { message = "Upload failed. File may be too large (max 5 MB)." });

        // Convert the relative path (/avatars/xxx.png) to an absolute URL
        // so the frontend can render it without knowing the API host.
        var baseUrl = $"{Request.Scheme}://{Request.Host}";
        var absolute = new UserProfileDto(
            updated.Id,
            updated.UserHandle,
            updated.FullHandle,
            updated.DisplayName,
            updated.AvatarUrl != null ? baseUrl + updated.AvatarUrl : null,
            updated.CreatedAt,
            updated.Email
        );

        return Ok(absolute);
    }
}