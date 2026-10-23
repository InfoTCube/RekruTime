using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using RekruTime.Api.Data;
using RekruTime.Api.Features.Email;

namespace RekruTime.Tests;

public class IcsTests
{
    static readonly DateTime Start = new(2026, 12, 2, 9, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Event_has_utc_times_and_escaped_text()
    {
        var ics = Ics.Event("interview-1@x", 5, Start, Start.AddMinutes(30), "Rozmowa; AI, Web", "Sala 1\nbudynek B", null);
        Assert.Contains("METHOD:PUBLISH\r\n", ics);
        Assert.Contains("DTSTART:20261202T090000Z\r\n", ics);
        Assert.Contains("DTEND:20261202T093000Z\r\n", ics);
        Assert.Contains("SUMMARY:Rozmowa\\; AI\\, Web\r\n", ics);
        Assert.Contains("LOCATION:Sala 1\\nbudynek B\r\n", ics);
        Assert.Contains("SEQUENCE:5\r\n", ics);
        Assert.DoesNotContain("STATUS:CANCELLED", ics);
    }

    [Fact]
    public void Cancel_event()
    {
        var ics = Ics.Event("u", 1, Start, Start.AddMinutes(30), "x", null, null, cancelled: true);
        Assert.Contains("METHOD:CANCEL\r\n", ics);
        Assert.Contains("STATUS:CANCELLED\r\n", ics);
        Assert.DoesNotContain("LOCATION", ics);
    }

    [Fact]
    public void Long_lines_fold_at_75_bytes_without_splitting_characters()
    {
        var ics = Ics.Event("u", 1, Start, Start.AddMinutes(30), new string('ż', 100), null, null);
        var lines = ics.Split("\r\n");
        Assert.All(lines, l => Assert.True(Encoding.UTF8.GetByteCount(l) <= 75, l));
        var summary = string.Concat(lines.SkipWhile(l => !l.StartsWith("SUMMARY:")).TakeWhile(l => l.StartsWith("SUMMARY:") || l.StartsWith(' '))
            .Select((l, n) => n == 0 ? l : l[1..]));
        Assert.Equal("SUMMARY:" + new string('ż', 100), summary); // unfolds back to the original
    }
}

public class BookingMailTextTests
{
    static readonly TimeZoneInfo Warsaw = TimeZoneInfo.FindSystemTimeZoneById("Europe/Warsaw");
    static readonly DateTime Start = new(2026, 12, 2, 9, 0, 0, DateTimeKind.Utc); // 10:00 in Warsaw (winter)

    [Theory]
    [InlineData(BookingMails.Confirmation, "pl")]
    [InlineData(BookingMails.Confirmation, "en")]
    [InlineData(BookingMails.Rescheduled, "pl")]
    [InlineData(BookingMails.Rescheduled, "en")]
    [InlineData(BookingMails.Cancelled, "pl")]
    [InlineData(BookingMails.Cancelled, "en")]
    [InlineData(BookingMails.Reminder, "pl")]
    [InlineData(BookingMails.Reminder, "en")]
    public void Defaults_fill_every_placeholder(string key, string language)
    {
        var v = new BookingValues("Jan Kowalski", Start, Start.AddMinutes(30), ["AI"], null, "kontakt@kn.pl", "https://kn.pl/book/abc");
        var r = BookingMails.Render(EmailTemplates.Defaults[key][language], v, language, Warsaw);
        Assert.DoesNotContain("{", r.Subject + r.Body);
        Assert.Contains("10:00–10:30", r.Body); // club time zone, not UTC
        Assert.Contains(language == "pl" ? "grudnia 2026" : "December 2026", r.Body);
    }

    [Fact]
    public void Missing_location_says_it_comes_later() =>
        Assert.Contains("Miejsce: podamy wkrótce", BookingMails.Render(BookingMails.ConfirmationDefaults["pl"],
            new BookingValues("Jan", Start, Start.AddMinutes(30), ["AI"], null, null, null), "pl", Warsaw).Body);

