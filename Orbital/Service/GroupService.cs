using Microsoft.EntityFrameworkCore;
using Orbital.Data;
using Orbital.DTOs;
using Orbital.Models;

namespace Orbital.Service;

public class GroupService
{
    private readonly ApplicationDbContext _db;

    public GroupService(ApplicationDbContext db)
    {
        _db = db;
    }

    /// <summary>
    /// Creates a new group channel and adds the creator as the initial member/owner.
    /// </summary>
    public async Task<(bool Success, string? Error, GroupDto? Group)> CreateGroupAsync(
        string creatorId,
        CreateGroupDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Name))
            return (false, "Group name is required.", null);

        var trimmedName = dto.Name.Trim();
        if (trimmedName.Length < 2)
            return (false, "Group name must be at least 2 characters.", null);

        var group = new Group
        {
            Name = trimmedName,
            Description = dto.Description?.Trim(),
            GroupAvatarUrl = dto.GroupAvatarUrl,
            CreatedById = creatorId,
            CreatedAt = DateTime.UtcNow
        };

        await _db.Groups.AddAsync(group);
        await _db.SaveChangesAsync();

        // Automatically assign creator as the initial group member
        var ownerMembership = new GroupMember
        {
            GroupId = group.Id,
            UserId = creatorId,
            JoinedAt = DateTime.UtcNow
        };

        await _db.GroupMembers.AddAsync(ownerMembership);
        await _db.SaveChangesAsync();

        var groupDto = new GroupDto(
            group.Id,
            group.Name,
            group.Description,
            group.GroupAvatarUrl,
            group.CreatedById,
            group.CreatedAt,
            1
        );

        return (true, null, groupDto);
    }

    /// <summary>
    /// Adds an existing user to a group if the requester is an existing member.
    /// </summary>
    public async Task<(bool Success, string? Error)> AddMemberAsync(
        string requestingUserId,
        AddGroupMemberDto dto)
    {
        var groupExists = await _db.Groups.AnyAsync(g => g.Id == dto.GroupId);
        if (!groupExists)
            return (false, "Group does not exist.");

        var isRequesterInGroup = await _db.GroupMembers
            .AnyAsync(gm => gm.GroupId == dto.GroupId && gm.UserId == requestingUserId);

        if (!isRequesterInGroup)
            return (false, "You are not authorized to add members to this group.");

        var targetUserExists = await _db.Users.AnyAsync(u => u.Id == dto.UserId);
        if (!targetUserExists)
            return (false, "Target user not found.");

        var alreadyMember = await _db.GroupMembers
            .AnyAsync(gm => gm.GroupId == dto.GroupId && gm.UserId == dto.UserId);

        if (alreadyMember)
            return (false, "User is already a member of this group.");

        var membership = new GroupMember
        {
            GroupId = dto.GroupId,
            UserId = dto.UserId,
            JoinedAt = DateTime.UtcNow
        };

        await _db.GroupMembers.AddAsync(membership);
        await _db.SaveChangesAsync();

        return (true, null);
    }

    /// <summary>
    /// Removes a user from a group (or leaves the group).
    /// </summary>
    public async Task<(bool Success, string? Error)> RemoveOrLeaveMemberAsync(
        string requestingUserId,
        int groupId,
        string targetUserId)
    {
        var group = await _db.Groups
            .Include(g => g.Members)
            .FirstOrDefaultAsync(g => g.Id == groupId);

        if (group == null)
            return (false, "Group not found.");

        var membership = group.Members.FirstOrDefault(m => m.UserId == targetUserId);
        if (membership == null)
            return (false, "User is not a member of this group.");

        // Users can remove themselves, or the group creator can remove other members
        var isSelfRemoval = requestingUserId == targetUserId;
        var isCreator = group.CreatedById == requestingUserId;

        if (!isSelfRemoval && !isCreator)
            return (false, "Only the group creator can remove other members.");

        // If the creator leaves and there are other members, transfer ownership
        if (isSelfRemoval && isCreator && group.Members.Count > 1)
        {
            var nextOwner = group.Members.First(m => m.UserId != targetUserId);
            group.CreatedById = nextOwner.UserId;
        }

        _db.GroupMembers.Remove(membership);

        // Delete group if no members remain
        if (group.Members.Count <= 1)
        {
            _db.Groups.Remove(group);
        }

        await _db.SaveChangesAsync();
        return (true, null);
    }

    /// <summary>
    /// Returns all groups the current user is a member of with active member counts.
    /// </summary>
    public async Task<List<GroupDto>> GetUserGroupsAsync(string userId)
    {
        return await _db.GroupMembers
            .AsNoTracking()
            .Where(gm => gm.UserId == userId)
            .Include(gm => gm.Group)
                .ThenInclude(g => g.Members)
            .OrderByDescending(gm => gm.Group.CreatedAt)
            .Select(gm => new GroupDto(
                gm.Group.Id,
                gm.Group.Name,
                gm.Group.Description,
                gm.Group.GroupAvatarUrl,
                gm.Group.CreatedById,
                gm.Group.CreatedAt,
                gm.Group.Members.Count
            ))
            .ToListAsync();
    }

    /// <summary>
    /// Returns full metadata and member roster (UserProfileDto) for a group.
    /// </summary>
    public async Task<GroupDetailDto?> GetGroupDetailAsync(int groupId, string userId)
    {
        var isMember = await _db.GroupMembers
            .AnyAsync(gm => gm.GroupId == groupId && gm.UserId == userId);

        if (!isMember)
            return null;

        var group = await _db.Groups
            .AsNoTracking()
            .Include(g => g.Members)
                .ThenInclude(gm => gm.User)
            .FirstOrDefaultAsync(g => g.Id == groupId);

        if (group == null)
            return null;

        var memberDtos = group.Members.Select(m => new UserProfileDto(
            m.User.Id,
            m.User.UserHandle,
            $"{m.User.UserHandle}@{m.User.Realm}",
            m.User.DisplayName,
            m.User.AvatarUrl,
            m.User.CreatedAt,
            m.User.Email
        )).ToList();

        return new GroupDetailDto(
            group.Id,
            group.Name,
            group.Description,
            group.GroupAvatarUrl,
            group.CreatedById,
            group.CreatedAt,
            memberDtos
        );
    }

    /// <summary>
    /// Persists a new message in a group channel.
    /// </summary>
    public async Task<(bool Success, string? Error, GroupMessageDto? Message)> SendGroupMessageAsync(
        string senderId,
        SendGroupMessageDto dto)
    {
        var isMember = await _db.GroupMembers
            .AnyAsync(gm => gm.GroupId == dto.GroupId && gm.UserId == senderId);

        if (!isMember)
            return (false, "You are not a member of this group.", null);

        if (string.IsNullOrWhiteSpace(dto.Content) && string.IsNullOrWhiteSpace(dto.MediaUrl))
            return (false, "Message cannot be empty.", null);

        var sender = await _db.Users.FindAsync(senderId);
        if (sender == null)
            return (false, "Sender not found.", null);

        var message = new GroupMessage
        {
            GroupId = dto.GroupId,
            SenderId = senderId,
            Content = dto.Content?.Trim() ?? string.Empty,
            MediaUrl = dto.MediaUrl,
            MediaType = dto.MediaType,
            SentAt = DateTime.UtcNow
        };

        await _db.GroupMessages.AddAsync(message);
        await _db.SaveChangesAsync();

        var messageDto = new GroupMessageDto(
            message.Id,
            message.GroupId,
            sender.Id,
            sender.DisplayName,
            sender.AvatarUrl,
            message.Content,
            message.MediaUrl,
            message.MediaType,
            message.SentAt
        );

        return (true, null, messageDto);
    }

    /// <summary>
    /// Returns paginated messages for a group channel.
    /// </summary>
    public async Task<List<GroupMessageDto>> GetGroupMessagesAsync(
        int groupId,
        string userId,
        long? beforeId = null,
        int take = 50)
    {
        var isMember = await _db.GroupMembers
            .AnyAsync(gm => gm.GroupId == groupId && gm.UserId == userId);

        if (!isMember)
            return new List<GroupMessageDto>();

        var query = _db.GroupMessages
            .AsNoTracking()
            .Include(m => m.Sender)
            .Where(m => m.GroupId == groupId);

        if (beforeId.HasValue)
        {
            query = query.Where(m => m.Id < beforeId.Value);
        }

        var messages = await query
            .OrderByDescending(m => m.SentAt)
            .Take(take)
            .ToListAsync();

        return messages
            .OrderBy(m => m.SentAt)
            .Select(m => new GroupMessageDto(
                m.Id,
                m.GroupId,
                m.SenderId,
                m.Sender.DisplayName,
                m.Sender.AvatarUrl,
                m.Content,
                m.MediaUrl,
                m.MediaType,
                m.SentAt
            ))
            .ToList();
    }

    // ─── NEW: Delete group (creator only) ───────────────────────

    /// <summary>
    /// Deletes a group and all its messages and members. Creator only.
    /// This is distinct from "leave" — everyone loses access, history gone.
    /// </summary>
    public async Task<(bool Success, string? Error)> DeleteGroupAsync(
        string requestingUserId,
        int groupId)
    {
        var group = await _db.Groups
            .Include(g => g.Members)
            .FirstOrDefaultAsync(g => g.Id == groupId);

        if (group == null)
            return (false, "Group not found.");

        if (group.CreatedById != requestingUserId)
            return (false, "Only the group creator can delete this group.");

        // Remove all members first
        if (group.Members.Count > 0)
            _db.GroupMembers.RemoveRange(group.Members);

        // Remove all messages in the group
        var messages = await _db.GroupMessages
            .Where(m => m.GroupId == groupId)
            .ToListAsync();
        if (messages.Count > 0)
            _db.GroupMessages.RemoveRange(messages);

        // Remove the group itself
        _db.Groups.Remove(group);

        await _db.SaveChangesAsync();
        return (true, null);
    }
}