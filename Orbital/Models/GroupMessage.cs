namespace Orbital.Models;

public class GroupMessage
{
    public long Id { get; set; }

    public string Content { get; set; } = string.Empty;

    public string? MediaUrl { get; set; }

    // 0 = Text, 1 = Image, 2 = Video, 3 = Audio, 4 = Document
    public int MediaType { get; set; } = 0;

    public DateTime SentAt { get; set; } = DateTime.UtcNow;

    // Foreign Keys
    public int GroupId { get; set; }
    public Group Group { get; set; } = null!;

    public string SenderId { get; set; } = string.Empty;
    public ApplicationUser Sender { get; set; } = null!;
}