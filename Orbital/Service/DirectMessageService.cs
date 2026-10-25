using Microsoft.EntityFrameworkCore;
using Orbital.Data;
using Orbital.DTOs;
using Orbital.Models;
using Orbital.Service;
using Microsoft.AspNetCore.Identity; // Required for UserManager

namespace Orbital.Service;

public class DirectMessageService
{
    private readonly ApplicationDbContext _db;
    private readonly R2StorageService _r2;
    private readonly PushNotificationService _pushService; // 🔥 Added
    private readonly UserManager<ApplicationUser> _userManager; // 🔥 Added

    public DirectMessageService(
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

    public async Task<(bool Success, string? Error, DirectMessageDto? Message)> SendMessageAsync(
        string senderId,
        SendDirectMessageDto dto)
    {
        // E2EE: accept an encrypted payload (ciphertext+nonce) or media-only.
        var hasCiphertext = !string.IsNullOrWhiteSpace(dto.Ciphertext);
        var hasMedia = !string.IsNullOrWhiteSpace(dto.MediaUrl)
                    || !string.IsNullOrWhiteSpace(dto.MediaKey);

        if (!hasCiphertext && !hasMedia)
            return (false, "Message cannot be empty.", null);

        if (hasCiphertext && string.IsNullOrWhiteSpace(dto.Nonce))
            return (false, "Encrypted payload missing nonce.", null);

        // R2 key must belong to the sender (prefix check).
        if (!string.IsNullOrEmpty(dto.MediaKey))
        {
            var expectedPrefix = $"dm/{senderId}/";
            if (!dto.MediaKey.StartsWith(expectedPrefix, StringComparison.Ordinal))
                return (false, "Invalid media key.", null);
        }

        var friendship = await _db.Friendships
            .AsNoTracking()
            .FirstOrDefaultAsync(f =>
                (f.RequesterId == senderId && f.AddresseeId == dto.RecipientId) ||
                (f.RequesterId == dto.RecipientId && f.AddresseeId == senderId));

        if (friendship == null || friendship.Status != FriendshipStatus.Accepted)
            return (false, "You can only message confirmed friends.", null);

        // Blocked either direction → refuse silently
        if (friendship.BlockedById != null)
            return (false, "You can't message this user.", null);

        var message = new DirectMessage
        {
            SenderId = senderId,
            RecipientId = dto.RecipientId,
            Content = string.Empty, // legacy column — unused for new rows
            Ciphertext = dto.Ciphertext,
            Nonce = dto.Nonce,
            MediaUrl = dto.MediaUrl,
            MediaKey = dto.MediaKey,
            MediaType = dto.MediaType ?? 0,
            IsRead = false,
            SentAt = DateTime.UtcNow,
        };

        await _db.DirectMessages.AddAsync(message);
        await _db.SaveChangesAsync();

        // 🔥 TRIGGER PUSH NOTIFICATION HERE
        var recipient = await _userManager.FindByIdAsync(dto.RecipientId);
        var sender = await _userManager.FindByIdAsync(senderId);

        if (recipient != null && !string.IsNullOrEmpty(recipient.FcmToken))
        {
            // Because it's E2EE, the server can't read the message.
            // We use the sender's handle for the title.
            var title = sender?.UserHandle ?? "New Message";
            var body = hasMedia ? "📷 Sent you an attachment" : "Sent you a message";

            // Fire and forget (don't await, so the API responds instantly)
            _ = Task.Run(() => _pushService.SendMessagePushAsync(
                recipient.FcmToken,
                title,
                body,
                senderId // We pass senderId as the chatId so the app knows who to open
            ));
        }

        var messageDto = new DirectMessageDto(
            message.Id,
            message.SenderId,
            message.RecipientId,
            message.Ciphertext,
            message.Nonce,
            null, // never echo legacy Content back for a new message
            message.MediaUrl,
            message.MediaKey,
            message.MediaType,
            message.IsRead,
            message.SentAt
        );

        return (true, null, messageDto);
    }

    public async Task<PresignedMessagesResponse<DirectMessageDto>> GetChatHistoryAsync(
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

        var rows = await query
            .OrderByDescending(m => m.SentAt)
            .Take(take)
            .ToListAsync();

        var messages = rows
            .OrderBy(m => m.SentAt)
            .Select(m => new DirectMessageDto(
                m.Id,
                m.SenderId,
                m.RecipientId,
                m.Ciphertext,
                m.Nonce,
                string.IsNullOrEmpty(m.Ciphertext) ? m.Content : null,
                m.MediaUrl,
                m.MediaKey,
                m.MediaType,
                m.IsRead,
                m.SentAt
            ))
            .ToList();

        // Batch-presign every R2 key in this page. Empty dict if none.
        var keys = rows
            .Where(m => !string.IsNullOrEmpty(m.MediaKey))
            .Select(m => m.MediaKey!)
            .ToList();

        var presigned = keys.Count > 0
            ? _r2.PresignGetBatch(keys, TimeSpan.FromHours(4))
            : new Dictionary<string, string>();

        return new PresignedMessagesResponse<DirectMessageDto>(messages, presigned);
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

    public async Task<(bool Success, string? Error)> DeleteForEveryoneAsync(
        string userId,
        long messageId)
    {
        var message = await _db.DirectMessages.FindAsync(messageId);
        if (message == null)
            return (false, "Message not found.");

        if (message.SenderId != userId)
            return (false, "You can only delete your own messages for everyone.");

        var mediaKey = message.MediaKey;

        _db.DirectMessages.Remove(message);
        await _db.SaveChangesAsync();

        if (!string.IsNullOrEmpty(mediaKey))
        {
            try
            {
                await _r2.DeleteAsync(mediaKey);
            }
            catch
            {
                // Orphan object — acceptable. Not worth failing the API call.
            }
        }

        return (true, null);
    }

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