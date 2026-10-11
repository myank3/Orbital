using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
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
}