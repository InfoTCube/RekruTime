using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RekruTime.Api.Common;
using RekruTime.Api.Data;

namespace RekruTime.Api.Features.Email;

public record TemplateDto(string Key, string Language, string Subject, string Body, bool IsDefault);
public record TemplateRequest([Required, StringLength(200)] string Subject, [Required, StringLength(10000)] string Body);

// Admin-editable e-mail texts (EmailTemplates): built-in default until an admin saves their own version.
public class TemplateService(AppDbContext db, IOptions<ClubOptions> club)
{
    public async Task<List<TemplateDto>> ListAsync()
    {
        var custom = await db.EmailTemplates.ToDictionaryAsync(t => (t.Key, t.Language));
        return EmailTemplates.Defaults.SelectMany(kind => kind.Value.Select(d => custom.TryGetValue((kind.Key, d.Key), out var c)
            ? new TemplateDto(kind.Key, d.Key, c.Subject, c.Body, false)
            : new TemplateDto(kind.Key, d.Key, d.Value.Subject, d.Value.Body, true))).ToList();
    }

    public async Task<Result> SaveAsync(string key, string language, TemplateRequest req)
    {
        if (!Exists(key, language)) return Error.NotFound();
        if (EmailTemplates.RequiredPlaceholder(key) is { } required && !req.Body.Contains(required))
            return Error.Validation("Body", $"The text must contain {required} – otherwise the recruit can't book.");
        var t = await db.EmailTemplates.FindAsync(key, language);
        if (t is null) db.EmailTemplates.Add(t = new EmailTemplate { Key = key, Language = language, Subject = "", Body = "" });
        (t.Subject, t.Body) = (req.Subject.Trim(), req.Body.Trim());
        await db.SaveChangesAsync();
        return Result.Ok;
    }

    public Task ResetAsync(string key, string language) =>
        db.EmailTemplates.Where(t => t.Key == key && t.Language == language).ExecuteDeleteAsync();

    // Unsaved text filled with sample values – exactly what a recruit would get.
    public async Task<Result<RenderedEmail>> PreviewAsync(string key, string language, TemplateRequest req)
    {
        if (!Exists(key, language)) return Error.NotFound();
        var sections = await db.Sections.OrderBy(s => s.Name).Select(s => s.Name).Take(2).ToListAsync();
        if (sections.Count == 0) sections = ["AI"];
        var t = new Template(req.Subject, req.Body);
        if (key != Invitations.Key) return Decisions.Render(t, new DecisionValues("Jan Kowalski", sections, club.Value.ContactEmail), language, club.Value.Name);
        var period = await db.RecruitmentSettings.FindAsync(1);
        return Invitations.Render(t, new InvitationValues(
            "Jan Kowalski", club.Value.BookingUrl("AbCdEfGhIjKlMnOpQrStUv"), sections, period?.InterviewsTo, club.Value.ContactEmail), language, club.Value.Name);
    }

    // The text to send: the admin's version, else the default; unknown language → Polish.
    public async Task<Template> GetAsync(string key, string language)
    {
        var defaults = EmailTemplates.Defaults[key];
        var lang = defaults.ContainsKey(language) ? language : "pl";
        var custom = await db.EmailTemplates.FindAsync(key, lang);
        return custom is null ? defaults[lang] : new Template(custom.Subject, custom.Body);
    }

    static bool Exists(string key, string language) => EmailTemplates.Defaults.TryGetValue(key, out var d) && d.ContainsKey(language);
}
