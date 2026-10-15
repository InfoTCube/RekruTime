using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using RekruTime.Api.Data;

namespace RekruTime.Api.Features.Booking;

public record BookingDto(DateTime Start, DateTime End);
public record PublicLinkDto(
    List<string> Sections, int DurationMinutes, string? ContactEmail, string? Location,
    DateOnly? InterviewsFrom, DateOnly? InterviewsTo, string? Name, string? Email, BookingDto? Booking);
public record PublicSlotDto(DateTime Start, DateTime End, SlotColor Color); // no interviewer names for recruits

public record BookRequest(
    DateTimeOffset Start,
    [Required, StringLength(200, MinimumLength = 2)] string FullName,
    [Required, EmailAddress, StringLength(256)] string Email,
    [StringLength(30)] string? Phone,
    [StringLength(50)] string? DiscordNickname, // optional but recommended – how the club talks to members
    bool GdprConsent);

// No auth: the unguessable token is the key. Rate limited per IP.
public static class PublicBookingEndpoints
{
    public const string RateLimitPolicy = "public";

    public static void MapPublicBookingEndpoints(this IEndpointRouteBuilder app)
    {
        var links = app.MapGroup("/api/public/links/{token}").WithTags("Public booking").RequireRateLimiting(RateLimitPolicy);

        links.MapGet("/", async (string token, AppDbContext db, IConfiguration config) =>
        {
            var link = await Find(db, token);
            if (Unavailable(link) is { } gone) return gone;
            var period = await db.RecruitmentSettings.FindAsync(1);
            return Results.Ok(new PublicLinkDto(
                link!.Sections.Select(s => s.Name).Order().ToList(),
                link.Sections.Max(s => s.InterviewDurationMinutes),
                NullIfEmpty(config["Club:ContactEmail"]), NullIfEmpty(config["Club:InterviewLocation"]),
                period?.InterviewsFrom, period?.InterviewsTo,
                link.Recruit?.FullName is { Length: > 0 } n ? n : null, link.Recruit?.Email,
                await Booking(db, link.Id)));
        });

        // Whole recruitment period by default; yellow only when the recruit asks for it ("none of these times suit me").
        links.MapGet("/slots", async (string token, DateTimeOffset? from, DateTimeOffset? to, bool? includeYellow, AppDbContext db, TimeZoneInfo club) =>
        {
            var link = await Find(db, token);
            if (Unavailable(link) is { } gone) return gone;
            if (await Booking(db, link!.Id) is not null) return Results.Ok(Array.Empty<PublicSlotDto>());

            var data = await SlotData.LoadAsync(db, club, link.Sections.Select(s => s.Id).ToList(),
                from?.UtcDateTime ?? DateTime.MinValue, to?.UtcDateTime ?? DateTime.MaxValue);
            if (data is not { } d) return Results.Ok(Array.Empty<PublicSlotDto>());
            return Results.Ok(SlotCalculator.Calculate(d.Query)
                .Where(s => includeYellow == true || s.Color == SlotColor.Green)
                .Select(s => new PublicSlotDto(s.Start, s.End, s.Color)));
        });

        links.MapPost("/bookings", async (string token, BookRequest req, AppDbContext db, TimeZoneInfo club) =>
        {
            if (!req.GdprConsent)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["GdprConsent"] = ["Consent is required to book an interview."] });
            try
            {
                return await Book(token, req, db, club);
            }
            // Double click / two tabs on a link without a recruit: both requests create the same new recruit and one
            // loses on the unique e-mail. Retry once – it now finds that recruit (and "already booked" for the same link).
            catch (Exception e) when (DbErrors.IsUniqueViolation(e, "IX_Recruits_Email"))
            {
                db.ChangeTracker.Clear();
                return await Book(token, req, db, club);
            }
        });
    }

    static async Task<IResult> Book(string token, BookRequest req, AppDbContext db, TimeZoneInfo club)
    {
        var link = await Find(db, token);
        if (Unavailable(link) is { } gone) return gone;
        if (await Booking(db, link!.Id) is not null) return AlreadyBooked();

        // Recompute with the same calculator the page used – the slot must still be free right now.
        var start = req.Start.UtcDateTime;
        var sectionIds = link.Sections.Select(s => s.Id).ToList();
        var duration = TimeSpan.FromMinutes(link.Sections.Max(s => s.InterviewDurationMinutes));
        var buffer = TimeSpan.FromMinutes(link.Sections.Max(s => s.BufferMinutes));
        var data = await SlotData.LoadAsync(db, club, sectionIds, start, start + duration + buffer);
        var slot = data is { } d ? SlotCalculator.Calculate(d.Query).FirstOrDefault(s => s.Start == start) : null;
        if (slot is null) return Results.Problem("This time is no longer available. Please pick another one.", statusCode: 409);

        var recruit = await RecruitFor(db, link, req);
        foreach (var section in link.Sections.Where(s => !recruit.Applications.Any(a => a.SectionId == s.Id)))
            recruit.Applications.Add(new Application { SectionId = section.Id });

        db.Interviews.Add(new Interview
        {
            Recruit = recruit,
            BookingLinkId = link.Id,
            Start = slot.Start,
            End = slot.End,
            BlockedUntil = slot.End + buffer,
            Sections = link.Sections,
            Interviewers = await db.Users.Where(u => slot.Team.Contains(u.Id)).ToListAsync(),
        });

        try
        {
            await db.SaveChangesAsync();
        }
        // The DB is the final judge for races: two recruits clicking the same time, or one link booked twice.
        catch (Exception e) when (DbErrors.IsRoomTaken(e))
        {
            return Results.Problem("Someone has just booked this time. Please pick another one.", statusCode: 409);
        }
        catch (Exception e) when (DbErrors.IsUniqueViolation(e, "IX_Interviews_OneBookingPerLink"))
        {
            return AlreadyBooked();
        }
        return Results.Ok(new BookingDto(slot.Start, slot.End));
    }

    static Task<BookingLink?> Find(AppDbContext db, string token) =>
        db.BookingLinks.Include(l => l.Sections).Include(l => l.Recruit).ThenInclude(r => r!.Applications)
            .FirstOrDefaultAsync(l => l.Token == token);

    // Same 404 for unknown and deactivated links – don't reveal which tokens exist.
    static IResult? Unavailable(BookingLink? link) =>
        link is null || !link.IsActive || link.Sections.Count == 0 ? Results.Problem("This link is not valid.", statusCode: 404)
        : link.ExpiresAt < DateTime.UtcNow ? Results.Problem("This link has expired.", statusCode: 410)
        : null;

    static Task<BookingDto?> Booking(AppDbContext db, int linkId) =>
        db.Interviews.Where(i => i.BookingLinkId == linkId && i.Status != InterviewStatus.Cancelled)
            .Select(i => new BookingDto(i.Start, i.End)).FirstOrDefaultAsync();

    static IResult AlreadyBooked() => Results.Problem("An interview has already been booked with this link.", statusCode: 409);

    // The link's recruit (created by the admin) or the one with this e-mail, else a new one. Name/phone/Discord as entered now.
    static async Task<Recruit> RecruitFor(AppDbContext db, BookingLink link, BookRequest req)
    {
        var email = req.Email.Trim().ToLowerInvariant();
        var recruit = link.Recruit
            ?? await db.Recruits.Include(r => r.Applications).FirstOrDefaultAsync(r => r.Email == email)
            ?? db.Recruits.Add(new Recruit { FullName = "", Email = email }).Entity;
        // Keep the admin's e-mail if the new one belongs to another recruit (unique e-mail).
        if (recruit.Email != email && !await db.Recruits.AnyAsync(r => r.Email == email)) recruit.Email = email;
        recruit.FullName = req.FullName.Trim();
        recruit.ConsentAt = DateTime.UtcNow;
        recruit.Phone = string.IsNullOrWhiteSpace(req.Phone) ? recruit.Phone : req.Phone.Trim();
        recruit.DiscordNickname = string.IsNullOrWhiteSpace(req.DiscordNickname) ? recruit.DiscordNickname : req.DiscordNickname.Trim().TrimStart('@');
        return recruit;
    }

    static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;
}
