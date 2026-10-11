namespace Orbital.Models;

public class DirectMessage
{
    public long Id { get; set; } // bigint for high throughput

    public string Content { get; set; } = string.Empty;

    public string? MediaUrl { get; set; }

    // 0 = None/Text, 1 = Image, 2 = Video, 3 = Audio, 4 = Document
    public int MediaType { get; set; } = 0;

    public bool IsRead { get; set; } = false;

    public DateTime SentAt { get; set; } = DateTime.UtcNow;

    // Foreign Keys
    public string SenderId { get; set; } = string.Empty;
    public ApplicationUser Sender { get; set; } = null!;

    public string RecipientId { get; set; } = string.Empty;
    public ApplicationUser Recipient { get; set; } = null!;
}