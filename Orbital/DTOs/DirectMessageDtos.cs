namespace Orbital.DTOs;

public record SendDirectMessageDto(
    string RecipientId,
    string? Ciphertext,
    string? Nonce,
    string? MediaUrl,
    string? MediaKey,
    int? MediaType
);

public record DirectMessageDto(
    long Id,
    string SenderId,
    string RecipientId,
    string? Ciphertext,
    string? Nonce,
    string? Content,
    string? MediaUrl,
    string? MediaKey,
    int? MediaType,
    bool IsRead,
    DateTime SentAt
);