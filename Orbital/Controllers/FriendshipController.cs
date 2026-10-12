using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Orbital.DTOs;
using Orbital.Service;

namespace Orbital.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class FriendshipController : ControllerBase
{
    private readonly FriendService _friendService;

    public FriendshipController(FriendService friendService)
    {
        _friendService = friendService;
    }

    private string CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    /// <summary>
    /// Send a friend request using an @orbit handle (e.g. "alice" or "alice@orbit") or email.
    /// </summary>
    [HttpPost("request")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> SendRequest([FromBody] SendFriendRequestDto dto)
    {
        var (success, error) = await _friendService.SendRequestAsync(CurrentUserId, dto);
        if (!success)
            return BadRequest(new { message = error });

        return Ok(new { message = "Friend request sent successfully." });
    }

    /// <summary>
    /// Accept or decline a received friend request.
    /// </summary>
    [HttpPost("respond")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Respond([FromBody] RespondFriendRequestDto dto)
    {
        var (success, error) = await _friendService.RespondToRequestAsync(CurrentUserId, dto);
        if (!success)
            return BadRequest(new { message = error });

        var state = dto.Accept ? "accepted" : "declined";
        return Ok(new { message = $"Friend request {state}." });
    }

    /// <summary>
    /// Fetch all confirmed friends for the logged-in user.
    /// </summary>
    [HttpGet("list")]
    [ProducesResponseType(typeof(List<FriendDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetFriends()
    {
        var friends = await _friendService.GetFriendsListAsync(CurrentUserId);
        return Ok(friends);
    }

    /// <summary>
    /// Fetch pending friend requests waiting for your decision.
    /// </summary>
    [HttpGet("pending")]
    [ProducesResponseType(typeof(List<PendingFriendRequestDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPendingRequests()
    {
        var pending = await _friendService.GetPendingRequestsAsync(CurrentUserId);
        return Ok(pending);
    }

    /// <summary>
    /// Remove an existing friendship. Either side can unfriend.
    /// </summary>
    [HttpDelete("unfriend/{targetUserId}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Unfriend(string targetUserId)
    {
        var (success, error) = await _friendService.UnfriendAsync(CurrentUserId, targetUserId);
        if (!success)
            return BadRequest(new { message = error });

        return Ok(new { message = "Unfriended." });
    }

    /// <summary>
    /// Block a user. Prevents future friend requests and DM delivery.
    /// </summary>
    [HttpPost("block/{targetUserId}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Block(string targetUserId)
    {
        var (success, error) = await _friendService.SetBlockedAsync(CurrentUserId, targetUserId, true);
        if (!success)
            return BadRequest(new { message = error });

        return Ok(new { message = "Blocked." });
    }

    /// <summary>
    /// Returns the full profiles of everyone the caller has blocked.
    /// </summary>
    [HttpGet("blocked")]
    [ProducesResponseType(typeof(List<UserProfileDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetBlockedUsers()
    {
        var blocked = await _friendService.GetBlockedUsersAsync(CurrentUserId);
        return Ok(blocked);
    }

    /// <summary>
    /// Unblock a previously blocked user. Row is deleted so a new request can be sent.
    /// </summary>
    [HttpPost("unblock/{targetUserId}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Unblock(string targetUserId)
    {
        var (success, error) = await _friendService.SetBlockedAsync(CurrentUserId, targetUserId, false);
        if (!success)
            return BadRequest(new { message = error });

        return Ok(new { message = "Unblocked." });
    }
}