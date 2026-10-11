using Microsoft.AspNetCore.Identity;

namespace Orbital.Models
{
    public class ApplicationUser : IdentityUser
    {
        // The prefix chosen by the user (e.g. "mayank")
        public string UserHandle { get; set; } = string.Empty;

        // Fixed realm ("orbit") to compose "user@orbit"
        public string Realm { get; set; } = "orbit";

        public string DisplayName { get; set; } = string.Empty;

        public string? AvatarUrl { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Navigation properties
        public ICollection<RefreshToken> RefreshTokens { get; set; } = new List<RefreshToken>();
        public ICollection<GroupMember> GroupMemberships { get; set; } = new List<GroupMember>();
    }
}
