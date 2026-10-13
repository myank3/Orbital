using Microsoft.EntityFrameworkCore;
using Orbital.Data;
using Orbital.DTOs;
using Orbital.Models;

namespace Orbital.Service;

public class DirectMessageService
{
    private readonly ApplicationDbContext _db;

    public DirectMessageService(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<(bool Success, string? Error, DirectMessageDto? Message)> SendMessageAsync(
        string senderId,
        SendDirectMessageDto dto)
    {
        // E2EE: accept an encrypted payload (ciphertext+nonce) or media-only.
        var hasCiphertext = !string.IsNullOrWhiteSpace(dto.Ciphertext);
        var hasMedia = !string.IsNullOrWhiteSpace(dto.MediaUrl);

        if (!hasCiphertext && !hasMedia)
            return (false, "Message cannot be empty.", null);

        if (hasCiphertext && string.IsNullOrWhiteSpace(dto.Nonce))
            return (false, "Encrypted payload missing nonce.", null);

        var areFriends = await _db.Friendships.AnyAsync(f =>
            ((f.RequesterId == senderId && f.AddresseeId == dto.RecipientId) ||
             (f.RequesterId == dto.RecipientId && f.AddresseeId == senderId)) &&
            f.Status == FriendshipStatus.Accepted);

        if (!areFriends)
            return (false, "You can only message confirmed friends.", null);

        var message = new DirectMessage
        {
            SenderId = senderId,
            RecipientId = dto.RecipientId,
            Content = string.Empty, // legacy column — unused for new rows
            Ciphertext = dto.Ciphertext,
            Nonce = dto.Nonce,
            MediaUrl = dto.MediaUrl,
            MediaType = dto.MediaType ?? 0,
            IsRead = false,
            SentAt = DateTime.UtcNow,
        };

        await _db.DirectMessages.AddAsync(message);
        await _db.SaveChangesAsync();

        var messageDto = new DirectMessageDto(
            message.Id,
            message.SenderId,
            message.RecipientId,
            message.Ciphertext,
            message.Nonce,
            null, // never echo legacy Content back for a new message
            message.MediaUrl,
            message.MediaType,
            message.IsRead,
            message.SentAt
        );

        return (true, null, messageDto);
    }

    public async Task<List<DirectMessageDto>> GetChatHistoryAsync(
        string currentUserId,
        string targetUserId,
        long? beforeId = null,
        int take = 50)
    {
        // Only show messages the caller hasn't hidden for themselves.
        var query = _db.DirectMessages
            .AsNoTracking()
            .Where(m =>
                (m.SenderId == currentUserId &&
                 m.RecipientId == targetUserId &&
                 !m.HiddenForSender) ||
                (m.SenderId == targetUserId &&
                 m.RecipientId == currentUserId &&
                 !m.HiddenForRecipient));

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
            .Select(m => new DirectMessageDto(
                m.Id,
                m.SenderId,
                m.RecipientId,
                m.Ciphertext,
                m.Nonce,
                // Legacy rows (pre-E2EE): return plaintext Content so the
                // frontend can still render them. New rows have Ciphertext
                // set and empty Content → return null.
                string.IsNullOrEmpty(m.Ciphertext) ? m.Content : null,
                m.MediaUrl,
                m.MediaType,
                m.IsRead,
                m.SentAt
            ))
            .ToList();
    }

    public async Task MarkAsReadAsync(string currentUserId, string senderId)
    {
        var unreadMessages = await _db.DirectMessages
            .Where(m =>
                m.RecipientId == currentUserId &&
                m.SenderId == senderId &&
                !m.IsRead &&
                !m.HiddenForRecipient)
            .ToListAsync();

        if (unreadMessages.Count > 0)
        {
            foreach (var msg in unreadMessages)
            {
                msg.IsRead = true;
            }
            await _db.SaveChangesAsync();
        }
    }

    /// <summary>
    /// WhatsApp-style "Delete for me": hides the message from the caller's own view.
    /// </summary>
    public async Task<(bool Success, string? Error)> DeleteMessageAsync(
        string userId,
        long messageId)
    {
        var message = await _db.DirectMessages.FindAsync(messageId);
        if (message == null)
            return (false, "Message not found.");

        if (message.SenderId == userId)
        {
            if (message.HiddenForSender)
                return (true, null);
            message.HiddenForSender = true;
        }
        else if (message.RecipientId == userId)
        {
            if (message.HiddenForRecipient)
                return (true, null);
            message.HiddenForRecipient = true;
        }
        else
        {
            return (false, "You are not a participant in this conversation.");
        }

        await _db.SaveChangesAsync();
        return (true, null);
    }

    /// <summary>
    /// WhatsApp-style "Delete for everyone": only the original sender may
    /// remove the message for both sides. Hard-deletes the row.
    /// </summary>
    public async Task<(bool Success, string? Error)> DeleteForEveryoneAsync(
        string userId,
        long messageId)
    {
        var message = await _db.DirectMessages.FindAsync(messageId);
        if (message == null)
            return (false, "Message not found.");

        if (message.SenderId != userId)
            return (false, "You can only delete your own messages for everyone.");

        _db.DirectMessages.Remove(message);
        await _db.SaveChangesAsync();
        return (true, null);
    }

    /// <summary>
    /// WhatsApp-style "Clear chat": hides every message between the caller and
    /// the target user from the caller's view only.
    /// </summary>
    public async Task<(bool Success, string? Error)> ClearConversationAsync(
        string userId,
        string targetUserId)
    {
        await _db.DirectMessages
            .Where(m =>
                m.SenderId == userId &&
                m.RecipientId == targetUserId &&
                !m.HiddenForSender)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.HiddenForSender, true));

        await _db.DirectMessages
            .Where(m =>
                m.SenderId == targetUserId &&
                m.RecipientId == userId &&
                !m.HiddenForRecipient)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.HiddenForRecipient, true));

        return (true, null);
    }
}