using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using RekruTime.Api.Common;
using RekruTime.Api.Data;
using RekruTime.Api.Features.Booking;
using RekruTime.Api.Features.Email;

namespace RekruTime.Api.Features.Forms;

// PendingReview: sections the recruit applied to that nobody has preselected or turned down yet.
public record InviteCandidateDto(int RecruitId, string FullName, string Email, List<string> Sections, List<string> PendingReview, bool AlreadyInvited);
public record InviteRequest([MinLength(1), MaxLength(2000)] int[] RecruitIds, DateTimeOffset? ExpiresAt, bool SendEmails);
public record InviteResult(int Created, int EmailsQueued, int Skipped);

// After preselection: one personal booking link per recruit for all their preselected sections (one interview
// covers them all), the applications move to InterviewInvited, optionally with the invitation e-mail.
public class InterviewInviteService(AppDbContext db, InvitationService invitations)
{
    public async Task<List<InviteCandidateDto>> CandidatesAsync()
    {
        var apps = await db.Applications.Where(a => a.Status == ApplicationStatus.Preselected || a.Status == ApplicationStatus.Submitted)
            .Select(a => new { a.RecruitId, a.Status, a.Section.Name }).ToListAsync();
        var ids = apps.Where(a => a.Status == ApplicationStatus.Preselected).Select(a => a.RecruitId).Distinct().ToList();
        var invited = await InvitedAsync(ids);
        return (await db.Recruits.Where(r => ids.Contains(r.Id)).OrderBy(r => r.FullName).ToListAsync())
            .Select(r => new InviteCandidateDto(r.Id, r.FullName, r.Email,
                Names(apps.Where(a => a.RecruitId == r.Id && a.Status == ApplicationStatus.Preselected).Select(a => a.Name)),
                Names(apps.Where(a => a.RecruitId == r.Id && a.Status == ApplicationStatus.Submitted).Select(a => a.Name)),
                invited.Contains(r.Id)))
            .ToList();
    }

    public async Task<Result<InviteResult>> InviteAsync(InviteRequest req, string userId)
    {
        if (req.SendEmails && invitations.CannotSend() is { } why) return why;
        var ids = req.RecruitIds.Distinct().ToList();

        await using var tx = await db.Database.BeginTransactionAsync();
        var recruits = await db.Recruits.Include(r => r.Applications).ThenInclude(a => a.Section)
            .Where(r => ids.Contains(r.Id)).ToListAsync();
        var invited = await InvitedAsync(ids);
        // The language they filled the form in – the invitation (and later e-mails) use it.
        var languages = (await db.FormResponses.Where(x => ids.Contains(x.RecruitId)).Select(x => new { x.RecruitId, x.Language, x.SubmittedAt }).ToListAsync())
            .GroupBy(x => x.RecruitId).ToDictionary(g => g.Key, g => g.MaxBy(x => x.SubmittedAt)!.Language);

        var links = new List<BookingLink>();
        foreach (var recruit in recruits.Where(r => !invited.Contains(r.Id)))
        {
            var preselected = recruit.Applications.Where(a => a.Status == ApplicationStatus.Preselected).ToList();
            if (preselected.Count == 0) continue;
            preselected.ForEach(a => a.Status = ApplicationStatus.InterviewInvited);
            var link = new BookingLink
            {
                Token = BookingLinkService.NewToken(),
                Recruit = recruit,
                Label = recruit.FullName,
                Language = languages.GetValueOrDefault(recruit.Id, "pl"),
                CreatedById = userId,
                ExpiresAt = req.ExpiresAt?.UtcDateTime,
                Sections = preselected.Select(a => a.Section).ToList(),
            };
            db.BookingLinks.Add(link);
            links.Add(link);
        }
        await db.SaveChangesAsync(); // links need ids before their e-mails reference them
        var queued = req.SendEmails ? await invitations.QueueAsync(links) : 0;
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return new InviteResult(links.Count, queued, ids.Count - links.Count);
    }

    // Same rule as the import: a link that still works or an interview that isn't cancelled = already invited.
    async Task<HashSet<int>> InvitedAsync(List<int> recruitIds) =>
        (await db.BookingLinks
            .Where(l => l.RecruitId != null && recruitIds.Contains(l.RecruitId.Value) && l.IsActive && (l.ExpiresAt == null || l.ExpiresAt > DateTime.UtcNow))
            .Select(l => l.RecruitId!.Value)
            .Union(db.Interviews.Where(i => i.Status != InterviewStatus.Cancelled && recruitIds.Contains(i.RecruitId)).Select(i => i.RecruitId))
            .ToListAsync()).ToHashSet();

    static List<string> Names(IEnumerable<string> names) => names.Order().ToList();
}
