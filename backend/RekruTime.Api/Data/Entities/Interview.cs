namespace RekruTime.Api.Data;

public enum InterviewStatus { Scheduled, Cancelled, Done }

public class Interview
{
    public int Id { get; set; }
    public int RecruitId { get; set; }
    public Recruit Recruit { get; set; } = null!;
    public int? BookingLinkId { get; set; }
    public DateTime Start { get; set; }
    public DateTime End { get; set; }
    public DateTime BlockedUntil { get; set; } // End + buffer; the room is taken until then
    public InterviewStatus Status { get; set; } = InterviewStatus.Scheduled;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ReminderSentAt { get; set; } // ReminderJob; reset when the time changes
    public List<Section> Sections { get; set; } = [];
    public List<AppUser> Interviewers { get; set; } = [];
}
