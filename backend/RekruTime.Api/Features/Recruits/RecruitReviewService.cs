using Microsoft.EntityFrameworkCore;
using RekruTime.Api.Common;
using RekruTime.Api.Data;
using RekruTime.Api.Features.Applications;
using RekruTime.Api.Features.Interviews;

namespace RekruTime.Api.Features.Recruits;

public record ApplicationReviewDto(int Id, int SectionId, string SectionName, ApplicationStatus Status, List<VoteDto> Votes, bool CanVote, bool CanDecide, DateTime? DecisionSentAt);
public record InterviewSummaryDto(int Id, DateTime Start, InterviewStatus Status, List<string> Interviewers);
public record RecruitRowDto(int Id, string FullName, string Email, List<ApplicationReviewDto> Applications, InterviewSummaryDto? Interview);
public record InterviewWithNotesDto(InterviewDto Interview, NotesDto Notes);
public record RecruitDetailDto(RecruitDto Recruit, List<ApplicationReviewDto> Applications, List<InterviewWithNotesDto> Interviews);

// After the interviews: recruits with their applications and votes. Admins see everything,
// section leaders only the applications to sections they lead (and recruits who have one).
public class RecruitReviewService(AppDbContext db, InterviewService interviews, NoteService notes)
{
    public record Viewer(string UserId, bool IsAdmin);

    public async Task<Result<List<RecruitRowDto>>> ListAsync(Viewer me, int? sectionId, ApplicationStatus? status)
    {
        var led = await LedSectionsAsync(me);
        if (led is { Count: 0 }) return Error.Forbidden("Only admins and section leaders can review recruits.");

        var apps = await ApplicationsAsync(me, led, a => (sectionId == null || a.SectionId == sectionId) && (status == null || a.Status == status));
        var ids = apps.Select(a => a.RecruitId).Distinct().ToList();
        var recruits = await db.Recruits.Where(r => ids.Contains(r.Id)).OrderBy(r => r.FullName)
            .Select(r => new
            {
                r.Id, r.FullName, r.Email,
                // The latest interview that wasn't cancelled (a recruit can rebook).
                Interview = db.Interviews.Where(i => i.RecruitId == r.Id && i.Status != InterviewStatus.Cancelled).OrderByDescending(i => i.Start)
                    .Select(i => new InterviewSummaryDto(i.Id, i.Start, i.Status, i.Interviewers.OrderBy(u => u.DisplayName).Select(u => u.DisplayName).ToList()))
                    .FirstOrDefault(),
            })
            .ToListAsync();
        return recruits.Select(r => new RecruitRowDto(r.Id, r.FullName, r.Email,
            apps.Where(a => a.RecruitId == r.Id).Select(a => a.Dto).ToList(), r.Interview)).ToList();
    }

    public async Task<Result<RecruitDetailDto>> GetAsync(Viewer me, int id)
    {
        var led = await LedSectionsAsync(me);
        if (led is { Count: 0 }) return Error.Forbidden("Only admins and section leaders can review recruits.");
        var recruit = await db.Recruits.Where(r => r.Id == id)
            .Select(r => new RecruitDto(r.Id, r.FullName, r.Email, r.Phone, r.DiscordNickname)).SingleOrDefaultAsync();
        if (recruit is null) return Error.NotFound();

        var apps = await ApplicationsAsync(me, led, a => a.RecruitId == id);
        if (!me.IsAdmin && apps.Count == 0) return Error.NotFound(); // not one of their sections

        // Every interview of the recruit the viewer may read (same rules as the interview page), with its notes.
        var withNotes = new List<InterviewWithNotesDto>();
        foreach (var interviewId in await db.Interviews.Where(i => i.RecruitId == id).OrderBy(i => i.Start).Select(i => i.Id).ToListAsync())
            if ((await interviews.GetAsync(interviewId, me.UserId, me.IsAdmin)).Value is { } interview
                && (await notes.ListAsync(interviewId, me.UserId, me.IsAdmin)).Value is { } n)
                withNotes.Add(new InterviewWithNotesDto(interview, n));

        return new RecruitDetailDto(recruit, apps.Select(a => a.Dto).ToList(), withNotes);
    }

    // null = admin (all sections)
    async Task<List<int>?> LedSectionsAsync(Viewer me) => me.IsAdmin ? null
        : await db.UserSections.Where(us => us.UserId == me.UserId && us.IsLeader).Select(us => us.SectionId).ToListAsync();

    async Task<List<(int RecruitId, ApplicationReviewDto Dto)>> ApplicationsAsync(Viewer me, List<int>? led, System.Linq.Expressions.Expression<Func<Application, bool>> filter)
    {
        var q = db.Applications.Where(filter);
        if (led is not null) q = q.Where(a => led.Contains(a.SectionId));
        var rows = await q.OrderBy(a => a.Section.Name)
            .Select(a => new
            {
                a.RecruitId, a.Id, a.SectionId, SectionName = a.Section.Name, a.Status, a.DecisionSentAt,
                Votes = db.ApplicationVotes.Where(v => v.ApplicationId == a.Id).OrderBy(v => v.CreatedAt)
                    .Select(v => new VoteDto(v.UserId, v.User.DisplayName, v.Decision, v.IsFinal, v.Comment, v.CreatedAt)).ToList(),
            })
            .ToListAsync();
        // Mirrors VoteService's checks so the UI only offers what will succeed.
        return rows.Select(a =>
        {
            var decided = a.Votes.Any(v => v.IsFinal) || a.DecisionSentAt != null; // decision e-mail sent → locked
            var canVote = !decided && !VoteService.InPreselection(a.Status) && (me.IsAdmin || led!.Contains(a.SectionId));
            var canDecide = !decided && me.IsAdmin && a.Status == ApplicationStatus.Disputed && a.Votes.All(v => v.UserId != me.UserId);
            return (a.RecruitId, new ApplicationReviewDto(a.Id, a.SectionId, a.SectionName, a.Status, a.Votes, canVote, canDecide, a.DecisionSentAt));
        }).ToList();
    }
}
