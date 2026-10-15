using Microsoft.EntityFrameworkCore;
using RekruTime.Api.Common;
using RekruTime.Api.Data;

namespace RekruTime.Api.Features.Availability;

// DateTimeOffset in requests so any offset the client sends is converted correctly; stored as UTC.
public record RangeRequest(DateTimeOffset Start, DateTimeOffset End);
public record AvailabilityDto(int Id, DateTime Start, DateTime End);

// Each user (recruiter or admin) manages only their own availability.
public class AvailabilityService(AppDbContext db)
{
    static readonly TimeSpan MaxQueryRange = TimeSpan.FromDays(62);
    static readonly TimeSpan MaxBlock = TimeSpan.FromDays(1);

    public async Task<Result<List<AvailabilityDto>>> ListAsync(string userId, DateTimeOffset from, DateTimeOffset to)
    {
        if (to <= from || to - from > MaxQueryRange) return RangeError("Range must be positive and at most 62 days.");
        var (f, t) = (from.UtcDateTime, to.UtcDateTime);
        return await db.Availabilities
            .Where(a => a.UserId == userId && a.Start < t && a.End > f)
            .OrderBy(a => a.Start)
            .Select(a => new AvailabilityDto(a.Id, a.Start, a.End))
            .ToListAsync();
    }

    // Add a range; overlapping or adjacent blocks are merged into one.
    public async Task<Result<AvailabilityDto>> AddAsync(string userId, RangeRequest req)
    {
        if (Invalid(req) is { } invalid) return invalid;
        var (start, end) = (req.Start.UtcDateTime, req.End.UtcDateTime);

        await using var tx = await db.Database.BeginTransactionAsync();
        var touching = await db.Availabilities.Where(a => a.UserId == userId && a.Start <= end && a.End >= start).ToListAsync();
        var merged = new Data.Availability
        {
            UserId = userId,
            Start = touching.Select(a => a.Start).Append(start).Min(),
            End = touching.Select(a => a.End).Append(end).Max(),
        };
        db.Availabilities.RemoveRange(touching);
        db.Availabilities.Add(merged);
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return new AvailabilityDto(merged.Id, merged.Start, merged.End);
    }

    // Remove a range (a whole block, or a piece of one – the rest is kept, possibly split in two).
    public async Task<Result> RemoveAsync(string userId, RangeRequest req)
    {
        if (Invalid(req) is { } invalid) return invalid;
        var (s, e) = (req.Start.UtcDateTime, req.End.UtcDateTime);

        // Interviewers must stay available for the whole interview incl. buffer.
        var clash = await db.Interviews.AnyAsync(i => i.Status == InterviewStatus.Scheduled
            && i.Start < e && i.BlockedUntil > s && i.Interviewers.Any(u => u.Id == userId));
        if (clash) return Error.Conflict("You have an interview in this time. Contact an admin to reassign it first.");

        await using var tx = await db.Database.BeginTransactionAsync();
        var overlapping = await db.Availabilities.Where(a => a.UserId == userId && a.Start < e && a.End > s).ToListAsync();
        foreach (var a in overlapping)
        {
            db.Availabilities.Remove(a);
            if (a.Start < s) db.Availabilities.Add(new Data.Availability { UserId = userId, Start = a.Start, End = s });
            if (a.End > e) db.Availabilities.Add(new Data.Availability { UserId = userId, Start = e, End = a.End });
        }
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return Result.Ok;
    }

    static Error? Invalid(RangeRequest r) =>
        r.End <= r.Start ? RangeError("End must be after start.")
        : r.End - r.Start > MaxBlock ? RangeError("A single range can be at most 24 hours.")
        : null;

    static Error RangeError(string message) => Error.Validation("Range", message);
}
