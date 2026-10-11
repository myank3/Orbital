namespace Orbital.DTOs;

public record SendDirectMessageDto(
    string RecipientId,
    string Content,
    string? MediaUrl,
    int MediaType           // 0: None, 1: Image, 2: Video, 3: Audio, 4: Document
);

public record DirectMessageDto(
    long Id,
    string SenderId,
    string RecipientId,
    string Content,
    string? MediaUrl,
    int MediaType,
    bool IsRead,
    DateTime SentAt
);