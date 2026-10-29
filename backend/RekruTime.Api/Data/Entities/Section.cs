namespace RekruTime.Api.Data;

public class Section
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public int InterviewDurationMinutes { get; set; } = 30;
    public int BufferMinutes { get; set; } // room time after an interview (notes/changeover)
    public List<string> SuggestedQuestions { get; set; } = []; // shown to interviewers of this section (#93)
}
