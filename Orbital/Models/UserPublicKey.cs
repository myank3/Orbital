namespace Orbital.Models;

public class UserPublicKey
{
    public string UserId { get; set; } = null!;
    public ApplicationUser User { get; set; } = null!;
    public string PublicKey { get; set; } = null!;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}