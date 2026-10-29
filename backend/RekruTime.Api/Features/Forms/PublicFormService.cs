using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RekruTime.Api.Common;
using RekruTime.Api.Data;

namespace RekruTime.Api.Features.Forms;

public record PublicSectionDto(int Id, string Name);
public record PublicFormDto(string? ClubName, string? ContactEmail, string Title, string? Description, List<FieldDto> Fields, List<PublicSectionDto> Sections);

// Answers keyed by field id, like the stored FormAnswer.
public record SubmitRequest(
    [Required, StringLength(200, MinimumLength = 2)] string FullName,
    [Required, EmailAddress, StringLength(256)] string Email,
    bool GdprConsent,
    string? Language,
    Dictionary<string, List<string>>? Answers);

// The recruitment form recruits fill in – no login, open forms only.
public class PublicFormService(AppDbContext db, IOptions<ClubOptions> club)
{
    public async Task<Result<PublicFormDto>> GetAsync(int id)
    {
        var f = await db.Forms.FindAsync(id);
        if (f is null) return Error.NotFound();
        if (!f.IsOpen) return Closed;
        var sections = await db.Sections.OrderBy(s => s.Name).Select(s => new PublicSectionDto(s.Id, s.Name)).ToListAsync();
        return new PublicFormDto(club.Value.Name, club.Value.ContactEmail, f.Title, f.Description,
            // No FollowUp: those are interview questions – recruits must not see them.
            f.Fields.Select(x => new FieldDto(x.Id, x.Type, x.Label, x.Required, x.Options)).ToList(), sections);
    }

    public async Task<Result> SubmitAsync(int id, SubmitRequest req)
    {
        if (!req.GdprConsent) return Error.Validation("GdprConsent", "Consent is required to apply.");
        try
        {
            return await TrySubmitAsync(id, req);
        }
        // Double click / two tabs: both create the same new recruit (or response) and one loses on a unique index.
        // Retry once – it now finds them and replaces the answers.
        catch (Exception e) when (DbErrors.IsUniqueViolation(e))
        {
            db.ChangeTracker.Clear();
            return await TrySubmitAsync(id, req);
        }
    }

    async Task<Result> TrySubmitAsync(int id, SubmitRequest req)
    {
        var form = await db.Forms.FindAsync(id);
        if (form is null) return Error.NotFound();
        if (!form.IsOpen) return Closed;
        var sectionIds = await db.Sections.Select(s => s.Id).ToListAsync();
        var answers = Answers(form.Fields, req.Answers ?? [], sectionIds);
        if (answers.Error is { } invalid) return invalid;

        var email = req.Email.Trim().ToLowerInvariant();
        var recruit = await db.Recruits.Include(r => r.Applications).FirstOrDefaultAsync(r => r.Email == email);
        if (recruit is null) db.Recruits.Add(recruit = new Recruit { FullName = "", Email = email });
        recruit.FullName = req.FullName.Trim();
        recruit.ConsentAt = DateTime.UtcNow;
        if (form.Fields.FirstOrDefault(f => f.Type == FieldType.Phone) is { } phone && Value(answers.Value!, phone.Id) is { } number)
            recruit.Phone = number;

        // A section un-ticked on a new submission drops its application – but only one nobody has acted on yet.
        var chosen = form.Fields.FirstOrDefault(f => f.Type == FieldType.Sections) is { } sf
            ? answers.Value!.FirstOrDefault(a => a.FieldId == sf.Id)?.Values.Select(int.Parse).ToList() ?? []
            : [];
        recruit.Applications.RemoveAll(a => a.Status == ApplicationStatus.Submitted && !chosen.Contains(a.SectionId));
        foreach (var s in chosen.Where(s => recruit.Applications.All(a => a.SectionId != s)))
            recruit.Applications.Add(new Application { SectionId = s, Status = ApplicationStatus.Submitted });

        var response = recruit.Id == 0 ? null : await db.FormResponses.FirstOrDefaultAsync(r => r.FormId == id && r.RecruitId == recruit.Id);
        if (response is null) db.FormResponses.Add(response = new FormResponse { FormId = id, Recruit = recruit });
        response.Answers = answers.Value!;
        response.Language = req.Language?.Trim().ToLowerInvariant() is { Length: > 0 and <= 5 } l ? l : "pl";
        response.SubmittedAt = DateTime.UtcNow;

        await db.SaveChangesAsync();
        return Result.Ok;
    }

    static readonly Error Closed = Error.Gone("This form is closed.");

    // Every answer checked against its question. Unknown field ids are ignored; empty answers are dropped.
    static Result<List<FormAnswer>> Answers(List<FormField> fields, Dictionary<string, List<string>> input, List<int> sectionIds)
    {
        var result = new List<FormAnswer>();
        foreach (var f in fields)
        {
            var values = (input.GetValueOrDefault(f.Id) ?? []).Select(v => v?.Trim() ?? "").Where(v => v.Length > 0).ToList();
            if (values.Count == 0)
            {
                if (f.Required) return Error.Validation(f.Id, "This question is required.");
                continue;
            }
            var ok = f.Type switch
            {
                FieldType.Text => values is [{ Length: <= 500 }],
                FieldType.Textarea => values is [{ Length: <= 5000 }],
                FieldType.Phone => values is [{ Length: <= 30 }],
                FieldType.Email => values is [{ Length: <= 256 } e] && new EmailAddressAttribute().IsValid(e),
                FieldType.SingleChoice => values is [var o] && f.Options.Contains(o),
                FieldType.MultiChoice => values.All(f.Options.Contains) && values.Distinct().Count() == values.Count,
                FieldType.Sections => values.All(v => int.TryParse(v, out var s) && sectionIds.Contains(s)) && values.Distinct().Count() == values.Count,
                _ => false,
            };
            if (!ok) return Error.Validation(f.Id, $"Invalid answer to \"{f.Label}\".");
            result.Add(new FormAnswer { FieldId = f.Id, Values = values });
        }
        return result;
    }

    static string? Value(List<FormAnswer> answers, string fieldId) => answers.FirstOrDefault(a => a.FieldId == fieldId)?.Values[0];
}
