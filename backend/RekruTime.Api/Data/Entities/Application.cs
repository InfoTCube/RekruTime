namespace RekruTime.Api.Data;

public enum ApplicationStatus { Pending, Disputed, Accepted, Rejected }

public class Application
{
    public int Id { get; set; }
    public int RecruitId { get; set; }
    public int SectionId { get; set; }
    public Section Section { get; set; } = null!;
    public ApplicationStatus Status { get; set; } = ApplicationStatus.Pending;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
