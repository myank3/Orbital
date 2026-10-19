namespace Orbital.DTOs;

// Channel Management
public record CreateGroupDto(
    string Name,
    string? Description,
    string? GroupAvatarUrl
);

public record AddGroupMemberDto(
    int GroupId,
    string UserId
);

public record GroupDto(
    int Id,
    string Name,
    string? Description,
    string? GroupAvatarUrl,
    string CreatedById,
    DateTime CreatedAt,
    int MemberCount
);

public record GroupDetailDto(
    int Id,
    string Name,
    string? Description,
    string? GroupAvatarUrl,
    string CreatedById,
    DateTime CreatedAt,
    List<UserProfileDto> Members
);

// Group Messaging
public record SendGroupMessageDto(
    int GroupId,
    string Content,
    string? MediaUrl,
    string? MediaKey,
    int MediaType
);

public record GroupMessageDto(
    long Id,
    int GroupId,
    string SenderId,
    string SenderDisplayName,
    string? SenderAvatarUrl,
    string Content,
    string? MediaUrl,
    string? MediaKey,
    int MediaType,
    DateTime SentAt,
    DateTime? DeletedAt = null
);