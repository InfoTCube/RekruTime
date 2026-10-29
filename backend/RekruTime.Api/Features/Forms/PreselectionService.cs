using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using RekruTime.Api.Common;
using RekruTime.Api.Data;

namespace RekruTime.Api.Features.Forms;

public record AnswerDto(string FieldId, List<string> Values);
public record ApplicantDto(int ApplicationId, int RecruitId, string FullName, string Email, ApplicationStatus Status, int? FormId, DateTime? SubmittedAt, List<AnswerDto> Answers);
public record PreselectionFormDto(int Id, string Title, List<FieldDto> Fields);
public record PreselectionDto(List<PreselectionFormDto> Forms, List<ApplicantDto> Applicants);
public record PreselectRequest([MinLength(1), MaxLength(2000)] int[] ApplicationIds, ApplicationStatus Status);
public record PreselectResult(int Updated, int Skipped);

// Before the interviews: leaders (their sections) and admins read what applicants answered and mark them
// Preselected / NotPreselected (or back to Submitted). Invitations (#31) take it from there.
public class PreselectionService(AppDbContext db)
{
    public static readonly ApplicationStatus[] Statuses =
        [ApplicationStatus.Submitted, ApplicationStatus.Preselected, ApplicationStatus.NotPreselected, ApplicationStatus.InterviewInvited];

    // Statuses a reviewer can set here – an invited applicant has moved on.
    static readonly ApplicationStatus[] Settable = [ApplicationStatus.Submitted, ApplicationStatus.Preselected, ApplicationStatus.NotPreselected];

    public record Viewer(string UserId, bool IsAdmin);

    public async Task<Result<PreselectionDto>> ListAsync(Viewer me, int? sectionId)
    {
        var led = await LedSectionsAsync(me);
        if (led is { Count: 0 }) return NotReviewer;
        if (sectionId is not { } section) return Error.Validation("sectionId", "Choose a section.");
        if (led is not null && !led.Contains(section)) return Error.Forbidden("You don't lead this section.");

        var applicants = await db.Applications
            .Where(a => a.SectionId == section && Statuses.Contains(a.Status))
            .Join(db.Recruits, a => a.RecruitId, r => r.Id, (a, r) => new
            {
                a.Id, a.Status, RecruitId = r.Id, r.FullName, r.Email,
                // A recruit who filled in more than one form: the latest response.
                Response = db.FormResponses.Where(x => x.RecruitId == r.Id).OrderByDescending(x => x.SubmittedAt).FirstOrDefault(),
            })
            .OrderBy(x => x.FullName)
            .ToListAsync();

        var formIds = applicants.Where(a => a.Response != null).Select(a => a.Response!.FormId).Distinct().ToList();
        var forms = await db.Forms.Where(f => formIds.Contains(f.Id)).ToListAsync();
        return new PreselectionDto(
            forms.Select(f => new PreselectionFormDto(f.Id, f.Title, f.Fields.Select(x => new FieldDto(x.Id, x.Type, x.Label, x.Required, x.Options)).ToList())).ToList(),
            applicants.Select(a => new ApplicantDto(a.Id, a.RecruitId, a.FullName, a.Email, a.Status, a.Response?.FormId, a.Response?.SubmittedAt,
                a.Response?.Answers.Select(x => new AnswerDto(x.FieldId, x.Values)).ToList() ?? [])).ToList());
    }

    public async Task<Result<PreselectResult>> SetAsync(Viewer me, PreselectRequest req)
    {
        var led = await LedSectionsAsync(me);
        if (led is { Count: 0 }) return NotReviewer;
        if (!Settable.Contains(req.Status)) return Error.Validation("Status", "Status must be Submitted, Preselected or NotPreselected.");

        var ids = req.ApplicationIds.Distinct().ToList();
        var apps = await db.Applications.Where(a => ids.Contains(a.Id)).ToListAsync();
        if (apps.Count != ids.Count) return Error.NotFound("Some applications don't exist.");
        if (led is not null && apps.Any(a => !led.Contains(a.SectionId))) return Error.Forbidden("You can only preselect in the sections you lead.");

        // Applications not in preselection (imported, invited, voted on) or already told the decision are left alone.
        var changing = apps.Where(a => Settable.Contains(a.Status) && a.DecisionSentAt == null).ToList();
        foreach (var a in changing) a.Status = req.Status;
        await db.SaveChangesAsync();
        return new PreselectResult(changing.Count, apps.Count - changing.Count);
    }

    static readonly Error NotReviewer = Error.Forbidden("Only admins and section leaders can preselect.");

    // null = admin (all sections)
    async Task<List<int>?> LedSectionsAsync(Viewer me) => me.IsAdmin ? null
        : await db.UserSections.Where(us => us.UserId == me.UserId && us.IsLeader).Select(us => us.SectionId).ToListAsync();
}
