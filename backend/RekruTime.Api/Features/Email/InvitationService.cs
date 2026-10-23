using Microsoft.Extensions.Options;
using RekruTime.Api.Common;
using RekruTime.Api.Data;

namespace RekruTime.Api.Features.Email;

public class InvitationService(AppDbContext db, TemplateService templates, IOptions<ClubOptions> club, IOptions<EmailOptions> email)
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
            var mail = Invitations.Render(await templates.GetAsync(Invitations.Key, link.Language), new InvitationValues(
                link.Recruit!.FullName, club.Value.BookingUrl(link.Token), link.Sections.Select(s => s.Name).Order(),
                period?.InterviewsTo, club.Value.ContactEmail), link.Language, club.Value.Name);
            db.EmailMessages.Add(new EmailMessage
            {
                To = link.Recruit.Email, ToName = link.Recruit.FullName, Subject = mail.Subject, Body = mail.Body, HtmlBody = mail.Html, BookingLinkId = link.Id,
            });
            queued++;
        }
        return queued;
    }
}
