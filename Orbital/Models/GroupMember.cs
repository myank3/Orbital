namespace Orbital.Models;

public class GroupMember
{
    public int GroupId { get; set; }
    public Group Group { get; set; } = null!;

    public string UserId { get; set; } = string.Empty;
    public ApplicationUser User { get; set; } = null!;

    public DateTime JoinedAt { get; set; } = DateTime.UtcNow;
}