using Microsoft.EntityFrameworkCore;
using RekruTime.Api.Common;
using RekruTime.Api.Data;

namespace RekruTime.Api.Features.Interviews;

public record PrepAnswerDto(string Question, List<string> Values, string? FollowUp);
public record PrepFormDto(int FormId, string Title, DateTime SubmittedAt, List<PrepAnswerDto> Answers);
public record PrepSectionDto(int Id, string Name, List<string> Questions);
public record PrepDto(List<PrepFormDto> Forms, List<PrepSectionDto> Sections);

// Interview prep: everything the recruit answered in our forms (with each question's follow-up) and the
// suggested questions of the sections being interviewed for. Same readers as the notes.
public class PrepService(AppDbContext db, InterviewService interviews)
{
    public async Task<Result<PrepDto>> ForInterviewAsync(int id, string userId, bool isAdmin)
    {
        var access = await interviews.AccessAsync(id, userId);
        if (access is null) return Error.NotFound();
        if (!access.CanRead(isAdmin)) return Error.Forbidden("Only the interviewers, section leaders and admins can see this interview.");
        var interview = await db.Interviews.Where(i => i.Id == id).Select(i => new { i.RecruitId, Sections = i.Sections.Select(s => s.Id).ToList() }).SingleAsync();
        return await BuildAsync(interview.RecruitId, interview.Sections);
    }

    public async Task<PrepDto> BuildAsync(int recruitId, IReadOnlyCollection<int> sectionIds)
    {
        var responses = await db.FormResponses.Where(r => r.RecruitId == recruitId).OrderBy(r => r.SubmittedAt)
            .Join(db.Forms, r => r.FormId, f => f.Id, (r, f) => new { Response = r, Form = f }).ToListAsync();
        var sectionNames = await db.Sections.ToDictionaryAsync(s => s.Id.ToString(), s => s.Name);

        // In the form's question order; answers to questions removed from the form since are left out.
        var forms = responses.Select(x => new PrepFormDto(x.Form.Id, x.Form.Title, x.Response.SubmittedAt,
            x.Form.Fields
                .Select(f => (Field: f, Answer: x.Response.Answers.FirstOrDefault(a => a.FieldId == f.Id)))
                .Where(p => p.Answer is not null)
                .Select(p => new PrepAnswerDto(p.Field.Label,
                    p.Field.Type == FieldType.Sections ? p.Answer!.Values.Select(v => sectionNames.GetValueOrDefault(v, v)).ToList() : p.Answer!.Values,
                    p.Field.FollowUp))
                .ToList()))
            .ToList();

        var sections = await db.Sections.Where(s => sectionIds.Contains(s.Id)).OrderBy(s => s.Name)
            .Select(s => new PrepSectionDto(s.Id, s.Name, s.SuggestedQuestions)).ToListAsync();
        return new PrepDto(forms, sections);
    }
}
