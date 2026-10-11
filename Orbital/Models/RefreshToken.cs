namespace Orbital.Models;

public class RefreshToken
{
    public int Id { get; set; }

    public string Token { get; set; } = string.Empty;

    public string JwtId { get; set; } = string.Empty; // Matches the JWT 'jti' claim

    public bool IsUsed { get; set; }

    public bool IsRevoked { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime ExpiryDate { get; set; }

    // Foreign Key
    public string UserId { get; set; } = string.Empty;
    public ApplicationUser User { get; set; } = null!;
}