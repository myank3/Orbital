using Microsoft.EntityFrameworkCore;
using Orbital.Data;
using Orbital.DTOs;
using Orbital.Models;
using Microsoft.AspNetCore.Identity;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System;
using Regex = System.Text.RegularExpressions.Regex; // 🔥 FIX: Aliased to prevent "Group" namespace collision

namespace Orbital.Service;

public class GroupService
{
    private readonly ApplicationDbContext _db;
    private readonly R2StorageService _r2;
    private readonly PushNotificationService _pushService;
    private readonly UserManager<ApplicationUser> _userManager;

    public GroupService(
        ApplicationDbContext db,
        R2StorageService r2,
        PushNotificationService pushService,
        UserManager<ApplicationUser> userManager)
    {
        _db = db;
        _r2 = r2;
        _pushService = pushService;
        _userManager = userManager;
    }

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

        var isSelfRemoval = requestingUserId == targetUserId;
        var isCreator = group.CreatedById == requestingUserId;

        if (!isSelfRemoval && !isCreator)
            return (false, "Only the group creator can remove other members.");

        if (isSelfRemoval && isCreator && group.Members.Count > 1)
        {
            var nextOwner = group.Members.First(m => m.UserId != targetUserId);
            group.CreatedById = nextOwner.UserId;
        }

        _db.GroupMembers.Remove(membership);

        if (group.Members.Count <= 1)
        {
            _db.Groups.Remove(group);
        }

