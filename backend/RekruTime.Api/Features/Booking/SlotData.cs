using Microsoft.EntityFrameworkCore;
using RekruTime.Api.Data;

namespace RekruTime.Api.Features.Booking;

// Loads everything SlotCalculator needs for a set of sections. Shared by the admin preview and public booking.
public static class SlotData
{
    // ponytail: constant; make it a setting if the board wants to change it.
    public static readonly TimeSpan MinNotice = TimeSpan.FromHours(12);

    /// <returns>null when booking is closed (no recruitment period) or the range is outside it.</returns>
    public static async Task<(SlotQuery Query, Dictionary<string, string> Names)?> LoadAsync(
        AppDbContext db, TimeZoneInfo club, IReadOnlyCollection<int> sectionIds, DateTime from, DateTime to)
    {
        // Clamp to the recruitment period (club dates → UTC) and minimum notice.
        var period = await db.RecruitmentSettings.FindAsync(1);
        if (period is not { InterviewsFrom: { } pFrom, InterviewsTo: { } pTo }) return null;
        from = Max(from, TimeZoneInfo.ConvertTimeToUtc(pFrom.ToDateTime(TimeOnly.MinValue), club), DateTime.UtcNow + MinNotice);
        to = Min(to, TimeZoneInfo.ConvertTimeToUtc(pTo.AddDays(1).ToDateTime(TimeOnly.MinValue), club));
        if (from >= to) return null;

        var sections = await db.Sections.Where(s => sectionIds.Contains(s.Id)).ToListAsync();
        var adminRoleId = await db.Roles.Where(r => r.Name == Roles.Admin).Select(r => r.Id).SingleAsync();

        var people = await db.Users
            .Where(u => db.UserRoles.Any(ur => ur.UserId == u.Id && ur.RoleId == adminRoleId) || u.Sections.Any(s => sectionIds.Contains(s.SectionId)))
            .Select(u => new
            {
                u.Id,
                u.DisplayName,
                IsAdmin = db.UserRoles.Any(ur => ur.UserId == u.Id && ur.RoleId == adminRoleId),
                Sections = u.Sections.Select(s => s.SectionId).ToList(),
                Load = db.Interviews.Count(i => i.Status == InterviewStatus.Scheduled && i.Interviewers.Any(x => x.Id == u.Id)),
            })
            .ToListAsync();
        var ids = people.Select(p => p.Id).ToList();
        var availability = (await db.Availabilities
                .Where(a => ids.Contains(a.UserId) && a.Start < to && a.End > from)
                .Select(a => new { a.UserId, a.Start, a.End })
                .ToListAsync())
            .ToLookup(a => a.UserId, a => new Interval(a.Start, a.End));
        var busy = await db.Interviews
            .Where(i => i.Status == InterviewStatus.Scheduled && i.Start < to && i.BlockedUntil > from)
            .Select(i => new Interval(i.Start, i.BlockedUntil))
            .ToListAsync();

        var query = new SlotQuery(
            sectionIds.ToHashSet(),
            TimeSpan.FromMinutes(sections.Max(s => s.InterviewDurationMinutes)),
            TimeSpan.FromMinutes(sections.Max(s => s.BufferMinutes)),
            from, to,
            people.Select(p => new Interviewer(p.Id, p.IsAdmin, p.Sections.ToHashSet(), availability[p.Id].ToList(), p.Load)).ToList(),
            busy);
        return (query, people.ToDictionary(p => p.Id, p => p.DisplayName));
    }

    static DateTime Max(params DateTime[] d) => d.Max();
    static DateTime Min(params DateTime[] d) => d.Min();
}
