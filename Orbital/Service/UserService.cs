using Microsoft.EntityFrameworkCore;
using Orbital.Data;
using Orbital.DTOs;

namespace Orbital.Service;

public class UserService
{
    private readonly ApplicationDbContext _db;

    public UserService(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<UserProfileDto?> GetProfileByIdAsync(string userId)
    {
        var user = await _db.Users.FindAsync(userId);
        if (user == null) return null;

        return new UserProfileDto(
            user.Id,
            user.UserHandle,
            $"{user.UserHandle}@{user.Realm}",
            user.DisplayName,
            user.AvatarUrl,
            user.CreatedAt
        );
    }

    public async Task<UserProfileDto?> UpdateProfileAsync(string userId, UpdateProfileDto dto)
    {
        var user = await _db.Users.FindAsync(userId);
        if (user == null) return null;

        if (!string.IsNullOrWhiteSpace(dto.DisplayName))
            user.DisplayName = dto.DisplayName.Trim();

        if (dto.AvatarUrl != null)
            user.AvatarUrl = dto.AvatarUrl;

        await _db.SaveChangesAsync();

        return new UserProfileDto(
            user.Id,
            user.UserHandle,
            $"{user.UserHandle}@{user.Realm}",
            user.DisplayName,
            user.AvatarUrl,
            user.CreatedAt
        );
    }
}