    [Fact]
    public void Staff_notice_links_the_interview_unless_cancelled()
    {
        var v = new StaffValues("Ewa", Start, Start.AddMinutes(30), ["AI"], ["Ala", "Ola"], "https://kn.pl/interviews/7");
        var booked = BookingMails.RenderStaff(StaffNotice.Booked, v, Warsaw);
        Assert.Equal("New interview: Ewa – Wednesday 2 December, 10:00–10:30", booked.Subject);
        Assert.Contains("https://kn.pl/interviews/7", booked.Body);
        Assert.Contains("Team: Ala, Ola", booked.Body);
        Assert.DoesNotContain("interviews/7", BookingMails.RenderStaff(StaffNotice.Cancelled, v, Warsaw).Body);
    }
}

// Booking → confirmation + interviewer notices with calendar events; admin moves/swaps/cancels → the right people hear about it.
public class BookingEmailTests() : IntegrationTest(new Dictionary<string, string?>(TestApp.QueuedEmail) { ["Club:InterviewLocation"] = "Sala 101" })
{
    [Fact]
    public async Task Booking_and_admin_changes_send_the_right_emails()
    {
        var setup = await Booking.FullSetupAsync(App, linkCount: 1);
        var (admin, link, slot) = (setup.Admin, setup.Links[0], setup.FirstSlot);
        var ala = setup.Recruiters[0];

        // 1. Booking → recruit confirmation + both interviewers (the account invites before don't count)
        var last = await App.LastMailIdAsync();
        Assert.Equal(HttpStatusCode.OK, await Booking.BookAsync(App, link, slot, "ewa@x.pl", "Ewa Nowak"));
        var mails = await App.MailsAsync(last);
        var toEwa = Assert.Single(mails, m => m.To == "ewa@x.pl");
        Assert.StartsWith("Potwierdzenie rozmowy rekrutacyjnej", toEwa.Subject);
        Assert.Contains("Miejsce: Sala 101", toEwa.Body);
        Assert.Contains($"{TestApp.PublicUrl}/book/{link}", toEwa.Body);
        Assert.Contains("METHOD:PUBLISH", toEwa.Calendar);
        Assert.Contains($"DTSTART:{slot:yyyyMMdd'T'HHmmss'Z'}", toEwa.Calendar);
        Assert.Equal(["ala@club.local", "ola@club.local"], mails.Where(m => m.To != "ewa@x.pl").Select(m => m.To).Order());
        var interviewId = (await TestApp.Json(admin.GetAsync("/api/interviews")))[0].GetProperty("id").GetInt32();
        Assert.All(mails.Where(m => m.To != "ewa@x.pl"), m =>
        {
            Assert.StartsWith("New interview: Ewa Nowak", m.Subject);
            Assert.Contains($"{TestApp.PublicUrl}/interviews/{interviewId}", m.Body);
        });
        Assert.All(mails, m => Assert.NotNull(m.RecruitId));
        last = mails.Max(m => m.Id);

        // 2. Move without notifying → nothing
        var url = $"/api/interviews/{interviewId}";
        await TestApp.Json(admin.PatchAsJsonAsync(url, new { start = slot.AddDays(1), notify = false }));
        Assert.Empty(await App.MailsAsync(last));

        // 3. Move → "rescheduled" for the recruit, "moved" for the team
        await TestApp.Json(admin.PatchAsJsonAsync(url, new { start = slot.AddDays(2) }));
        mails = await App.MailsAsync(last);
        Assert.StartsWith("Nowy termin rozmowy rekrutacyjnej", Assert.Single(mails, m => m.To == "ewa@x.pl").Subject);
        Assert.Equal(2, mails.Count(m => m.Subject.StartsWith("Interview moved")));
        last = mails.Max(m => m.Id);

        // 4. Swap Ola for the admin → only they hear about it; Ola's calendar event is cancelled
        await TestApp.Json(admin.PatchAsJsonAsync(url, new { interviewerIds = new[] { ala.Id, await App.AdminIdAsync() } }));
        mails = await App.MailsAsync(last);
        Assert.Equal(2, mails.Count);
        Assert.StartsWith("You're on an interview", Assert.Single(mails, m => m.To == TestApp.AdminEmail).Subject);
        var ola = Assert.Single(mails, m => m.To == "ola@club.local");
        Assert.StartsWith("You're off an interview", ola.Subject);
        Assert.Contains("METHOD:CANCEL", ola.Calendar);
        last = mails.Max(m => m.Id);

        // 5. Mark done → nothing; cancel → recruit gets a new-time link, the current team a cancel event
        await TestApp.Json(admin.PatchAsJsonAsync(url, new { status = "Done" }));
        Assert.Empty(await App.MailsAsync(last));
        await TestApp.Json(admin.PatchAsJsonAsync(url, new { status = "Scheduled" })); // Done → Scheduled isn't news either
        Assert.Empty(await App.MailsAsync(last));
        await TestApp.Json(admin.PatchAsJsonAsync(url, new { status = "Cancelled" }));
        mails = await App.MailsAsync(last);
        var cancelled = Assert.Single(mails, m => m.To == "ewa@x.pl");
        Assert.Equal("Rozmowa rekrutacyjna odwołana", cancelled.Subject);
        Assert.Contains($"{TestApp.PublicUrl}/book/{link}", cancelled.Body);
        Assert.Equal([TestApp.AdminEmail, "ala@club.local"], mails.Where(m => m.To != "ewa@x.pl").Select(m => m.To).Order());
        Assert.All(mails, m => Assert.Contains("METHOD:CANCEL", m.Calendar));

        // GDPR delete removes every e-mail about the recruit
        var recruitId = mails[0].RecruitId!.Value;
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/recruits/{recruitId}")).StatusCode);
        Assert.DoesNotContain(await App.MailsAsync(), m => m.RecruitId == recruitId || m.To == "ewa@x.pl"); // not Body.Contains("Ewa") – random invite tokens can contain that
    }
}
