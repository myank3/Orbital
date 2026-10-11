using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Orbital.DTOs;
using Orbital.Service;

namespace Orbital.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class DirectMessageController : ControllerBase
{
    private readonly DirectMessageService _dmService;

    public DirectMessageController(DirectMessageService dmService)
    {
        _dmService = dmService;
    }

    private string CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    /// <summary>
    /// Send a direct message to an accepted friend.
    /// </summary>
    [HttpPost("send")]
    [ProducesResponseType(typeof(DirectMessageDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> SendMessage([FromBody] SendDirectMessageDto dto)
    {
        var (success, error, message) = await _dmService.SendMessageAsync(CurrentUserId, dto);
        if (!success || message == null)
            return BadRequest(new { message = error });

        return Ok(message);
    }

    /// <summary>
    /// Retrieve paginated chat history with a specific friend.
    /// </summary>
    [HttpGet("history/{targetUserId}")]
    [ProducesResponseType(typeof(List<DirectMessageDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetChatHistory(
        string targetUserId,
        [FromQuery] long? beforeId = null,
        [FromQuery] int take = 50)
    {
        var history = await _dmService.GetChatHistoryAsync(CurrentUserId, targetUserId, beforeId, take);
        return Ok(history);
    }

    /// <summary>
    /// Mark all incoming messages from a specific sender as read.
    /// </summary>
    [HttpPost("read/{senderId}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> MarkAsRead(string senderId)
    {
        await _dmService.MarkAsReadAsync(CurrentUserId, senderId);
        return Ok(new { message = "Messages marked as read." });
    }
}