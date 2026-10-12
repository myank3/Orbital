using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Orbital.Data;
using Orbital.DTOs;
using Orbital.Models;

namespace Orbital.Service;

public class FriendService
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;

    public FriendService(ApplicationDbContext db, UserManager<ApplicationUser> userManager)
    {
        _db = db;
        _userManager = userManager;
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

        var existing = await _db.Friendships.FirstOrDefaultAsync(f =>
            (f.RequesterId == senderId && f.AddresseeId == targetUser.Id) ||
            (f.RequesterId == targetUser.Id && f.AddresseeId == senderId));

        if (existing != null)
        {
            if (existing.Status == FriendshipStatus.Accepted)
                return (false, "You are already friends with this user.");

            if (existing.Status == FriendshipStatus.Pending)
                return (false, "A friend request is already pending.");

            if (existing.Status == FriendshipStatus.Blocked)
                return (false, "Unable to send friend request.");
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
            .Where(f => (f.RequesterId == userId || f.AddresseeId == userId) && f.Status == FriendshipStatus.Accepted)
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
            .Where(f => f.AddresseeId == userId && f.Status == FriendshipStatus.Pending)
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

    // ─── NEW: Unfriend and Block ────────────────────────────────

    /// <summary>
    /// Removes an accepted friendship. Works for either direction —
    /// you can unfriend someone you accepted, or someone who added you.
    /// </summary>
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

    /// <summary>
    /// Toggles the friendship row into (or out of) the Blocked state.
    /// Blocking preserves the row so the SendRequestAsync guard can
    /// refuse to re-add the pair. Unblocking reverts to Pending so
    /// either side can send a fresh request.
    /// </summary>
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

        if (friendship == null)
        {
            if (!blocked)
                return (true, null); // unblock with no existing row = no-op

            friendship = new Friendship
            {
                RequesterId = userId,
                AddresseeId = targetUserId,
                Status = FriendshipStatus.Blocked,
                CreatedAt = DateTime.UtcNow
            };
            await _db.Friendships.AddAsync(friendship);
        }
        else
        {
            friendship.Status = blocked
                ? FriendshipStatus.Blocked
                : FriendshipStatus.Pending;
            friendship.UpdatedAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync();
        return (true, null);
    }

    /// <summary>
    /// Returns the IDs of all users the caller has blocked.
    /// Only returns rows where the caller was the one who initiated the
    /// block (Status == Blocked and RequesterId == userId). If the other
    /// user blocked them instead, that user does not appear here.
    /// </summary>
    /// /// <summary>
    /// Returns full profiles of everyone the caller has blocked.
    /// Only returns rows where the caller initiated the block.
    /// </summary>
    public async Task<List<UserProfileDto>> GetBlockedUsersAsync(string userId)
    {
        var blocked = await _db.Friendships
            .AsNoTracking()
            .Include(f => f.Addressee)
            .Where(f =>
                f.RequesterId == userId &&
                f.Status == FriendshipStatus.Blocked)
            .OrderByDescending(f => f.UpdatedAt ?? f.CreatedAt)
            .ToListAsync();

        return blocked
            .Where(f => f.Addressee != null)
            .Select(f => new UserProfileDto(
                f.Addressee.Id,
                f.Addressee.UserHandle,
                $"{f.Addressee.UserHandle}@{f.Addressee.Realm}",
                f.Addressee.DisplayName,
                f.Addressee.AvatarUrl,
                f.Addressee.CreatedAt,
                f.Addressee.Email
            ))
            .ToList();
    }
    public async Task<List<string>> GetBlockedUserIdsAsync(string userId)
    {
        return await _db.Friendships
            .AsNoTracking()
            .Where(f =>
                f.RequesterId == userId &&
                f.Status == FriendshipStatus.Blocked)
            .Select(f => f.AddresseeId)
            .ToListAsync();
    }
}