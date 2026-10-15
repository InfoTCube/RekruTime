using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.EntityFrameworkCore;
using MimeKit;
using RekruTime.Api.Data;

namespace RekruTime.Api.Features.Email;

// Bound from the "Email" config section (env vars in production: Email__Host, Email__Password, …). Secrets never in the DB.
public class EmailOptions
{
    public string? Host { get; set; }
    public int Port { get; set; } = 587;
    public SecureSocketOptions Security { get; set; } = SecureSocketOptions.Auto; // StartTls, SslOnConnect, None, Auto
    public string? Username { get; set; }
    public string? Password { get; set; }
    public string? FromAddress { get; set; }
    public string FromName { get; set; } = "Rekrutacja";
    public string? ReplyTo { get; set; }
    public int DelayBetweenMessagesMs { get; set; } = 1000; // stay under provider rate limits (Gmail ~ 20/min is safe)

    public bool IsConfigured => !string.IsNullOrWhiteSpace(Host) && !string.IsNullOrWhiteSpace(FromAddress);
}

public class SmtpMailer(Microsoft.Extensions.Options.IOptions<EmailOptions> options)
{
    readonly EmailOptions _o = options.Value;

    public async Task<SmtpClient> ConnectAsync(CancellationToken ct)
    {
        if (!_o.IsConfigured) throw new InvalidOperationException("E-mail is not configured (Email:Host, Email:FromAddress).");
        var client = new SmtpClient { Timeout = 30_000 };
        await client.ConnectAsync(_o.Host!, _o.Port, _o.Security, ct); // IsConfigured checked above
        if (!string.IsNullOrEmpty(_o.Username)) await client.AuthenticateAsync(_o.Username, _o.Password ?? "", ct);
        return client;
    }

    public Task SendAsync(SmtpClient client, string to, string? toName, string subject, string body, string? html, CancellationToken ct)
    {
        // With HTML: multipart/alternative – clients show HTML, the plain text is the fallback.
        var msg = new MimeMessage { Subject = subject, Body = new BodyBuilder { TextBody = body, HtmlBody = html }.ToMessageBody() };
        msg.From.Add(new MailboxAddress(_o.FromName, _o.FromAddress!));
        msg.To.Add(new MailboxAddress(toName ?? "", to));
        if (!string.IsNullOrWhiteSpace(_o.ReplyTo)) msg.ReplyTo.Add(MailboxAddress.Parse(_o.ReplyTo));
        return client.SendAsync(msg, ct);
    }
}

// Delivers the outbox. ponytail: single app instance assumed; with several, claim rows with FOR UPDATE SKIP LOCKED.
public class EmailSender(IServiceScopeFactory scopes, SmtpMailer mailer, Microsoft.Extensions.Options.IOptions<EmailOptions> options, ILogger<EmailSender> log)
    : BackgroundService
{
    public const int MaxAttempts = 5;
    static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (options.Value.IsConfigured) await SendPendingAsync(ct);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                log.LogError(e, "E-mail sending loop failed");
            }
            await Task.Delay(PollInterval, ct);
        }
    }

    public async Task SendPendingAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var batch = await db.EmailMessages
            .Where(m => m.Status == EmailStatus.Pending && m.NextAttemptAt <= DateTime.UtcNow)
            .OrderBy(m => m.Id).Take(50).ToListAsync(ct);
        if (batch.Count == 0) return;

        SmtpClient? client = null;
        try
        {
            foreach (var m in batch)
            {
                try
                {
                    client ??= await mailer.ConnectAsync(ct);
                    await mailer.SendAsync(client, m.To, m.ToName, m.Subject, m.Body, m.HtmlBody, ct);
                    (m.Status, m.SentAt, m.LastError) = (EmailStatus.Sent, DateTime.UtcNow, null);
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    m.Attempts++;
                    m.LastError = e.Message;
                    // 1, 4, 9, 16 min… then give up – an admin can resend.
                    if (m.Attempts >= MaxAttempts) m.Status = EmailStatus.Failed;
                    else m.NextAttemptAt = DateTime.UtcNow.AddMinutes(m.Attempts * m.Attempts);
                    log.LogWarning("E-mail {Id} to {To} failed (attempt {Attempt}): {Error}", m.Id, m.To, m.Attempts, e.Message);
                    if (client is { IsConnected: false }) client = null; // reconnect for the next one
                }
                await db.SaveChangesAsync(ct); // per message, so a crash never re-sends what already went out
                await Task.Delay(options.Value.DelayBetweenMessagesMs, ct);
            }
        }
        finally
        {
            if (client is not null)
            {
                if (client.IsConnected) await client.DisconnectAsync(true, CancellationToken.None);
                client.Dispose();
            }
        }
    }
}
