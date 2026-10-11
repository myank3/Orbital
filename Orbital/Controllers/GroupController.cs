using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Orbital.DTOs;
using Orbital.Service;

namespace Orbital.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class GroupController : ControllerBase
{
    private readonly GroupService _groupService;

    public GroupController(GroupService groupService)
    {
        _groupService = groupService;
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
}