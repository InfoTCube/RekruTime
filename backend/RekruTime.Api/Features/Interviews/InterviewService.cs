using Microsoft.EntityFrameworkCore;
using RekruTime.Api.Common;
using RekruTime.Api.Data;
using RekruTime.Api.Features.Auth;

namespace RekruTime.Api.Features.Interviews;

public record PersonRef(string Id, string Name);
public record RecruitDto(int Id, string FullName, string Email, string? Phone, string? DiscordNickname);
public record InterviewDto(int Id, DateTime Start, DateTime End, DateTime BlockedUntil, InterviewStatus Status,
    RecruitDto Recruit, List<SectionRef> Sections, List<PersonRef> Interviewers);

public record InterviewFilter(DateTimeOffset? From, DateTimeOffset? To, int? SectionId, InterviewStatus? Status);

// What a user may do with one interview: interviewers write notes; they, leaders of its sections and admins read it.
public record InterviewAccess(bool IsInterviewer, bool IsLeader)
{
    public bool CanRead(bool isAdmin) => IsInterviewer || IsLeader || isAdmin;
}

// All fields optional: cancel / mark done, swap interviewers, move to another time.
public record UpdateInterviewRequest(InterviewStatus? Status, string[]? InterviewerIds, DateTimeOffset? Start);

public class InterviewService(AppDbContext db)
{
    // Recruiters see the interviews they conduct (onlyFor = their id); admins see all (onlyFor = null).
    public Task<List<InterviewDto>> ListAsync(InterviewFilter f, string? onlyFor)
    {
        var q = db.Interviews.AsQueryable();
        if (onlyFor is not null) q = q.Where(i => i.Interviewers.Any(u => u.Id == onlyFor));
        if (f.From is { } from) q = q.Where(i => i.End > from.UtcDateTime);
        if (f.To is { } to) q = q.Where(i => i.Start < to.UtcDateTime);
        if (f.SectionId is { } sid) q = q.Where(i => i.Sections.Any(s => s.Id == sid));
        if (f.Status is { } st) q = q.Where(i => i.Status == st);
        return Project(q.OrderBy(i => i.Start)).ToListAsync();
    }

    public async Task<Result<InterviewDto>> GetAsync(int id, string userId, bool isAdmin)
    {
        var access = await AccessAsync(id, userId);
        if (access is null) return Error.NotFound();
        if (!access.CanRead(isAdmin)) return Error.Forbidden("Only the interviewers, section leaders and admins can see this interview.");
        return await Project(db.Interviews.Where(i => i.Id == id)).SingleAsync();
    }

    // null = no such interview
    public Task<InterviewAccess?> AccessAsync(int id, string userId) =>
        db.Interviews.Where(i => i.Id == id).Select(i => new InterviewAccess(
            i.Interviewers.Any(u => u.Id == userId),
            db.UserSections.Any(us => us.UserId == userId && us.IsLeader && i.Sections.Any(s => s.Id == us.SectionId))))
        .SingleOrDefaultAsync();

    public async Task<Result<InterviewDto>> UpdateAsync(int id, UpdateInterviewRequest req)
    {
        var interview = await db.Interviews.Include(i => i.Interviewers).FirstOrDefaultAsync(i => i.Id == id);
        if (interview is null) return Error.NotFound();

        if (req.InterviewerIds is { } ids)
        {
            var users = await db.Users.Where(u => ids.Contains(u.Id)).ToListAsync();
            if (users.Count == 0 || users.Count != ids.Distinct().Count()) return Error.Validation("InterviewerIds", "Pick at least one existing user.");
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
        catch (Exception e) when (DbErrors.IsRoomTaken(e))
        {
            return Error.Conflict("The room is taken at that time by another interview.");
        }
        catch (Exception e) when (DbErrors.IsUniqueViolation(e))
        {
            return Error.Conflict("This recruit's link already has another active interview.");
        }
        return await Project(db.Interviews.Where(i => i.Id == id)).SingleAsync();
    }

    static IQueryable<InterviewDto> Project(IQueryable<Interview> q) => q.Select(i => new InterviewDto(
        i.Id, i.Start, i.End, i.BlockedUntil, i.Status,
        new RecruitDto(i.Recruit.Id, i.Recruit.FullName, i.Recruit.Email, i.Recruit.Phone, i.Recruit.DiscordNickname),
        i.Sections.OrderBy(s => s.Name).Select(s => new SectionRef(s.Id, s.Name)).ToList(),
        i.Interviewers.OrderBy(u => u.DisplayName).Select(u => new PersonRef(u.Id, u.DisplayName)).ToList()));
}
