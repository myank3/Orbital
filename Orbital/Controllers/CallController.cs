using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Orbital.DTOs;
using Orbital.Models;
using Orbital.Service;

namespace Orbital.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class CallController : ControllerBase
{
    private readonly CallService _callService;

    public CallController(CallService callService)
    {
        _callService = callService;
    }

    private string CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    /// <summary>
    /// Initiate a 1-on-1 audio/video call or group voice channel session.
    /// </summary>
    [HttpPost("initiate")]
    [ProducesResponseType(typeof(CallSessionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> InitiateCall([FromBody] InitiateCallDto dto)
    {
        var (success, error, session) = await _callService.InitiateCallAsync(CurrentUserId, dto);
        if (!success || session == null)
            return BadRequest(new { message = error });

        return Ok(session);
    }

    /// <summary>
    /// End an active call session.
    /// </summary>
    [HttpPost("{sessionId:guid}/end")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> EndCall(Guid sessionId)
    {
        var success = await _callService.EndCallAsync(sessionId);
        if (!success)
            return BadRequest(new { message = "Session not found or call already ended." });

        return Ok(new { message = "Call ended successfully." });
    }

    /// <summary>
    /// Update call status (e.g., Active, Rejected, Missed).
    /// </summary>
    [HttpPatch("{sessionId:guid}/status")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UpdateStatus(Guid sessionId, [FromQuery] CallStatus status)
    {
        var success = await _callService.UpdateCallStatusAsync(sessionId, status);
        if (!success)
            return BadRequest(new { message = "Failed to update call session status." });

        return Ok(new { message = $"Call status updated to {status}." });
    }

    /// <summary>
    /// Fetch public STUN/TURN ICE servers for WebRTC peer connection handshakes.
    /// Returns STUN plus any configured TURN providers (Cloudflare + Metered).
    /// </summary>
    [HttpGet("ice-servers")]
    [ProducesResponseType(typeof(List<IceServerConfigDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetIceServers()
    {
        var servers = await _callService.GetIceServersAsync();
        return Ok(servers);
    }
}