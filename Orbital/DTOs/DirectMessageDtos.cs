namespace Orbital.DTOs;

public record SendDirectMessageDto(
    string RecipientId,
    string? Ciphertext,
    string? Nonce,
    string? MediaUrl,
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
    int? MediaType,
    bool IsRead,
    DateTime SentAt
);