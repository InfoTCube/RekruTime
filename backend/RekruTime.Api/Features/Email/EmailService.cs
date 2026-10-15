using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RekruTime.Api.Common;
using RekruTime.Api.Data;

namespace RekruTime.Api.Features.Email;

public record EmailSettingsDto(bool Configured, string? Host, int Port, string? FromAddress, string FromName, string? ReplyTo);
public record EmailMessageDto(int Id, string To, string Subject, EmailStatus Status, int Attempts, string? LastError, DateTime CreatedAt, DateTime? SentAt);
public record TestSentDto(string SentTo);

// Admin side of e-mail: configuration status, test mail, outbox. Delivery itself is EmailSender (background).
public class EmailService(AppDbContext db, SmtpMailer mailer, IOptions<EmailOptions> options)
{
    // What is configured – never the password.
    public EmailSettingsDto Settings()
    {
        var o = options.Value;
        return new(o.IsConfigured, o.Host, o.Port, o.FromAddress, o.FromName, o.ReplyTo);
    }

    // Sends right away (not via the outbox) and returns the SMTP error, so setup problems show up immediately.
    public async Task<Result<TestSentDto>> SendTestAsync(string to, CancellationToken ct)
    {
        try
        {
            using var client = await mailer.ConnectAsync(ct);
            await mailer.SendAsync(client, to, null, "RekruTime: test e-mail", "If you can read this, RekruTime can send e-mails.", null, ct);
            await client.DisconnectAsync(true, ct);
            return new TestSentDto(to);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            return Error.BadGateway($"Sending failed: {e.Message}");
        }
    }

    public Task<List<EmailMessageDto>> MessagesAsync(EmailStatus? status)
    {
        var q = db.EmailMessages.AsQueryable();
        if (status is { } s) q = q.Where(m => m.Status == s);
        return q.OrderByDescending(m => m.Id).Take(200)
            .Select(m => new EmailMessageDto(m.Id, m.To, m.Subject, m.Status, m.Attempts, m.LastError, m.CreatedAt, m.SentAt))
            .ToListAsync();
    }

    // Failed (or stuck) message → try again now.
    public async Task<Result> RetryAsync(int id)
    {
        var updated = await db.EmailMessages.Where(m => m.Id == id && m.Status != EmailStatus.Sent)
            .ExecuteUpdateAsync(u => u.SetProperty(m => m.Status, EmailStatus.Pending).SetProperty(m => m.Attempts, 0).SetProperty(m => m.NextAttemptAt, DateTime.UtcNow));
        return updated == 0 ? Error.NotFound() : Result.Ok;
    }
}
