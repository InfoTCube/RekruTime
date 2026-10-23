using System.Net.Http.Json;
using System.Text.Json;
using RekruTime.Api.Data;
using RekruTime.Api.Features.Email;

namespace RekruTime.Tests;

// Day-before reminders: only when turned on, once per interview, again after a move; booking/moving inside the last day counts as reminded.
public class ReminderTests() : IntegrationTest(TestApp.QueuedEmail)
{
    Task<int> Run(DateTime now) => App.UseAsync<ReminderJob, int>(job => job.RunAsync(now));

    async Task<List<EmailMessage>> Reminders() => (await App.MailsAsync()).Where(m => m.Subject.StartsWith("Przypomnienie")).ToList();

    [Fact]
    public void Reset_counts_a_time_inside_the_last_day_as_reminded()
    {
        var now = DateTime.UtcNow;
        var soon = new Interview { Start = now.AddHours(5) };
        var later = new Interview { Start = now.AddHours(30), ReminderSentAt = now.AddDays(-1) };
        ReminderJob.Reset(soon, now);
        ReminderJob.Reset(later, now);
        Assert.Equal(now, soon.ReminderSentAt);
        Assert.Null(later.ReminderSentAt);
    }

    [Fact]
    public async Task Sends_once_when_enabled_and_again_after_a_move()
    {
        var admin = await App.AdminAsync();
        var ai = await App.SectionAsync(admin, "AI");
        var now = DateTime.UtcNow;
        var due = await App.InterviewAsync("Ewa", now.AddHours(23), [ai], []);
        await App.InterviewAsync("Later", now.AddHours(30), [ai], []);
        await App.InterviewAsync("Cancelled", now.AddHours(22), [ai], [], InterviewStatus.Cancelled);
        await App.InterviewAsync("Reminded", now.AddHours(21), [ai], [], reminded: now.AddHours(-1));
        await App.InterviewAsync("Past", now.AddHours(-2), [ai], []);

        Assert.False((await admin.GetFromJsonAsync<JsonElement>("/api/settings/reminders")).GetProperty("enabled").GetBoolean()); // off by default
        Assert.Equal(0, await Run(now));

        await admin.PutAsJsonAsync("/api/settings/reminders", new { enabled = true });
        Assert.Equal(1, await Run(now));
        var mail = Assert.Single(await Reminders());
        Assert.Equal("ewa@x.pl", mail.To);
        Assert.Contains("Przypominamy o Twojej rozmowie rekrutacyjnej (AI)", mail.Body);
        Assert.Null(mail.Calendar);
        Assert.Equal(0, await Run(now.AddMinutes(5))); // once

        // Moved two days later → reminded again a day before the new time (the move itself e-mails "new time" now)
        await admin.PatchAsJsonAsync($"/api/interviews/{due}", new { start = now.AddHours(23 + 48) });
        Assert.Equal(0, await Run(now.AddHours(1)));
        Assert.Equal(1, await Run(now.AddHours(48)));
        Assert.Equal(2, (await Reminders()).Count);

        // Turned off → nothing more
        await admin.PutAsJsonAsync("/api/settings/reminders", new { enabled = false });
        await admin.PatchAsJsonAsync($"/api/interviews/{due}", new { start = now.AddHours(23 + 96) });
        Assert.Equal(0, await Run(now.AddHours(96)));
    }
}
