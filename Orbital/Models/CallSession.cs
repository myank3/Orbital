namespace Orbital.Models;

public enum CallType
{
    Audio = 0,
    Video = 1
}

public enum CallStatus
{
    Ringing = 0,
    Active = 1,
    Ended = 2,
    Missed = 3,
    Rejected = 4
}

public class CallSession
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public CallType Type { get; set; }
    public CallStatus Status { get; set; } = CallStatus.Ringing;

    public string InitiatorId { get; set; } = string.Empty;
    public ApplicationUser Initiator { get; set; } = null!;

    // Optional for Direct Call (null if Group Call)
    public string? PeerId { get; set; }
    public ApplicationUser? Peer { get; set; }

    // Optional for Group Call (null if Direct Call)
    public int? GroupId { get; set; }
    public Group? Group { get; set; }

    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? EndedAt { get; set; }
}