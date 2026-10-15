namespace RekruTime.Api.Data;

// All DateTime values are UTC (timestamptz).

public class Section
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public int InterviewDurationMinutes { get; set; } = 30;
    public int BufferMinutes { get; set; } // room time after an interview (notes/changeover)
}

public class UserSection
{
    public required string UserId { get; set; }
    public int SectionId { get; set; }
    public AppUser User { get; set; } = null!;
    public Section Section { get; set; } = null!;
}

public class Availability
{
    public int Id { get; set; }
    public required string UserId { get; set; }
    public DateTime Start { get; set; }
    public DateTime End { get; set; }
}

public class BookingLink
{
    public int Id { get; set; }
    public required string Token { get; set; }
    public int? RecruitId { get; set; }
    public Recruit? Recruit { get; set; }
    public string? Label { get; set; }
    public string? CreatedById { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ExpiresAt { get; set; }
    public bool IsActive { get; set; } = true;
    public List<Section> Sections { get; set; } = [];
}

public class Recruit
{
    public int Id { get; set; }
    public required string FullName { get; set; }
    public required string Email { get; set; } // stored lower-case
    public string? Phone { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public List<Application> Applications { get; set; } = [];
}

public enum ApplicationStatus { Pending }

public class Application
{
    public int Id { get; set; }
    public int RecruitId { get; set; }
    public int SectionId { get; set; }
    public Section Section { get; set; } = null!;
    public ApplicationStatus Status { get; set; } = ApplicationStatus.Pending;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

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
    public List<Section> Sections { get; set; } = [];
    public List<AppUser> Interviewers { get; set; } = [];
}
