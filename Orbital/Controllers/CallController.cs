using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
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
    private readonly PushNotificationService _pushService; // 🔥 Added
    private readonly UserManager<ApplicationUser> _userManager; // 🔥 Added

    public CallController(
        CallService callService,
        PushNotificationService pushService,
        UserManager<ApplicationUser> userManager)
    {
        _callService = callService;
        _pushService = pushService;
        _userManager = userManager;
    }

    private string CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    [HttpPost("initiate")]
    public async Task<IActionResult> InitiateCall([FromBody] InitiateCallDto dto)
    {
        var (success, error, session) = await _callService.InitiateCallAsync(CurrentUserId, dto);
        if (!success || session == null)
            return BadRequest(new { message = error });

        // 🔥 TRIGGER PUSH NOTIFICATION FOR 1-ON-1 CALLS
        if (!string.IsNullOrEmpty(dto.PeerId))
        {
            var recipient = await _userManager.FindByIdAsync(dto.PeerId);
            var caller = await _userManager.FindByIdAsync(CurrentUserId);

            if (recipient != null && !string.IsNullOrEmpty(recipient.FcmToken))
            {
                var callerName = caller?.DisplayName ?? caller?.UserHandle ?? "Someone";

                // Fire and forget
                _ = Task.Run(() => _pushService.SendCallPushAsync(
                    recipient.FcmToken,
                    callerName,
                    session.SessionId.ToString()
                ));
            }
        }

        return Ok(session);
    }

    [HttpPost("{sessionId:guid}/end")]
    public async Task<IActionResult> EndCall(Guid sessionId)
    {
        var success = await _callService.EndCallAsync(sessionId);
        if (!success) return BadRequest(new { message = "Session not found or call already ended." });
        return Ok(new { message = "Call ended successfully." });
    }

    [HttpPatch("{sessionId:guid}/status")]
    public async Task<IActionResult> UpdateStatus(Guid sessionId, [FromQuery] CallStatus status)
    {
        var success = await _callService.UpdateCallStatusAsync(sessionId, status);
        if (!success) return BadRequest(new { message = "Failed to update call session status." });
        return Ok(new { message = $"Call status updated to {status}." });
    }

    [HttpGet("ice-servers")]
    public async Task<IActionResult> GetIceServers()
    {
        var servers = await _callService.GetIceServersAsync();
        return Ok(servers);
    }
}