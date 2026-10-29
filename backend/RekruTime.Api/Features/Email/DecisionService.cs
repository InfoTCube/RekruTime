using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RekruTime.Api.Common;
using RekruTime.Api.Data;

namespace RekruTime.Api.Features.Email;

public record SendDecisionsRequest([MinLength(1), MaxLength(2000)] int[] RecruitIds);
public record SendDecisionsResult(int Queued, int Skipped);

// Decision e-mails – only when an admin asks, never automatically on a vote. One e-mail per recruit, once all
// their applications are Accepted/Rejected/NotPreselected: "accepted" (listing the accepted sections) if any is, else "rejected".
// Sending locks the votes (VoteService), so the decision can't change after the recruit was told.
public class DecisionService(AppDbContext db, TemplateService templates, IOptions<ClubOptions> club, IOptions<EmailOptions> email)
{
    public async Task<Result<SendDecisionsResult>> SendAsync(SendDecisionsRequest req)
    {
        if (!email.Value.IsConfigured) return Error.BadRequest("E-mail isn't configured – set it up first (Admin → E-mail).");
        var ids = req.RecruitIds.Distinct().ToArray();

        await using var tx = await db.Database.BeginTransactionAsync();
        // Row locks: a vote arriving meanwhile waits, then sees DecisionSentAt and is refused.
        var apps = await db.Applications.FromSqlInterpolated($"SELECT * FROM \"Applications\" WHERE \"RecruitId\" = ANY({ids}) FOR UPDATE").ToListAsync();
        var sections = await db.Sections.ToDictionaryAsync(s => s.Id, s => s.Name);
        var recruits = await db.Recruits.Where(r => ids.Contains(r.Id)).ToListAsync();
        // The recruit's language = their latest booking link's, else the one they filled the form in (not invited at all).
        var languages = (await db.BookingLinks.Where(l => l.RecruitId != null && ids.Contains(l.RecruitId.Value))
                .Select(l => new { RecruitId = l.RecruitId!.Value, l.Language, l.Id }).ToListAsync())
            .GroupBy(l => l.RecruitId).ToDictionary(g => g.Key, g => g.MaxBy(l => l.Id)!.Language);
        foreach (var r in (await db.FormResponses.Where(x => ids.Contains(x.RecruitId)).Select(x => new { x.RecruitId, x.Language, x.SubmittedAt }).ToListAsync())
                     .GroupBy(x => x.RecruitId))
            languages.TryAdd(r.Key, r.MaxBy(x => x.SubmittedAt)!.Language);

        var queued = 0;
        foreach (var recruit in recruits)
        {
            var mine = apps.Where(a => a.RecruitId == recruit.Id).ToList();
            if (!IsReady(mine)) continue;
            var accepted = mine.Where(a => a.Status == ApplicationStatus.Accepted).ToList();
            var key = accepted.Count > 0 ? Decisions.Accepted : Decisions.Rejected;
            var language = languages.GetValueOrDefault(recruit.Id, "pl");
            var mail = Decisions.Render(await templates.GetAsync(key, language),
                new DecisionValues(recruit.FullName, (accepted.Count > 0 ? accepted : mine).Select(a => sections[a.SectionId]).Order(), club.Value.ContactEmail),
                language, club.Value.Name);
            db.EmailMessages.Add(new EmailMessage { To = recruit.Email, ToName = recruit.FullName, Subject = mail.Subject, Body = mail.Body, HtmlBody = mail.Html, RecruitId = recruit.Id });
            mine.ForEach(a => a.DecisionSentAt = DateTime.UtcNow);
            queued++;
        }
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return new SendDecisionsResult(queued, ids.Length - queued);
    }

    // Every application decided and none told yet. Not preselected (forms) is a final "no" too – those recruits
    // get the rejection without an interview.
    public static bool IsReady(IReadOnlyCollection<Application> apps) =>
        apps.Count > 0 && apps.All(a => a.Status is ApplicationStatus.Accepted or ApplicationStatus.Rejected or ApplicationStatus.NotPreselected && a.DecisionSentAt == null);
}
