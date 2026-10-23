using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using RekruTime.Api.Common;
using RekruTime.Api.Data;

namespace RekruTime.Api.Features.Applications;

public record VoteRequest(VoteDecision Decision, bool IsFinal = false, [StringLength(2000)] string? Comment = null);
public record VoteDto(string UserId, string Name, VoteDecision Decision, bool IsFinal, string? Comment, DateTime CreatedAt);
public record ApplicationVotesDto(int Id, ApplicationStatus Status, List<VoteDto> Votes);

// Voters: leaders of the application's section and admins. Status is recomputed (VoteRules) and stored on every vote.
public class VoteService(AppDbContext db)
{
    public async Task<Result<ApplicationVotesDto>> VoteAsync(int applicationId, string userId, VoteRequest req)
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        // Row lock: concurrent votes on one application recompute the status one after another.
        var app = await db.Applications.FromSqlInterpolated($"SELECT * FROM \"Applications\" WHERE \"Id\" = {applicationId} FOR UPDATE").SingleOrDefaultAsync();
        if (app is null) return Error.NotFound();

        var votes = await FactsAsync(app);
        var me = await VoterAsync(userId, app.SectionId);
        if (!me.IsLeader && !me.IsAdmin) return Error.Forbidden("Only leaders of this section and admins can vote.");
        if (votes.Any(v => v.IsFinal)) return Error.Conflict("A final decision has already been made.");
        if (app.DecisionSentAt is not null) return Error.Conflict("The decision e-mail was already sent – votes are locked.");

        var existing = await db.ApplicationVotes.SingleOrDefaultAsync(v => v.ApplicationId == app.Id && v.UserId == userId);
        if (req.IsFinal)
        {
            if (app.Status != ApplicationStatus.Disputed) return Error.Conflict("A final decision is only possible on a disputed application.");
            if (!me.IsAdmin || existing is not null) return Error.Forbidden("The final decision is made by an admin who hasn't voted yet.");
        }

        if (existing is null) db.Add(existing = new ApplicationVote { ApplicationId = app.Id, UserId = userId });
        existing.Decision = req.Decision;
        existing.IsFinal = req.IsFinal;
        existing.Comment = string.IsNullOrWhiteSpace(req.Comment) ? null : req.Comment.Trim();
        existing.CreatedAt = DateTime.UtcNow;

        votes.RemoveAll(v => v.UserId == userId);
        votes.Add(me with { Decision = req.Decision, IsFinal = req.IsFinal });
        app.Status = VoteRules.Decide(votes);
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return await GetAsync(app.Id);
    }

    public async Task<ApplicationVotesDto> GetAsync(int applicationId) =>
        new(applicationId, await db.Applications.Where(a => a.Id == applicationId).Select(a => a.Status).SingleAsync(),
            await db.ApplicationVotes.Where(v => v.ApplicationId == applicationId).OrderBy(v => v.CreatedAt)
                .Select(v => new VoteDto(v.UserId, v.User.DisplayName, v.Decision, v.IsFinal, v.Comment, v.CreatedAt)).ToListAsync());

    async Task<List<VoteFacts>> FactsAsync(Application app) =>
        await db.ApplicationVotes.Where(v => v.ApplicationId == app.Id)
            .Select(v => new VoteFacts(v.UserId,
                db.UserSections.Any(us => us.UserId == v.UserId && us.SectionId == app.SectionId && us.IsLeader),
                db.UserRoles.Any(ur => ur.UserId == v.UserId && db.Roles.Any(r => r.Id == ur.RoleId && r.Name == Roles.Admin)),
                v.Decision, v.IsFinal))
            .ToListAsync();

    async Task<VoteFacts> VoterAsync(string userId, int sectionId) => new(userId,
        await db.UserSections.AnyAsync(us => us.UserId == userId && us.SectionId == sectionId && us.IsLeader),
        await db.UserRoles.AnyAsync(ur => ur.UserId == userId && db.Roles.Any(r => r.Id == ur.RoleId && r.Name == Roles.Admin)),
        default, false);
}
