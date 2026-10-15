using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RekruTime.Api.Common;
using RekruTime.Api.Data;

namespace RekruTime.Api.Features.Booking;

public record BookingDto(DateTime Start, DateTime End);
public record PublicLinkDto(
    string? ClubName, List<string> Sections, int DurationMinutes, string? ContactEmail, string? Location,
    DateOnly? InterviewsFrom, DateOnly? InterviewsTo, string? Name, string? Email, string Language, BookingDto? Booking);
public record PublicSlotDto(DateTime Start, DateTime End, SlotColor Color); // no interviewer names for recruits

public record BookRequest(
    DateTimeOffset Start,
    [Required, StringLength(200, MinimumLength = 2)] string FullName,
    [Required, EmailAddress, StringLength(256)] string Email,
    [StringLength(30)] string? Phone,
    [StringLength(50)] string? DiscordNickname, // optional but recommended – how the club talks to members
    bool GdprConsent);

// What recruits can do with their link. No login: the unguessable token is the key.
public class PublicBookingService(AppDbContext db, SlotService slots, IOptions<ClubOptions> club)
{
    public async Task<Result<PublicLinkDto>> GetLinkAsync(string token)
    {
        var link = await FindAsync(token);
        if (Unavailable(link) is { } gone) return gone;
        var period = await db.RecruitmentSettings.FindAsync(1);
        return new PublicLinkDto(
            NullIfEmpty(club.Value.Name),
            link!.Sections.Select(s => s.Name).Order().ToList(),
            link.Sections.Max(s => s.InterviewDurationMinutes),
            NullIfEmpty(club.Value.ContactEmail), NullIfEmpty(club.Value.InterviewLocation),
            period?.InterviewsFrom, period?.InterviewsTo,
            link.Recruit?.FullName is { Length: > 0 } n ? n : null, link.Recruit?.Email, link.Language,
            await BookingOfAsync(link.Id));
    }

    // Whole recruitment period by default; yellow only when the recruit asks for it ("none of these times suit me").
    public async Task<Result<List<PublicSlotDto>>> GetSlotsAsync(string token, DateTimeOffset? from, DateTimeOffset? to, bool includeYellow)
    {
        var link = await FindAsync(token);
        if (Unavailable(link) is { } gone) return gone;
        if (await BookingOfAsync(link!.Id) is not null) return new List<PublicSlotDto>();

        var data = await slots.LoadAsync(link.Sections.Select(s => s.Id).ToList(), from?.UtcDateTime ?? DateTime.MinValue, to?.UtcDateTime ?? DateTime.MaxValue);
        if (data is null) return new List<PublicSlotDto>();
        return SlotCalculator.Calculate(data.Query)
            .Where(s => includeYellow || s.Color == SlotColor.Green)
            .Select(s => new PublicSlotDto(s.Start, s.End, s.Color)).ToList();
    }

    public async Task<Result<BookingDto>> BookAsync(string token, BookRequest req)
    {
        if (!req.GdprConsent) return Error.Validation("GdprConsent", "Consent is required to book an interview.");
        try
        {
            return await TryBookAsync(token, req);
        }
        // Double click / two tabs on a link without a recruit: both requests create the same new recruit and one
        // loses on the unique e-mail. Retry once – it now finds that recruit (and "already booked" for the same link).
        catch (Exception e) when (DbErrors.IsUniqueViolation(e, "IX_Recruits_Email"))
        {
            db.ChangeTracker.Clear();
            return await TryBookAsync(token, req);
        }
    }

    async Task<Result<BookingDto>> TryBookAsync(string token, BookRequest req)
    {
        var link = await FindAsync(token);
        if (Unavailable(link) is { } gone) return gone;
        if (await BookingOfAsync(link!.Id) is not null) return AlreadyBooked;

        // Recompute with the same calculator the page used – the slot must still be free right now.
        var start = req.Start.UtcDateTime;
        var duration = TimeSpan.FromMinutes(link.Sections.Max(s => s.InterviewDurationMinutes));
        var buffer = TimeSpan.FromMinutes(link.Sections.Max(s => s.BufferMinutes));
        var data = await slots.LoadAsync(link.Sections.Select(s => s.Id).ToList(), start, start + duration + buffer);
        var slot = data is null ? null : SlotCalculator.Calculate(data.Query).FirstOrDefault(s => s.Start == start);
        if (slot is null) return Error.Conflict("This time is no longer available. Please pick another one.");

        var recruit = await RecruitForAsync(link, req);
        link.Recruit ??= recruit; // anonymous link → now belongs to this recruit (import sees them as invited)
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
            return Error.Conflict("Someone has just booked this time. Please pick another one.");
        }
        catch (Exception e) when (DbErrors.IsUniqueViolation(e, "IX_Interviews_OneBookingPerLink"))
        {
            return AlreadyBooked;
        }
        return new BookingDto(slot.Start, slot.End);
    }

    Task<BookingLink?> FindAsync(string token) =>
        db.BookingLinks.Include(l => l.Sections).Include(l => l.Recruit).ThenInclude(r => r!.Applications)
            .FirstOrDefaultAsync(l => l.Token == token);

    // Same 404 for unknown and deactivated links – don't reveal which tokens exist.
    static Error? Unavailable(BookingLink? link) =>
        link is null || !link.IsActive || link.Sections.Count == 0 ? Error.NotFound("This link is not valid.")
        : link.ExpiresAt < DateTime.UtcNow ? Error.Gone("This link has expired.")
        : null;

    Task<BookingDto?> BookingOfAsync(int linkId) =>
        db.Interviews.Where(i => i.BookingLinkId == linkId && i.Status != InterviewStatus.Cancelled)
            .Select(i => new BookingDto(i.Start, i.End)).FirstOrDefaultAsync();

    static readonly Error AlreadyBooked = Error.Conflict("An interview has already been booked with this link.");

    // The link's recruit (created by the admin) or the one with this e-mail, else a new one. Name/phone/Discord as entered now.
    async Task<Recruit> RecruitForAsync(BookingLink link, BookRequest req)
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
