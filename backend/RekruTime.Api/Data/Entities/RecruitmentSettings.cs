namespace RekruTime.Api.Data;

// Single row (Id = 1). Interview dates in the club time zone, both inclusive; null = booking closed.
public class RecruitmentSettings
{
    public int Id { get; set; } = 1;
    public DateOnly? InterviewsFrom { get; set; }
    public DateOnly? InterviewsTo { get; set; }
    public bool RemindersEnabled { get; set; } // day-before e-mail to recruits (ReminderJob); off until an admin turns it on
}
