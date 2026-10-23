namespace RekruTime.Api.Data;

public enum EmailStatus { Pending, Sent, Failed }

// Outbox: requests only add rows; EmailSender (background) delivers them, so a slow or broken SMTP server
// never fails a request, and every e-mail has a visible status.
public class EmailMessage
{
    public int Id { get; set; }
    public required string To { get; set; }
    public string? ToName { get; set; }
    public required string Subject { get; set; }
    public required string Body { get; set; } // plain text (always sent – alternative part for clients without HTML)
    public string? HtmlBody { get; set; }
    public string? Calendar { get; set; } // .ics event attached as invite.ics (booking e-mails)
    public EmailStatus Status { get; set; } = EmailStatus.Pending;
    public int Attempts { get; set; }
    public string? LastError { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime NextAttemptAt { get; set; } = DateTime.UtcNow;
    public DateTime? SentAt { get; set; }
    public int? BookingLinkId { get; set; } // invitation for this link (deleted with it – contains personal data)
    public int? RecruitId { get; set; }     // decision for this recruit (deleted with them)
}
