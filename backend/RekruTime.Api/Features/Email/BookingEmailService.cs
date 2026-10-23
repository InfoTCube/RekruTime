using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RekruTime.Api.Common;
using RekruTime.Api.Data;

namespace RekruTime.Api.Features.Email;

public enum InterviewChange { Booked, Moved, Cancelled, Restored, TeamChanged, Reminder }

// Queues the e-mails about one interview (caller saves): the recruit's confirmation / new time / cancellation and
// notices to the interviewers, each with a calendar event. Without e-mail configured it does nothing – booking
// never depends on e-mail. All rows carry RecruitId, so deleting the recruit (GDPR) deletes them.
public class BookingEmailService(AppDbContext db, TemplateService templates, IOptions<ClubOptions> club, IOptions<EmailOptions> email)
{
    // added/removed: interviewers swapped by an admin in the same change.
    public async Task QueueAsync(int interviewId, InterviewChange change, IReadOnlyCollection<string>? added = null, IReadOnlyCollection<string>? removed = null)
    {
        if (!email.Value.IsConfigured) return;
        var c = club.Value;
        var i = await db.Interviews.Where(x => x.Id == interviewId).Select(x => new
        {
            x.Id, x.Start, x.End, x.RecruitId, x.Recruit.FullName, x.Recruit.Email,
            Sections = x.Sections.OrderBy(s => s.Name).Select(s => s.Name).ToList(),
            Team = x.Interviewers.OrderBy(u => u.DisplayName).Select(u => new { u.Id, u.DisplayName, u.Email }).ToList(),
            Link = db.BookingLinks.Where(l => l.Id == x.BookingLinkId).Select(l => new { l.Token, l.Language }).FirstOrDefault(),
        }).SingleAsync();

        var uid = $"interview-{i.Id}@{(c.HasPublicUrl ? new Uri(c.PublicUrl!).Host : "rekrutime")}";
        var sequence = Ics.Sequence();
        var cancelled = change == InterviewChange.Cancelled;

        // Recruit – their language, their booking link
        var recruitKey = change switch
        {
            InterviewChange.Booked or InterviewChange.Restored => BookingMails.Confirmation,
            InterviewChange.Moved => BookingMails.Rescheduled,
            InterviewChange.Cancelled => BookingMails.Cancelled,
            InterviewChange.Reminder => BookingMails.Reminder,
            _ => null, // a new team is internal
        };
        if (recruitKey is not null)
        {
            var language = i.Link?.Language ?? "pl";
            var mail = BookingMails.Render(await templates.GetAsync(recruitKey, language), new BookingValues(
                i.FullName, i.Start, i.End, i.Sections, c.InterviewLocation, c.ContactEmail,
                i.Link is not null && c.HasPublicUrl ? c.BookingUrl(i.Link.Token) : null), language, c.Zone, c.Name);
            var summary = language == "en" ? "Recruitment interview" : "Rozmowa rekrutacyjna";
            Add(i.Email, i.FullName, mail, change == InterviewChange.Reminder ? null // the calendar didn't change
                : Ics.Event(uid, sequence, i.Start, i.End, string.IsNullOrWhiteSpace(c.Name) ? summary : $"{summary} – {c.Name}", c.InterviewLocation, null, cancelled));
        }

        // Interviewers – who gets which notice
        var staff = new StaffValues(i.FullName, i.Start, i.End, i.Sections, i.Team.Select(u => u.DisplayName),
            c.HasPublicUrl ? $"{c.PublicUrl!.TrimEnd('/')}/interviews/{i.Id}" : null);
        var staffEvent = $"Interview: {i.FullName} ({string.Join(", ", i.Sections)})";
        var stayNotice = change switch
        {
            InterviewChange.Booked or InterviewChange.Restored => StaffNotice.Booked,
            InterviewChange.Moved => StaffNotice.Moved,
            InterviewChange.Cancelled => StaffNotice.Cancelled,
            _ => (StaffNotice?)null, // new team: only the added/removed people hear about it; reminders are for the recruit
        };
        foreach (var u in i.Team)
        {
            var notice = added?.Contains(u.Id) == true ? StaffNotice.Added : stayNotice;
            if (notice is { } n)
                Add(u.Email!, u.DisplayName, BookingMails.RenderStaff(n, staff, c.Zone),
                    Ics.Event(uid, sequence, i.Start, i.End, staffEvent, c.InterviewLocation, staff.Link, cancelled));
        }
        if (removed is { Count: > 0 })
            foreach (var u in await db.Users.Where(u => removed.Contains(u.Id)).Select(u => new { u.Email, u.DisplayName }).ToListAsync())
                Add(u.Email!, u.DisplayName, BookingMails.RenderStaff(StaffNotice.Removed, staff, c.Zone),
                    Ics.Event(uid, sequence, i.Start, i.End, staffEvent, c.InterviewLocation, null, cancelled: true));

        void Add(string to, string? name, RenderedEmail mail, string? calendar) => db.EmailMessages.Add(new EmailMessage
        {
            To = to, ToName = name, Subject = mail.Subject, Body = mail.Body, HtmlBody = mail.Html, Calendar = calendar, RecruitId = i.RecruitId,
        });
    }
}
