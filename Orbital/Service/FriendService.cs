using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Orbital.Data;
using Orbital.DTOs;
using Orbital.Models;
using System.Threading.Tasks;
using System;

namespace Orbital.Service;

public class FriendService
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly PushNotificationService _pushService; // 🔥 Added

    public FriendService(
        ApplicationDbContext db,
        UserManager<ApplicationUser> userManager,
        PushNotificationService pushService) // 🔥 Added
    {
        _db = db;
        _userManager = userManager;
        _pushService = pushService;
    }

    public async Task<(bool Success, string? Error)> SendRequestAsync(string senderId, SendFriendRequestDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.TargetIdentifier))
            return (false, "Target handle or email is required.");

        var targetInput = dto.TargetIdentifier.Trim().ToLowerInvariant();
        ApplicationUser? targetUser;

        if (targetInput.Contains('@') && !targetInput.EndsWith("@orbit"))
        {
            targetUser = await _userManager.FindByEmailAsync(targetInput);
        }
        else
        {
            var cleanHandle = targetInput.Replace("@orbit", "");
            targetUser = await _db.Users.FirstOrDefaultAsync(u => u.UserHandle == cleanHandle && u.Realm == "orbit");
        }

        if (targetUser == null)
            return (false, "User not found.");

        if (targetUser.Id == senderId)
            return (false, "You cannot send a friend request to yourself.");

        // Refuse if either side has blocked the other.
        var blockedEitherWay = await _db.Friendships.AnyAsync(f =>
            ((f.RequesterId == senderId && f.AddresseeId == targetUser.Id) ||
             (f.RequesterId == targetUser.Id && f.AddresseeId == senderId)) &&
            f.BlockedById != null);

        if (blockedEitherWay)
            return (false, "Unable to send friend request.");

        var existing = await _db.Friendships.FirstOrDefaultAsync(f =>
            (f.RequesterId == senderId && f.AddresseeId == targetUser.Id) ||
            (f.RequesterId == targetUser.Id && f.AddresseeId == senderId));

        if (existing != null)
        {
            if (existing.Status == FriendshipStatus.Accepted)
                return (false, "You are already friends with this user.");

            if (existing.Status == FriendshipStatus.Pending)
                return (false, "A friend request is already pending.");
        }

        var friendship = new Friendship
        {
            RequesterId = senderId,
            AddresseeId = targetUser.Id,
            Status = FriendshipStatus.Pending,
            CreatedAt = DateTime.UtcNow
        };

        await _db.Friendships.AddAsync(friendship);
        await _db.SaveChangesAsync();

        // 🔥 TRIGGER FRIEND REQUEST PUSH NOTIFICATION
        var senderUser = await _userManager.FindByIdAsync(senderId);
        if (targetUser != null && !string.IsNullOrEmpty(targetUser.FcmToken) && senderUser != null)
        {
            _ = Task.Run(() => _pushService.SendMessagePushAsync(
                targetUser.FcmToken,
                "New Friend Request",
                $"{senderUser.DisplayName} sent you a friend request.",
                senderId, // Pass senderId so the app can route to their profile/requests
                "friend_request" // 🔥 Specific type for the frontend
            ));
        }

        return (true, null);
    }

    public async Task<(bool Success, string? Error)> RespondToRequestAsync(string userId, RespondFriendRequestDto dto)
    {
        var friendship = await _db.Friendships.FirstOrDefaultAsync(f => f.Id == dto.FriendshipId);

        if (friendship == null)
            return (false, "Friend request not found.");

        if (friendship.AddresseeId != userId)
            return (false, "You are not authorized to respond to this request.");

        if (friendship.Status != FriendshipStatus.Pending)
            return (false, "Friend request has already been handled.");

        if (dto.Accept)
        {
            friendship.Status = FriendshipStatus.Accepted;
            friendship.UpdatedAt = DateTime.UtcNow;

            // 🔥 TRIGGER ACCEPTED PUSH NOTIFICATION
            var requester = await _userManager.FindByIdAsync(friendship.RequesterId);
            var accepter = await _userManager.FindByIdAsync(userId);

            if (requester != null && !string.IsNullOrEmpty(requester.FcmToken) && accepter != null)
            {
                _ = Task.Run(() => _pushService.SendMessagePushAsync(
                    requester.FcmToken,
                    "Friend Request Accepted",
                    $"{accepter.DisplayName} accepted your friend request.",
                    userId, // Pass accepter's ID
                    "friend_accept"
                ));
            }
        }
        else
        {
            _db.Friendships.Remove(friendship);
        }

        await _db.SaveChangesAsync();
        return (true, null);
    }

    public async Task<List<FriendDto>> GetFriendsListAsync(string userId)
    {
        var friendships = await _db.Friendships
            .AsNoTracking()
            .Include(f => f.Requester)
            .Include(f => f.Addressee)
            .Where(f =>
                (f.RequesterId == userId || f.AddresseeId == userId) &&
                f.Status == FriendshipStatus.Accepted &&
                f.BlockedById == null)
            .ToListAsync();

        return friendships.Select(f =>
        {
            var friend = f.RequesterId == userId ? f.Addressee : f.Requester;

            return new FriendDto(
                FriendshipId: f.Id,
                UserId: friend.Id,
                Handle: friend.UserHandle,
                FullHandle: $"{friend.UserHandle}@{friend.Realm}",
                DisplayName: friend.DisplayName,
                AvatarUrl: friend.AvatarUrl,
                IsOnline: false,
                EstablishedAt: f.UpdatedAt ?? f.CreatedAt
            );
        }).ToList();
    }

    public async Task<List<PendingFriendRequestDto>> GetPendingRequestsAsync(string userId)
    {
        return await _db.Friendships
            .AsNoTracking()
            .Include(f => f.Requester)
            .Where(f =>
                f.AddresseeId == userId &&
                f.Status == FriendshipStatus.Pending &&
                f.BlockedById == null)
            .OrderByDescending(f => f.CreatedAt)
            .Select(f => new PendingFriendRequestDto(
                f.Id,
                f.Requester.Id,
                f.Requester.UserHandle,
                f.Requester.DisplayName,
                f.Requester.AvatarUrl,
                f.CreatedAt
            ))
            .ToListAsync();
    }

    public async Task<(bool Success, string? Error)> UnfriendAsync(
        string userId,
        string targetUserId)
    {
        var friendship = await _db.Friendships.FirstOrDefaultAsync(f =>
            (f.RequesterId == userId && f.AddresseeId == targetUserId) ||
            (f.RequesterId == targetUserId && f.AddresseeId == userId));

        if (friendship == null)
            return (false, "You are not friends with this user.");

        _db.Friendships.Remove(friendship);
        await _db.SaveChangesAsync();

        return (true, null);
    }

    public async Task<(bool Success, string? Error)> SetBlockedAsync(
        string userId,
        string targetUserId,
        bool blocked)
    {
        if (userId == targetUserId)
            return (false, "You cannot block yourself.");

        var friendship = await _db.Friendships.FirstOrDefaultAsync(f =>
            (f.RequesterId == userId && f.AddresseeId == targetUserId) ||
            (f.RequesterId == targetUserId && f.AddresseeId == userId));

        if (blocked)
        {
            // If the other side already blocked you, you can't block them back.
            if (friendship?.BlockedById != null && friendship.BlockedById != userId)
                return (false, "This user has blocked you.");

            if (friendship == null)
            {
                // No prior relationship — create a placeholder row carrying the block.
                friendship = new Friendship
                {
                    RequesterId = userId,
                    AddresseeId = targetUserId,
                    Status = FriendshipStatus.Pending,
                    BlockedById = userId,
                    CreatedAt = DateTime.UtcNow,
                };
                await _db.Friendships.AddAsync(friendship);
            }
            else if (friendship.BlockedById == userId)
            {
                return (true, null); // already blocked by you, idempotent
            }
            else
            {
                friendship.BlockedById = userId;
                friendship.UpdatedAt = DateTime.UtcNow;
            }

            await _db.SaveChangesAsync();
            return (true, null);
        }

        // Unblock
        if (friendship == null || friendship.BlockedById == null)
            return (true, null); // no-op

        if (friendship.BlockedById != userId)
            return (false, "You did not block this user.");

        // Placeholder row (never a real friendship) → delete.
        // Real friendship row → just clear the flag.
        if (friendship.Status == FriendshipStatus.Pending)
        {
            _db.Friendships.Remove(friendship);
        }
        else
        {
            friendship.BlockedById = null;
            friendship.UpdatedAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync();
        return (true, null);
    }

    public async Task<List<UserProfileDto>> GetBlockedUsersAsync(string userId)
    {
        var rows = await _db.Friendships
            .AsNoTracking()
            .Include(f => f.Requester)
            .Include(f => f.Addressee)
            .Where(f => f.BlockedById == userId)
            .OrderByDescending(f => f.UpdatedAt ?? f.CreatedAt)
            .ToListAsync();

        var result = new List<UserProfileDto>();
        foreach (var f in rows)
        {
            var other = f.RequesterId == userId ? f.Addressee : f.Requester;
            if (other == null) continue;

            result.Add(new UserProfileDto(
                other.Id,
                other.UserHandle,
                $"{other.UserHandle}@{other.Realm}",
                other.DisplayName,
                other.AvatarUrl,
                other.CreatedAt,
                other.Email
            ));
        }

        return result;
    }

    public async Task<List<string>> GetBlockedUserIdsAsync(string userId)
    {
        return await _db.Friendships
            .AsNoTracking()
            .Where(f => f.BlockedById == userId)
            .Select(f => f.RequesterId == userId ? f.AddresseeId : f.RequesterId)
            .ToListAsync();
    }
}