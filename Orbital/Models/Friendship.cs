namespace Orbital.Models;

public enum FriendshipStatus
{
    Pending = 0,
    Accepted = 1,
    Declined = 2,
    Blocked = 3
}

public class Friendship
{
    public int Id { get; set; }

    public string RequesterId { get; set; } = string.Empty;
    public ApplicationUser Requester { get; set; } = null!;

    public string AddresseeId { get; set; } = string.Empty;
    public ApplicationUser Addressee { get; set; } = null!;

    public FriendshipStatus Status { get; set; } = FriendshipStatus.Pending;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    public string? BlockedById { get; set; }
}