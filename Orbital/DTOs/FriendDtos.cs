namespace Orbital.DTOs;

public record SendFriendRequestDto(
    string TargetIdentifier // Can be "alice" or "alice@orbit"
);

public record RespondFriendRequestDto(
    int FriendshipId,
    bool Accept
);

public record FriendDto(
    int FriendshipId,
    string UserId,
    string Handle,
    string FullHandle,
    string DisplayName,
    string? AvatarUrl,
    bool IsOnline,
    DateTime EstablishedAt
);

public record PendingFriendRequestDto(
    int FriendshipId,
    string RequesterId,
    string RequesterHandle,
    string RequesterDisplayName,
    string? RequesterAvatarUrl,
    DateTime SentAt
);