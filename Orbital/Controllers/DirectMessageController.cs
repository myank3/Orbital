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

    /// <summary>
    /// WhatsApp-style "Delete for me": hides the message from the caller's own
    /// view only. The other participant keeps their copy.
    /// </summary>
    [HttpDelete("{messageId:long}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> DeleteMessage(long messageId)
    {
        var (success, error) = await _dmService.DeleteMessageAsync(
            CurrentUserId, messageId);
        if (!success)
            return BadRequest(new { message = error });

        return Ok(new { message = "Deleted for you." });
    }

    /// <summary>
    /// WhatsApp-style "Delete for everyone": removes the message for both
    /// sides. Only the original sender may call this.
    /// </summary>
    [HttpDelete("{messageId:long}/everyone")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> DeleteForEveryone(long messageId)
    {
        var (success, error) = await _dmService.DeleteForEveryoneAsync(
            CurrentUserId, messageId);
        if (!success)
            return BadRequest(new { message = error });

        return Ok(new { message = "Deleted for everyone." });
    }

    /// <summary>
    /// WhatsApp-style "Clear chat": hides every message between the caller
    /// and the target user from the caller's view only.
    /// </summary>
    [HttpDelete("conversation/{targetUserId}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> ClearConversation(string targetUserId)
    {
        var (success, error) = await _dmService.ClearConversationAsync(
            CurrentUserId, targetUserId);
        if (!success)
            return BadRequest(new { message = error });

        return Ok(new { message = "Chat cleared for you." });
    }
}