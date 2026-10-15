using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RekruTime.Api.Data;

namespace RekruTime.Api.Features.Email;

public record EmailSettingsDto(bool Configured, string? Host, int Port, string? FromAddress, string FromName, string? ReplyTo);
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
