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
        if (string.IsNullOrWhiteSpace(dto.Content) && string.IsNullOrWhiteSpace(dto.MediaUrl))
            return (false, "Message cannot be empty.", null);

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
            Content = dto.Content?.Trim() ?? string.Empty,
            MediaUrl = dto.MediaUrl,
            MediaType = dto.MediaType,
            IsRead = false,
            SentAt = DateTime.UtcNow
        };

        await _db.DirectMessages.AddAsync(message);
        await _db.SaveChangesAsync();

        var messageDto = new DirectMessageDto(
            message.Id,
            message.SenderId,
            message.RecipientId,
            message.Content,
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
        var query = _db.DirectMessages
            .AsNoTracking()
            .Where(m => (m.SenderId == currentUserId && m.RecipientId == targetUserId) ||
                        (m.SenderId == targetUserId && m.RecipientId == currentUserId));

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
                m.Content,
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
            .Where(m => m.RecipientId == currentUserId && m.SenderId == senderId && !m.IsRead)
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

    public async Task<(bool Success, string? Error)> DeleteMessageAsync(
        string userId,
        long messageId)
    {
        var message = await _db.DirectMessages.FindAsync(messageId);
        if (message == null)
            return (false, "Message not found.");

        if (message.SenderId != userId)
            return (false, "You can only delete your own messages.");

        _db.DirectMessages.Remove(message);
        await _db.SaveChangesAsync();

        return (true, null);
    }

    // ─── NEW: Clear Conversation ────────────────────────────────

    /// <summary>
    /// Deletes every message between the current user and the target user,
    /// in both directions. Removes the conversation for both sides.
    /// </summary>
    public async Task<(bool Success, string? Error)> ClearConversationAsync(
        string userId,
        string targetUserId)
    {
        var messages = await _db.DirectMessages
            .Where(m =>
                (m.SenderId == userId && m.RecipientId == targetUserId) ||
                (m.SenderId == targetUserId && m.RecipientId == userId))
            .ToListAsync();

        if (messages.Count == 0)
            return (true, null); // nothing to delete, still success

        _db.DirectMessages.RemoveRange(messages);
        await _db.SaveChangesAsync();

        return (true, null);
    }
}