namespace RekruTime.Api.Data;

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
    public string Language { get; set; } = "pl"; // invitation e-mail + booking page
    public List<Section> Sections { get; set; } = [];
}
