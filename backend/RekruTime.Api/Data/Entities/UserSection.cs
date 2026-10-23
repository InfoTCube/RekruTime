namespace RekruTime.Api.Data;

public class UserSection
{
    public required string UserId { get; set; }
    public int SectionId { get; set; }
    public bool IsLeader { get; set; } // votes on applications to this section (phase 2)
    public AppUser User { get; set; } = null!;
    public Section Section { get; set; } = null!;
}
