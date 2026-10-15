using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using RekruTime.Api.Data;
using RekruTime.Api.Features.Auth;

namespace RekruTime.Api.Features.Interviews;

public record PersonRef(string Id, string Name);
public record RecruitDto(int Id, string FullName, string Email, string? Phone, string? DiscordNickname);
public record InterviewDto(int Id, DateTime Start, DateTime End, DateTime BlockedUntil, InterviewStatus Status,
    RecruitDto Recruit, List<SectionRef> Sections, List<PersonRef> Interviewers);

// All fields optional: cancel / mark done, swap interviewers, move to another time.
public record UpdateInterviewRequest(InterviewStatus? Status, string[]? InterviewerIds, DateTimeOffset? Start);

public static class InterviewEndpoints
{
    public static void MapInterviewEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/interviews").WithTags("Interviews").RequireAuthorization();

        // Recruiters see the interviews they conduct; admins see all.
        group.MapGet("/", async (DateTimeOffset? from, DateTimeOffset? to, int? sectionId, InterviewStatus? status, ClaimsPrincipal user, AppDbContext db) =>
        {
            var q = db.Interviews.AsQueryable();
            if (!user.IsInRole(Roles.Admin))
            {
                var me = user.FindFirstValue(ClaimTypes.NameIdentifier);
                q = q.Where(i => i.Interviewers.Any(u => u.Id == me));
            }
            if (from is { } f) q = q.Where(i => i.End > f.UtcDateTime);
            if (to is { } t) q = q.Where(i => i.Start < t.UtcDateTime);
            if (sectionId is { } sid) q = q.Where(i => i.Sections.Any(s => s.Id == sid));
            if (status is { } st) q = q.Where(i => i.Status == st);
            return await Project(q.OrderBy(i => i.Start)).ToListAsync();
        });

        group.MapPatch("/{id:int}", async (int id, UpdateInterviewRequest req, AppDbContext db) =>
        {
            var interview = await db.Interviews.Include(i => i.Interviewers).FirstOrDefaultAsync(i => i.Id == id);
            if (interview is null) return Results.NotFound();

            if (req.InterviewerIds is { } ids)
            {
                var users = await db.Users.Where(u => ids.Contains(u.Id)).ToListAsync();
                if (users.Count == 0 || users.Count != ids.Distinct().Count())
                    return Results.ValidationProblem(new Dictionary<string, string[]> { ["InterviewerIds"] = ["Pick at least one existing user."] });
                interview.Interviewers = users;
            }
            if (req.Start is { } start)
            {
                // Admin override: keeps length and buffer, doesn't check availability – only the room (DB constraint).
                var (length, blocked) = (interview.End - interview.Start, interview.BlockedUntil - interview.Start);
                interview.Start = start.UtcDateTime;
                (interview.End, interview.BlockedUntil) = (interview.Start + length, interview.Start + blocked);
            }
            if (req.Status is { } status) interview.Status = status;

            try
            {
                await db.SaveChangesAsync();
            }
            catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: PostgresErrorCodes.ExclusionViolation })
            {
                return Results.Problem("The room is taken at that time by another interview.", statusCode: 409);
            }
            catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                return Results.Problem("This recruit's link already has another active interview.", statusCode: 409);
            }
            return Results.Ok(await Project(db.Interviews.Where(i => i.Id == id)).SingleAsync());
        }).RequireAuthorization(Roles.Admin);
    }

    static IQueryable<InterviewDto> Project(IQueryable<Interview> q) => q.Select(i => new InterviewDto(
        i.Id, i.Start, i.End, i.BlockedUntil, i.Status,
        new RecruitDto(i.Recruit.Id, i.Recruit.FullName, i.Recruit.Email, i.Recruit.Phone, i.Recruit.DiscordNickname),
        i.Sections.OrderBy(s => s.Name).Select(s => new SectionRef(s.Id, s.Name)).ToList(),
        i.Interviewers.OrderBy(u => u.DisplayName).Select(u => new PersonRef(u.Id, u.DisplayName)).ToList()));
}
