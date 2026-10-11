using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Orbital.Data;
using Orbital.DTOs;

namespace Orbital.Service;

public class UserService
{
    private readonly ApplicationDbContext _db;
    private readonly FileStorageService _storage;

    public UserService(ApplicationDbContext db, FileStorageService storage)
    {
        _db = db;
        _storage = storage;
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
            user.CreatedAt,
            user.Email
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
            user.CreatedAt,
            user.Email
        );
    }

    public async Task<UserProfileDto?> UpdateAvatarAsync(string userId, IFormFile file)
    {
        var user = await _db.Users.FindAsync(userId);
        if (user == null) return null;

        var relativeUrl = await _storage.SaveAsync(file, "avatars", maxBytes: 5 * 1024 * 1024);
        if (relativeUrl == null) return null;

        user.AvatarUrl = relativeUrl;
        await _db.SaveChangesAsync();

        return new UserProfileDto(
            user.Id,
            user.UserHandle,
            $"{user.UserHandle}@{user.Realm}",
            user.DisplayName,
            user.AvatarUrl,
            user.CreatedAt,
            user.Email
        );
    }
}