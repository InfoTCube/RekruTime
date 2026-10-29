namespace RekruTime.Api.Data;

// Submitted → Preselected | NotPreselected → InterviewInvited come from forms (phase 3); Pending onwards from votes.
public enum ApplicationStatus { Pending, Disputed, Accepted, Rejected, Submitted, Preselected, NotPreselected, InterviewInvited }

public class Application
{
    public int Id { get; set; }
    public int RecruitId { get; set; }
    public int SectionId { get; set; }
    public Section Section { get; set; } = null!;
    public ApplicationStatus Status { get; set; } = ApplicationStatus.Pending;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? DecisionSentAt { get; set; } // decision e-mail queued → votes locked
}
