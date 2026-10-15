using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Orbital.DTOs;
using Orbital.Hubs;
using Orbital.Service;

namespace Orbital.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class GroupController : ControllerBase
{
    private readonly GroupService _groupService;
    private readonly IHubContext<ChatHub> _hub;

    public GroupController(GroupService groupService, IHubContext<ChatHub> hub)
    {
        _groupService = groupService;
        _hub = hub;
    }

    private string CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    /// <summary>
    /// Create a new group channel.
    /// </summary>
    [HttpPost("create")]
    [ProducesResponseType(typeof(GroupDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateGroup([FromBody] CreateGroupDto dto)
    {
        var (success, error, group) = await _groupService.CreateGroupAsync(CurrentUserId, dto);
        if (!success || group == null)
            return BadRequest(new { message = error });

        return Ok(group);
    }

    /// <summary>
    /// Add an existing user into a group.
    /// </summary>
    [HttpPost("add-member")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> AddMember([FromBody] AddGroupMemberDto dto)
    {
        var (success, error) = await _groupService.AddMemberAsync(CurrentUserId, dto);
        if (!success)
            return BadRequest(new { message = error });

        return Ok(new { message = "Member added to group successfully." });
    }

    /// <summary>
    /// Get all groups the current user is a member of.
    /// </summary>
    [HttpGet("my-groups")]
    [ProducesResponseType(typeof(List<GroupDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetUserGroups()
    {
        var groups = await _groupService.GetUserGroupsAsync(CurrentUserId);
        return Ok(groups);
    }

    /// <summary>
    /// Fetch group metadata along with its full member roster.
    /// </summary>
    [HttpGet("{groupId:int}")]
    [ProducesResponseType(typeof(GroupDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetGroupDetail(int groupId)
    {
        var detail = await _groupService.GetGroupDetailAsync(groupId, CurrentUserId);
        if (detail == null)
            return NotFound(new { message = "Group not found or access denied." });

        return Ok(detail);
    }

    /// <summary>
    /// Post a message to a group channel.
    /// </summary>
    [HttpPost("messages/send")]
    [ProducesResponseType(typeof(GroupMessageDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> SendGroupMessage([FromBody] SendGroupMessageDto dto)
    {
        var (success, error, message) = await _groupService.SendGroupMessageAsync(CurrentUserId, dto);
        if (!success || message == null)
            return BadRequest(new { message = error });

        return Ok(message);
    }

    /// <summary>
    /// Retrieve paginated messages within a group.
    /// </summary>
    [HttpGet("{groupId:int}/messages")]
    [ProducesResponseType(typeof(List<GroupMessageDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetGroupMessages(
        int groupId,
        [FromQuery] long? beforeId = null,
        [FromQuery] int take = 50)
    {
        var messages = await _groupService.GetGroupMessagesAsync(groupId, CurrentUserId, beforeId, take);
        return Ok(messages);
    }

    /// <summary>
    /// Soft-delete a single group message for everyone. Only the original
    /// sender or the group creator may delete. Broadcasts a
    /// "GroupMessageDeleted" event to the group channel in real time.
    /// </summary>
    [HttpDelete("messages/{messageId:long}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> DeleteGroupMessage(long messageId)
    {
        var (success, error, groupId) = await _groupService
            .DeleteGroupMessageForEveryoneAsync(CurrentUserId, messageId);

        if (!success)
            return BadRequest(new { message = error });

        // Broadcast to everyone currently in this group's SignalR room.
        // Format matches ChatHub.SendGroupMessage: $"group_{groupId}"
        await _hub.Clients
            .Group($"group_{groupId}")
            .SendAsync("GroupMessageDeleted", groupId, messageId);

        return Ok(new { message = "Message deleted." });
    }

    // ─── Leave / Kick / Delete group ────────────────────────────

    /// <summary>
    /// Leave a group (self-removal). If you are the last member, the
    /// group is deleted. If you are the creator and others remain,
    /// ownership transfers to another member.
    /// </summary>
    [HttpPost("{groupId:int}/leave")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> LeaveGroup(int groupId)
    {
        var (success, error) = await _groupService.RemoveOrLeaveMemberAsync(
            CurrentUserId, groupId, CurrentUserId);
        if (!success)
            return BadRequest(new { message = error });

        return Ok(new { message = "Left group." });
    }

    /// <summary>
    /// Remove another member from a group. Creator only.
    /// </summary>
    [HttpDelete("{groupId:int}/members/{targetUserId}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> RemoveMember(int groupId, string targetUserId)
    {
        var (success, error) = await _groupService.RemoveOrLeaveMemberAsync(
            CurrentUserId, groupId, targetUserId);
        if (!success)
            return BadRequest(new { message = error });

        return Ok(new { message = "Member removed." });
    }

    /// <summary>
    /// Delete the entire group. Creator only. Removes all members,
    /// all messages, and the group itself.
    /// </summary>
    [HttpDelete("{groupId:int}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> DeleteGroup(int groupId)
    {
        var (success, error) = await _groupService.DeleteGroupAsync(
            CurrentUserId, groupId);
        if (!success)
            return BadRequest(new { message = error });

        return Ok(new { message = "Group deleted." });
    }
}