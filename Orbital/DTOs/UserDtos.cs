namespace Orbital.DTOs;

public record UserProfileDto(
    string Id,
    string UserHandle,
    string FullHandle,      // Formatted as "user@orbit"
    string DisplayName,
    string? AvatarUrl,
    DateTime CreatedAt
);

public record UpdateProfileDto(
    string DisplayName,
    string? AvatarUrl
);