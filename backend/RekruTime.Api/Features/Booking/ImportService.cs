using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using RekruTime.Api.Common;
using RekruTime.Api.Data;
using RekruTime.Api.Features.Email;

namespace RekruTime.Api.Features.Booking;

// One row of the Google Form export, already mapped by the frontend (column choice, answer → section id).
public record ImportRow(int Line, string? Name, string? Email, int[]? SectionIds, string? Language);
public record ImportRequest([MaxLength(2000)] ImportRow[] Rows, DateTimeOffset? ExpiresAt, bool DryRun, bool SendEmails);
public enum ImportStatus { New, AlreadyInvited, Error }
public record ImportResultRow(int[] Lines, string Email, string? Name, List<string> Sections, string Language, ImportStatus Status, string? Message);
public record ImportResult(List<ImportResultRow> Rows, int Created, int EmailsQueued);

public class ImportService(AppDbContext db, InvitationService invitations)
{
    static readonly EmailAddressAttribute EmailCheck = new();

    // Preview with DryRun (nothing saved), then the same request for real: links + invitations in one transaction.
    public async Task<Result<ImportResult>> ImportAsync(ImportRequest req, string userId)
    {
        if (!req.DryRun && req.SendEmails && invitations.CannotSend() is { } why) return why;

        var sections = await db.Sections.ToDictionaryAsync(s => s.Id);
        var rows = await AnalyzeAsync(req.Rows, sections);
        var results = rows.Select(r => r.Result).ToList();
        if (req.DryRun) return new ImportResult(results, 0, 0);

        await using var tx = await db.Database.BeginTransactionAsync();
        var toCreate = rows.Where(r => r.Result.Status == ImportStatus.New).ToList();
        var emails = toCreate.Select(r => r.Result.Email).ToList();
        var recruits = await db.Recruits.Where(r => emails.Contains(r.Email)).ToDictionaryAsync(r => r.Email);
        var links = new List<BookingLink>();
        foreach (var (result, sectionIds) in toCreate)
        {
            if (!recruits.TryGetValue(result.Email, out var recruit))
                db.Recruits.Add(recruit = new Recruit { FullName = result.Name ?? "", Email = result.Email });
            else if (recruit.FullName == "" && result.Name is not null)
                recruit.FullName = result.Name;
            var link = new BookingLink
            {
                Token = BookingLinkService.NewToken(),
                Recruit = recruit,
                Label = result.Name ?? result.Email,
                Language = result.Language,
                CreatedById = userId,
                ExpiresAt = req.ExpiresAt?.UtcDateTime,
                Sections = sectionIds.Select(id => sections[id]).ToList(),
            };
            db.BookingLinks.Add(link);
            links.Add(link);
        }
        await db.SaveChangesAsync(); // links need ids before their e-mails reference them
        var queued = req.SendEmails ? await invitations.QueueAsync(links) : 0;
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return new ImportResult(results, links.Count, queued);
    }

    // Merge rows by e-mail (a person who ticked several sections, or appears twice), validate, find who's already invited.
    async Task<List<(ImportResultRow Result, int[] SectionIds)>> AnalyzeAsync(ImportRow[] input, Dictionary<int, Section> sections)
    {
        var groups = input
            .Select(r => r with { Email = r.Email?.Trim().ToLowerInvariant() ?? "", Name = string.IsNullOrWhiteSpace(r.Name) ? null : r.Name.Trim() })
            .GroupBy(r => r.Email!) // never null after the line above
            .ToList();

        var emails = groups.Select(g => g.Key).ToList();
        // Anyone with a link that still works or an active interview – re-importing the same file creates nothing new.
        var invited = await db.BookingLinks
            .Where(l => l.Recruit != null && emails.Contains(l.Recruit.Email) && l.IsActive && (l.ExpiresAt == null || l.ExpiresAt > DateTime.UtcNow))
            .Select(l => new { l.Recruit!.Email, Sections = l.Sections.Select(s => s.Name).ToList() })
            .ToListAsync();
        invited.AddRange(await db.Interviews
            .Where(i => i.Status != InterviewStatus.Cancelled && emails.Contains(i.Recruit.Email))
            .Select(i => new { i.Recruit.Email, Sections = i.Sections.Select(s => s.Name).ToList() })
            .ToListAsync());

        return groups.Select(g =>
        {
            var lines = g.Select(r => r.Line).ToArray();
            var name = g.Select(r => r.Name).FirstOrDefault(n => n is not null);
            var ids = g.SelectMany(r => r.SectionIds ?? []).Distinct().Where(sections.ContainsKey).OrderBy(id => sections[id].Name).ToArray();
            var language = g.Select(r => r.Language?.Trim().ToLowerInvariant()).FirstOrDefault(l => !string.IsNullOrEmpty(l)) ?? "pl";
            var names = ids.Select(id => sections[id].Name).ToList();

            string? error =
                g.Key == "" ? "Missing e-mail."
                : !EmailCheck.IsValid(g.Key) || g.Key.Length > 256 ? "Invalid e-mail."
                : g.Any(r => (r.SectionIds ?? []).Any(id => !sections.ContainsKey(id))) ? "Unknown section."
                : ids.Length == 0 ? "No section."
                : !Invitations.Defaults.ContainsKey(language) ? $"Unknown language \"{language}\"."
                : name?.Length > 200 ? "Name too long."
                : null;
            var already = invited.FirstOrDefault(i => i.Email == g.Key);
            var result = error is not null ? new ImportResultRow(lines, g.Key, name, names, language, ImportStatus.Error, error)
                : already is not null ? new ImportResultRow(lines, g.Key, name, names, language, ImportStatus.AlreadyInvited, $"Already has a link ({string.Join(", ", already.Sections)}).")
                : new ImportResultRow(lines, g.Key, name, names, language, ImportStatus.New, null);
            return (result, ids);
        }).OrderBy(r => r.result.Lines.Min()).ToList();
    }
}
