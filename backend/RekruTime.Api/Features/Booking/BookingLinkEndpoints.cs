using System.Buffers.Text;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using RekruTime.Api.Data;

namespace RekruTime.Api.Features.Booking;

// Validated in the endpoint: built-in validation doesn't descend into array items.
public record Recipient(string? Name, string? Email);

public record CreateLinksRequest(
    [MinLength(1)] int[] SectionIds,
    DateTimeOffset? ExpiresAt,
    [MaxLength(200)] Recipient[]? Recipients, // one link per recipient …
    [Range(1, 200)] int? Count);              // … or this many anonymous links

public enum LinkStatus { Unused, Booked, Expired, Inactive }

public record LinkDto(int Id, string Token, string Url, string? Label, string? RecruitName, string? RecruitEmail,
    List<string> Sections, DateTime CreatedAt, DateTime? ExpiresAt, LinkStatus Status, DateTime? InterviewStart);

public record SetActiveRequest(bool IsActive);

public record PreviewSlotDto(DateTime Start, DateTime End, SlotColor Color, List<string> Team);

public static class BookingLinkEndpoints
{
    static readonly EmailAddressAttribute Email = new();

    public static void MapBookingLinkEndpoints(this IEndpointRouteBuilder app)
    {
        var links = app.MapGroup("/api/booking-links").WithTags("Booking links").RequireAuthorization(Roles.Admin);

        links.MapGet("/", async (int? sectionId, LinkStatus? status, AppDbContext db) =>
        {
            var query = db.BookingLinks.AsQueryable();
            if (sectionId is { } sid) query = query.Where(l => l.Sections.Any(s => s.Id == sid));
            var list = await Project(query, db).ToListAsync();
            return list.Select(ToDto).Where(l => status is null || l.Status == status);
        });

        links.MapPost("/", async (CreateLinksRequest req, ClaimsPrincipal user, AppDbContext db) =>
        {
            if (req.Recipients is null == req.Count is null)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["Recipients"] = ["Give either a list of recipients or a count."] });
            var bad = (req.Recipients ?? []).Where(r => r.Name?.Length > 200 || r.Email is { Length: > 0 } e && (e.Length > 256 || !Email.IsValid(e.Trim()))).ToList();
            if (bad.Count > 0)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["Recipients"] = [$"Invalid e-mail or name: {string.Join(", ", bad.Select(b => b.Email ?? b.Name))}"] });
            var sectionIds = req.SectionIds.Distinct().ToArray();
            var sections = await db.Sections.Where(s => sectionIds.Contains(s.Id)).ToListAsync();
            if (sections.Count != sectionIds.Length)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["SectionIds"] = ["Unknown section."] });

            // Recruits are matched by e-mail, so a known recruit gets the same Recruit row (and later their applications).
            var recipients = req.Recipients ?? Enumerable.Repeat(new Recipient(null, null), req.Count!.Value).ToArray();
            var emails = recipients.Select(r => r.Email?.Trim().ToLowerInvariant()).OfType<string>().Where(e => e != "").Distinct().ToList();
            var recruits = await db.Recruits.Where(r => emails.Contains(r.Email)).ToDictionaryAsync(r => r.Email);

            var created = new List<BookingLink>();
            foreach (var r in recipients)
            {
                var (name, email) = (r.Name?.Trim() is { Length: > 0 } n ? n : null, r.Email?.Trim().ToLowerInvariant() is { Length: > 0 } e ? e : null);
                Recruit? recruit = null;
                if (email is not null && !recruits.TryGetValue(email, out recruit))
                    db.Recruits.Add(recruits[email] = recruit = new Recruit { FullName = name ?? "", Email = email });
                var link = new BookingLink
                {
                    Token = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(16)), // 128 bit, unguessable
                    Recruit = recruit,
                    Label = name ?? email,
                    CreatedById = user.FindFirstValue(ClaimTypes.NameIdentifier),
                    ExpiresAt = req.ExpiresAt?.UtcDateTime,
                    Sections = sections,
                };
                db.BookingLinks.Add(link);
                created.Add(link);
            }
            await db.SaveChangesAsync();

            var ids = created.Select(l => l.Id).ToList();
            var result = await Project(db.BookingLinks.Where(l => ids.Contains(l.Id)), db).ToListAsync();
            return Results.Ok(result.OrderBy(l => l.Id).Select(ToDto)); // same order as the pasted recipients
        });

        links.MapPatch("/{id:int}", async (int id, SetActiveRequest req, AppDbContext db) =>
        {
            var link = await db.BookingLinks.FindAsync(id);
            if (link is null) return Results.NotFound();
            link.IsActive = req.IsActive;
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        // Coverage preview for admins: same calculation as the public page, but with yellow and interviewer names.
        links.MapGet("/{id:int}/slots", async (int id, DateTimeOffset from, DateTimeOffset to, AppDbContext db, TimeZoneInfo club) =>
        {
            if (to <= from || to - from > TimeSpan.FromDays(62))
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["Range"] = ["Range must be positive and at most 62 days."] });
            var sectionIds = await db.BookingLinks.Where(l => l.Id == id).SelectMany(l => l.Sections.Select(s => s.Id)).ToListAsync();
            if (sectionIds.Count == 0) return Results.NotFound();

            var data = await SlotData.LoadAsync(db, club, sectionIds, from.UtcDateTime, to.UtcDateTime);
            if (data is not { } d) return Results.Ok(Array.Empty<PreviewSlotDto>());
            return Results.Ok(SlotCalculator.Calculate(d.Query)
                .Select(s => new PreviewSlotDto(s.Start, s.End, s.Color, s.Team.Select(t => d.Names[t]).ToList())));
        });
    }

    // Everything the list needs, in one query; status is derived in memory.
    static IQueryable<LinkRow> Project(IQueryable<BookingLink> links, AppDbContext db) =>
        links.OrderByDescending(l => l.CreatedAt).ThenByDescending(l => l.Id).Select(l => new LinkRow(
            l.Id, l.Token, l.Label, l.Recruit != null ? l.Recruit.FullName : null, l.Recruit != null ? l.Recruit.Email : null,
            l.Sections.OrderBy(s => s.Name).Select(s => s.Name).ToList(), l.CreatedAt, l.ExpiresAt, l.IsActive,
            db.Interviews.Where(i => i.BookingLinkId == l.Id && i.Status != InterviewStatus.Cancelled).Select(i => (DateTime?)i.Start).FirstOrDefault()));

    record LinkRow(int Id, string Token, string? Label, string? RecruitName, string? RecruitEmail, List<string> Sections,
        DateTime CreatedAt, DateTime? ExpiresAt, bool IsActive, DateTime? InterviewStart);

    static LinkDto ToDto(LinkRow l) => new(l.Id, l.Token, $"/book/{l.Token}", l.Label, l.RecruitName, l.RecruitEmail, l.Sections,
        l.CreatedAt, l.ExpiresAt,
        l.InterviewStart is not null ? LinkStatus.Booked
        : !l.IsActive ? LinkStatus.Inactive
        : l.ExpiresAt < DateTime.UtcNow ? LinkStatus.Expired
        : LinkStatus.Unused,
        l.InterviewStart);
}
