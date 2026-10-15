using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using RekruTime.Api.Data;

namespace RekruTime.Api.Features.Availability;

// DateTimeOffset in requests so any offset the client sends is converted correctly; stored as UTC.
public record RangeRequest(DateTimeOffset Start, DateTimeOffset End);
public record AvailabilityDto(int Id, DateTime Start, DateTime End);

public static class AvailabilityEndpoints
{
    static readonly TimeSpan MaxQueryRange = TimeSpan.FromDays(62);

    public static void MapAvailabilityEndpoints(this IEndpointRouteBuilder app)
    {
        // Every logged-in user (recruiters and admins) manages only their own availability.
        var group = app.MapGroup("/api/availability").WithTags("Availability").RequireAuthorization();

        group.MapGet("/", async (DateTimeOffset from, DateTimeOffset to, ClaimsPrincipal user, AppDbContext db) =>
        {
            if (to <= from || to - from > MaxQueryRange) return Results.ValidationProblem(Error("Range must be positive and at most 62 days."));
            var (f, t, userId) = (from.UtcDateTime, to.UtcDateTime, UserId(user));
            var blocks = await db.Availabilities
                .Where(a => a.UserId == userId && a.Start < t && a.End > f)
                .OrderBy(a => a.Start)
                .Select(a => new AvailabilityDto(a.Id, a.Start, a.End))
                .ToListAsync();
            return Results.Ok(blocks);
        });

        // Add a range; overlapping or adjacent blocks are merged into one.
        group.MapPost("/", async (RangeRequest req, ClaimsPrincipal user, AppDbContext db) =>
        {
            if (Invalid(req) is { } invalid) return invalid;
            var (start, end, userId) = (req.Start.UtcDateTime, req.End.UtcDateTime, UserId(user));

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
            return Results.Ok(new AvailabilityDto(merged.Id, merged.Start, merged.End));
        });

        // Remove a range (e.g. a whole block, or a piece of one – the rest is kept, possibly split in two).
        group.MapDelete("/", async (DateTimeOffset start, DateTimeOffset end, ClaimsPrincipal user, AppDbContext db) =>
        {
            if (Invalid(new RangeRequest(start, end)) is { } invalid) return invalid;
            var (s, e, userId) = (start.UtcDateTime, end.UtcDateTime, UserId(user));

            // Interviewers must stay available for the whole interview incl. buffer.
            var clash = await db.Interviews.AnyAsync(i => i.Status == InterviewStatus.Scheduled
                && i.Start < e && i.BlockedUntil > s && i.Interviewers.Any(u => u.Id == userId));
            if (clash) return Results.Problem("You have an interview in this time. Contact an admin to reassign it first.", statusCode: 409);

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
            return Results.NoContent();
        });
    }

    static string UserId(ClaimsPrincipal user) => user.FindFirstValue(ClaimTypes.NameIdentifier)!;

    static IResult? Invalid(RangeRequest r) =>
        r.End <= r.Start ? Results.ValidationProblem(Error("End must be after start."))
        : r.End - r.Start > TimeSpan.FromDays(1) ? Results.ValidationProblem(Error("A single range can be at most 24 hours."))
        : null;

    static Dictionary<string, string[]> Error(string message) => new() { ["Range"] = [message] };
}
