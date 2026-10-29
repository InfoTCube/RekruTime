using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RekruTime.Api.Common;
using RekruTime.Api.Data;

namespace RekruTime.Api.Features.Booking;

public record SlotData(SlotQuery Query, Dictionary<string, string> Names);
public record SectionCapacityDto(int SectionId, string Name, int Green, int WithYellow); // interviews that still fit

// Loads everything SlotCalculator needs for a set of sections. Shared by the admin preview and public booking.
public class SlotService(AppDbContext db, IOptions<ClubOptions> club)
{
    // Overview dashboard: remaining capacity of each section for the rest of the recruitment period.
    public async Task<List<SectionCapacityDto>> CapacityAsync()
    {
        var result = new List<SectionCapacityDto>();
        foreach (var section in await db.Sections.OrderBy(s => s.Name).ToListAsync())
        {
            var data = await LoadAsync([section.Id], DateTime.UtcNow, DateTime.MaxValue);
            var all = data is null ? [] : SlotCalculator.Calculate(data.Query).ToList();
            var block = TimeSpan.FromMinutes(section.InterviewDurationMinutes + section.BufferMinutes);
            result.Add(new(section.Id, section.Name,
                SlotCalculator.Capacity(all.Where(s => s.Color == SlotColor.Green), block), SlotCalculator.Capacity(all, block)));
        }
        return result;
    }

    /// <returns>null when booking is closed (no recruitment period) or the range is outside it.</returns>
    public async Task<SlotData?> LoadAsync(IReadOnlyCollection<int> sectionIds, DateTime from, DateTime to)
    {
        // Clamp to the recruitment period (club dates → UTC) and minimum notice.
        var period = await db.RecruitmentSettings.FindAsync(1);
        if (period is not { InterviewsFrom: { } pFrom, InterviewsTo: { } pTo }) return null;
        var zone = club.Value.Zone;
        from = new[] { from, TimeZoneInfo.ConvertTimeToUtc(pFrom.ToDateTime(TimeOnly.MinValue), zone), DateTime.UtcNow.AddHours(period.MinNoticeHours) }.Max();
        to = new[] { to, TimeZoneInfo.ConvertTimeToUtc(pTo.AddDays(1).ToDateTime(TimeOnly.MinValue), zone) }.Min();
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
        // Each person's scheduled interviews around the range – suggested slots and the on-site team preference (#110).
        var (dayBefore, dayAfter) = (from.AddDays(-1), to.AddDays(1));
        var interviewsOf = (await db.Interviews
                .Where(i => i.Status == InterviewStatus.Scheduled && i.Start < dayAfter && i.BlockedUntil > dayBefore)
                .SelectMany(i => i.Interviewers.Where(u => ids.Contains(u.Id)).Select(u => new { UserId = u.Id, i.Start, i.BlockedUntil }))
                .ToListAsync())
            .ToLookup(x => x.UserId, x => new Interval(x.Start, x.BlockedUntil));
        var busy = await db.Interviews
            // Same rule as the DB constraint EX_Interviews_NoOverlap: only cancelled interviews free the room.
            .Where(i => i.Status != InterviewStatus.Cancelled && i.Start < to && i.BlockedUntil > from)
            .Select(i => new Interval(i.Start, i.BlockedUntil))
            .ToListAsync();

        var query = new SlotQuery(
            sectionIds.ToHashSet(),
            TimeSpan.FromMinutes(sections.Max(s => s.InterviewDurationMinutes)),
            TimeSpan.FromMinutes(sections.Max(s => s.BufferMinutes)),
            from, to,
            people.Select(p => new Interviewer(p.Id, p.IsAdmin, p.Sections.ToHashSet(), availability[p.Id].ToList(), p.Load, interviewsOf[p.Id].ToList())).ToList(),
            busy, zone);
        return new SlotData(query, people.ToDictionary(p => p.Id, p => p.DisplayName));
    }
}
