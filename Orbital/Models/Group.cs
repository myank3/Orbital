namespace Orbital.Models;

public class Group
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public string? GroupAvatarUrl { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Creator / Owner tracking
    public string CreatedById { get; set; } = string.Empty;
    public ApplicationUser CreatedBy { get; set; } = null!;

    // Navigation properties
    public ICollection<GroupMember> Members { get; set; } = new List<GroupMember>();
    public ICollection<GroupMessage> Messages { get; set; } = new List<GroupMessage>();
}