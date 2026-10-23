namespace RekruTime.Api.Data;

// An interviewer's own note on an interview; one per author per interview.
public class InterviewNote
{
    public int Id { get; set; }
    public int InterviewId { get; set; }
    public string? AuthorId { get; set; } // null = the author's account was deleted; the note stays for voting
    public AppUser? Author { get; set; }
    public string Text { get; set; } = "";
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
