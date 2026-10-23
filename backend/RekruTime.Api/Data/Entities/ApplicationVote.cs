namespace RekruTime.Api.Data;

public enum VoteDecision { Approve, Reject }

// One vote per user per application; changeable until a final decision is made.
public class ApplicationVote
{
    public int Id { get; set; }
    public int ApplicationId { get; set; }
    public required string UserId { get; set; }
    public AppUser User { get; set; } = null!;
    public VoteDecision Decision { get; set; }
    public bool IsFinal { get; set; } // an uninvolved admin settling a Disputed application
    public string? Comment { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
