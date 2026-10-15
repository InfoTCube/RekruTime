using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RekruTime.Api.Data;

namespace RekruTime.Api.Features.Email;

public record EmailSettingsDto(bool Configured, string? Host, int Port, string? FromAddress, string FromName, string? ReplyTo);
public record TemplateDto(string Key, string Language, string Subject, string Body, bool IsDefault);
public record TemplateRequest(
    [System.ComponentModel.DataAnnotations.Required, System.ComponentModel.DataAnnotations.StringLength(200)] string Subject,
    [System.ComponentModel.DataAnnotations.Required, System.ComponentModel.DataAnnotations.StringLength(10000)] string Body);
public record EmailMessageDto(int Id, string To, string Subject, EmailStatus Status, int Attempts, string? LastError, DateTime CreatedAt, DateTime? SentAt);

public static class EmailEndpoints
{
    public static void MapEmailEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/email").WithTags("E-mail").RequireAuthorization(Roles.Admin);

        // What is configured (never the password) – the admin page shows it next to the test button.
        group.MapGet("/settings", (IOptions<EmailOptions> o) =>
            new EmailSettingsDto(o.Value.IsConfigured, o.Value.Host, o.Value.Port, o.Value.FromAddress, o.Value.FromName, o.Value.ReplyTo));

        // Sends right away (not via the outbox) to the logged-in admin and returns the SMTP error, so setup problems show up immediately.
        group.MapPost("/test", async (ClaimsPrincipal user, SmtpMailer mailer, CancellationToken ct) =>
        {
            var to = user.FindFirstValue(ClaimTypes.Email) ?? user.FindFirstValue(ClaimTypes.Name)!;
            try
            {
                using var client = await mailer.ConnectAsync(ct);
                await mailer.SendAsync(client, to, null, "RekruTime: test e-mail", "If you can read this, RekruTime can send e-mails.", ct);
                await client.DisconnectAsync(true, ct);
                return Results.Ok(new { sentTo = to });
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                return Results.Problem($"Sending failed: {e.Message}", statusCode: 502);
            }
        });

        // Invitation template per language; built-in default until an admin saves their own version.
        group.MapGet("/templates", async (AppDbContext db) =>
        {
            var custom = await db.EmailTemplates.Where(t => t.Key == Invitations.Key).ToDictionaryAsync(t => t.Language);
            return Invitations.Defaults.Select(d => custom.TryGetValue(d.Key, out var c)
                ? new TemplateDto(Invitations.Key, d.Key, c.Subject, c.Body, false)
                : new TemplateDto(Invitations.Key, d.Key, d.Value.Subject, d.Value.Body, true));
        });

        group.MapPut("/templates/{language}", async (string language, TemplateRequest req, AppDbContext db) =>
        {
            if (!Invitations.Defaults.ContainsKey(language)) return Results.NotFound();
            if (!req.Body.Contains("{link}"))
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["Body"] = ["The text must contain {link} – otherwise the recruit can't book."] });
            var t = await db.EmailTemplates.FindAsync(Invitations.Key, language);
            if (t is null) db.EmailTemplates.Add(t = new EmailTemplate { Key = Invitations.Key, Language = language, Subject = "", Body = "" });
            (t.Subject, t.Body) = (req.Subject.Trim(), req.Body.Trim());
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        group.MapDelete("/templates/{language}", async (string language, AppDbContext db) =>
        {
            await db.EmailTemplates.Where(t => t.Key == Invitations.Key && t.Language == language).ExecuteDeleteAsync();
            return Results.NoContent(); // back to the built-in default
        });

        // Unsaved text filled with sample values – exactly what a recruit would get.
        group.MapPost("/templates/{language}/preview", async (string language, TemplateRequest req, AppDbContext db, IConfiguration config) =>
        {
            if (!Invitations.Defaults.ContainsKey(language)) return Results.NotFound();
            var period = await db.RecruitmentSettings.FindAsync(1);
            var sections = await db.Sections.OrderBy(s => s.Name).Select(s => s.Name).Take(2).ToListAsync();
            return Results.Ok(Invitations.Render(new Template(req.Subject, req.Body), new InvitationValues(
                "Jan Kowalski", $"{(config["Club:PublicUrl"] ?? "").TrimEnd('/')}/book/AbCdEfGhIjKlMnOpQrStUv",
                sections.Count > 0 ? sections : ["AI"], period?.InterviewsTo, config["Club:ContactEmail"]), language));
        });

        group.MapGet("/messages", async (EmailStatus? status, AppDbContext db) =>
        {
            var q = db.EmailMessages.AsQueryable();
            if (status is { } s) q = q.Where(m => m.Status == s);
            return await q.OrderByDescending(m => m.Id).Take(200)
                .Select(m => new EmailMessageDto(m.Id, m.To, m.Subject, m.Status, m.Attempts, m.LastError, m.CreatedAt, m.SentAt))
                .ToListAsync();
        });

        // Failed (or stuck) message → try again now.
        group.MapPost("/messages/{id:int}/retry", async (int id, AppDbContext db) =>
        {
            var updated = await db.EmailMessages.Where(m => m.Id == id && m.Status != EmailStatus.Sent)
                .ExecuteUpdateAsync(u => u.SetProperty(m => m.Status, EmailStatus.Pending).SetProperty(m => m.Attempts, 0).SetProperty(m => m.NextAttemptAt, DateTime.UtcNow));
            return updated == 0 ? Results.NotFound() : Results.NoContent();
        });
    }
}