        await _db.SaveChangesAsync();
        return (true, null);
    }

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

    public async Task<(bool Success, string? Error, GroupMessageDto? Message)> SendGroupMessageAsync(
        string senderId,
        SendGroupMessageDto dto)
    {
        var isMember = await _db.GroupMembers
            .AnyAsync(gm => gm.GroupId == dto.GroupId && gm.UserId == senderId);

        if (!isMember)
            return (false, "You are not a member of this group.", null);

        if (string.IsNullOrWhiteSpace(dto.Content)
            && string.IsNullOrWhiteSpace(dto.MediaUrl)
            && string.IsNullOrWhiteSpace(dto.MediaKey))
            return (false, "Message cannot be empty.", null);

        if (!string.IsNullOrEmpty(dto.MediaKey))
        {
            var expectedPrefix = $"group/{dto.GroupId}/";
            if (!dto.MediaKey.StartsWith(expectedPrefix, StringComparison.Ordinal))
                return (false, "Invalid media key for this group.", null);
        }

        var sender = await _db.Users.FindAsync(senderId);
        if (sender == null)
            return (false, "Sender not found.", null);

        if (!string.IsNullOrEmpty(dto.Content) &&
            dto.Content.StartsWith("[call:", StringComparison.Ordinal))
        {
            return (true, null, new GroupMessageDto(
                0L,
                dto.GroupId,
                sender.Id,
                sender.DisplayName,
                sender.AvatarUrl,
                dto.Content,
                dto.MediaUrl,
                dto.MediaKey,
                dto.MediaType,
                DateTime.UtcNow
            ));
        }

        var message = new GroupMessage
        {
            GroupId = dto.GroupId,
            SenderId = senderId,
            Content = dto.Content?.Trim() ?? string.Empty,
            MediaUrl = dto.MediaUrl,
            MediaKey = dto.MediaKey,
            MediaType = dto.MediaType, // 🔥 FIX: Removed ?? 0 because MediaType is int, not int?
            SentAt = DateTime.UtcNow
        };

        await _db.GroupMessages.AddAsync(message);
        await _db.SaveChangesAsync();

        // 🔥 TRIGGER PUSH NOTIFICATIONS FOR GROUP MEMBERS HERE
        var group = await _db.Groups.FindAsync(dto.GroupId);
        if (group != null)
        {
            var memberIds = await _db.GroupMembers
                .Where(gm => gm.GroupId == dto.GroupId && gm.UserId != senderId)
                .Select(gm => gm.UserId)
                .ToListAsync();

            var tokens = await _db.Users
                .Where(u => memberIds.Contains(u.Id) && !string.IsNullOrEmpty(u.FcmToken))
                .Select(u => u.FcmToken)
                .ToListAsync();

            if (tokens.Count > 0)
            {
                // 🔥 FIX: Strip the [client:tmp-...] prefix from the message content
                var rawContent = message.Content ?? "";
                var cleanText = Regex.Replace(rawContent, @"\[client:[^\]]+\]", "").Trim();

                if (!string.IsNullOrWhiteSpace(cleanText) && cleanText.Length > 80)
                    cleanText = cleanText.Substring(0, 77) + "...";

                var notificationBody = string.IsNullOrWhiteSpace(cleanText)
                    ? $"{sender.DisplayName}: 📷 Photo"
                    : $"{sender.DisplayName}: {cleanText}";

                _ = Task.Run(async () =>
                {
                    var tasks = tokens.Select(token =>
                        _pushService.SendMessagePushAsync(
                            token!,
                            group.Name,
                            notificationBody,
                            dto.GroupId.ToString(),
                            "group_message"
                        )
                    );
                    await Task.WhenAll(tasks);
                });
            }
        }

        var messageDto = new GroupMessageDto(
            message.Id,
            message.GroupId,
            sender.Id,
            sender.DisplayName,
            sender.AvatarUrl,
            message.Content,
            message.MediaUrl,
            message.MediaKey,
            message.MediaType,
            message.SentAt
        );

        return (true, null, messageDto);
    }

    public async Task<PresignedMessagesResponse<GroupMessageDto>> GetGroupMessagesAsync(
        int groupId,
        string userId,
        long? beforeId = null,
        int take = 50)
    {
        var isMember = await _db.GroupMembers
            .AnyAsync(gm => gm.GroupId == groupId && gm.UserId == userId);

        if (!isMember)
            return new PresignedMessagesResponse<GroupMessageDto>(
                new List<GroupMessageDto>(),
                new Dictionary<string, string>());

        var query = _db.GroupMessages
            .AsNoTracking()
            .Include(m => m.Sender)
            .Where(m => m.GroupId == groupId && m.DeletedAt == null);

        if (beforeId.HasValue)
        {
            query = query.Where(m => m.Id < beforeId.Value);
        }

        var rows = await query
            .OrderByDescending(m => m.SentAt)
            .Take(take)
            .ToListAsync();

        var messages = rows
            .OrderBy(m => m.SentAt)
            .Select(m => new GroupMessageDto(
                m.Id,
                m.GroupId,
                m.SenderId,
                m.Sender.DisplayName,
                m.Sender.AvatarUrl,
                m.Content,
                m.MediaUrl,
                m.MediaKey,
                m.MediaType,
                m.SentAt
            ))
            .ToList();

        var keys = rows
            .Where(m => !string.IsNullOrEmpty(m.MediaKey))
            .Select(m => m.MediaKey!)
            .ToList();

        var presigned = keys.Count > 0
            ? _r2.PresignGetBatch(keys, TimeSpan.FromHours(4))
            : new Dictionary<string, string>();

        return new PresignedMessagesResponse<GroupMessageDto>(messages, presigned);
    }

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

        if (group.Members.Count > 0)
            _db.GroupMembers.RemoveRange(group.Members);

        var messages = await _db.GroupMessages
            .Where(m => m.GroupId == groupId)
            .ToListAsync();
        if (messages.Count > 0)
            _db.GroupMessages.RemoveRange(messages);

        _db.Groups.Remove(group);

        var mediaKeys = messages
            .Where(m => !string.IsNullOrEmpty(m.MediaKey))
            .Select(m => m.MediaKey!)
            .ToList();

        await _db.SaveChangesAsync();

        foreach (var key in mediaKeys)
        {
            try
            {
                await _r2.DeleteAsync(key);
            }
            catch { }
        }

        return (true, null);
    }

    public async Task<(bool Success, string? Error, int GroupId)> DeleteGroupMessageForEveryoneAsync(
        string requestingUserId,
        long messageId)
    {
        var message = await _db.GroupMessages
            .Include(m => m.Group)
            .FirstOrDefaultAsync(m => m.Id == messageId);

        if (message == null)
            return (false, "Message not found.", 0);

        if (message.DeletedAt != null)
            return (false, "Message already deleted.", message.GroupId);

        var isSender = message.SenderId == requestingUserId;
        var isCreator = message.Group.CreatedById == requestingUserId;

        if (!isSender && !isCreator)
            return (false, "You can only delete your own messages.", message.GroupId);

        var mediaKey = message.MediaKey;

        message.DeletedAt = DateTime.UtcNow;
        message.DeletedBy = requestingUserId;
        await _db.SaveChangesAsync();

        if (!string.IsNullOrEmpty(mediaKey))
        {
            try
            {
                await _r2.DeleteAsync(mediaKey);
            }
            catch { }
        }

        return (true, null, message.GroupId);
    }
}