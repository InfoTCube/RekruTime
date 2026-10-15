namespace RekruTime.Api.Data;

public class Recruit
{
    public int Id { get; set; }
    public required string FullName { get; set; }
    public required string Email { get; set; } // stored lower-case
    public string? Phone { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public List<Application> Applications { get; set; } = [];
}
