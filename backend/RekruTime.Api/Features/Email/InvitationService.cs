using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RekruTime.Api.Common;
using RekruTime.Api.Data;

namespace RekruTime.Api.Features.Email;

public record TemplateDto(string Key, string Language, string Subject, string Body, bool IsDefault);
public record TemplateRequest([Required, StringLength(200)] string Subject, [Required, StringLength(10000)] string Body);

public class InvitationService(AppDbContext db, IOptions<ClubOptions> club, IOptions<EmailOptions> email)
{
    // Why invitations can't go out right now (null = they can).
    public Error? CannotSend() =>
        !email.Value.IsConfigured ? Error.BadRequest("E-mail isn't configured – set it up first (Admin → E-mail), or create links without sending.")
        : !club.Value.HasPublicUrl ? Error.BadRequest("Club:PublicUrl isn't set, so e-mails can't contain a working link.")
        : null;

    // Queues the invitation for each link that has a recruit. Caller saves.
    public async Task<int> QueueAsync(IReadOnlyCollection<BookingLink> links)
    {
        var period = await db.RecruitmentSettings.FindAsync(1);
        var queued = 0;
        foreach (var link in links.Where(l => l.Recruit is not null))
        {
            var mail = Invitations.Render(await TemplateAsync(link.Language), new InvitationValues(
                link.Recruit!.FullName, club.Value.BookingUrl(link.Token), link.Sections.Select(s => s.Name).Order(),
                period?.InterviewsTo, club.Value.ContactEmail), link.Language);
            db.EmailMessages.Add(new EmailMessage
            {
                To = link.Recruit.Email, ToName = link.Recruit.FullName, Subject = mail.Subject, Body = mail.Body, BookingLinkId = link.Id,
            });
            queued++;
        }
        return queued;
    }

    // Built-in default until an admin saves their own version.
    public async Task<List<TemplateDto>> ListTemplatesAsync()
    {
        var custom = await db.EmailTemplates.Where(t => t.Key == Invitations.Key).ToDictionaryAsync(t => t.Language);
        return Invitations.Defaults.Select(d => custom.TryGetValue(d.Key, out var c)
            ? new TemplateDto(Invitations.Key, d.Key, c.Subject, c.Body, false)
            : new TemplateDto(Invitations.Key, d.Key, d.Value.Subject, d.Value.Body, true)).ToList();
    }

    public async Task<Result> SaveTemplateAsync(string language, TemplateRequest req)
    {
        if (!Invitations.Defaults.ContainsKey(language)) return Error.NotFound();
        if (!req.Body.Contains("{link}")) return Error.Validation("Body", "The text must contain {link} – otherwise the recruit can't book.");
        var t = await db.EmailTemplates.FindAsync(Invitations.Key, language);
        if (t is null) db.EmailTemplates.Add(t = new EmailTemplate { Key = Invitations.Key, Language = language, Subject = "", Body = "" });
        (t.Subject, t.Body) = (req.Subject.Trim(), req.Body.Trim());
        await db.SaveChangesAsync();
        return Result.Ok;
    }

    public Task ResetTemplateAsync(string language) =>
        db.EmailTemplates.Where(t => t.Key == Invitations.Key && t.Language == language).ExecuteDeleteAsync();

    // Unsaved text filled with sample values – exactly what a recruit would get.
    public async Task<Result<Template>> PreviewAsync(string language, TemplateRequest req)
    {
        if (!Invitations.Defaults.ContainsKey(language)) return Error.NotFound();
        var period = await db.RecruitmentSettings.FindAsync(1);
        var sections = await db.Sections.OrderBy(s => s.Name).Select(s => s.Name).Take(2).ToListAsync();
        return Invitations.Render(new Template(req.Subject, req.Body), new InvitationValues(
            "Jan Kowalski", club.Value.BookingUrl("AbCdEfGhIjKlMnOpQrStUv"), sections.Count > 0 ? sections : ["AI"],
            period?.InterviewsTo, club.Value.ContactEmail), language);
    }

    async Task<Template> TemplateAsync(string language)
    {
        var lang = Invitations.Defaults.ContainsKey(language) ? language : "pl";
        var custom = await db.EmailTemplates.FindAsync(Invitations.Key, lang);
        return custom is null ? Invitations.Defaults[lang] : new Template(custom.Subject, custom.Body);
    }
}
