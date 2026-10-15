namespace RekruTime.Api.Data;

public class Availability
{
    public int Id { get; set; }
    public required string UserId { get; set; }
    public DateTime Start { get; set; }
    public DateTime End { get; set; }
}
