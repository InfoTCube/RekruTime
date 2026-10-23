using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RekruTime.Api.Data;
using RekruTime.Api.Features.Email;
using Testcontainers.PostgreSql;

namespace RekruTime.Tests;

// Day-before reminders: only when turned on, once per interview, again after a move; booking/moving inside the last day counts as reminded.
public class ReminderTests : IAsyncLifetime
{
    readonly PostgreSqlContainer _db = new PostgreSqlBuilder("postgres:17").Build();
    WebApplicationFactory<Program> _app = null!;

    public async Task InitializeAsync()
    {
        await _db.StartAsync();
        _app = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b
            .UseSetting("ConnectionStrings:Default", _db.GetConnectionString())
            .UseSetting("SeedAdmin:Email", "admin@test.local")
            .UseSetting("SeedAdmin:Password", "Admin123!")
            .UseSetting("Club:PublicUrl", "https://kn.test")
            .UseSetting("Email:Host", "smtp.invalid") // queued only
            .UseSetting("Email:FromAddress", "rekrutacja@test.local"));
    }

    public async Task DisposeAsync()
    {
        await _app.DisposeAsync();
        await _db.DisposeAsync();
    }

    async Task<int> Run(DateTime now)
    {
        using var scope = _app.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ReminderJob>().RunAsync(now);
    }

    async Task<List<EmailMessage>> Reminders()
    {
        using var scope = _app.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().EmailMessages.Where(m => m.Subject.StartsWith("Przypomnienie")).ToListAsync();
    }

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
        var admin = _app.CreateClient();
        await admin.PostAsJsonAsync("/api/auth/login", new { email = "admin@test.local", password = "Admin123!" });
        var now = DateTime.UtcNow;

        int due;
        using (var scope = _app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var ai = new Section { Name = "AI" };
            Interview At(string name, double hours, InterviewStatus status = InterviewStatus.Scheduled, DateTime? reminded = null)
            {
                var start = now.AddHours(hours);
                return new Interview
                {
                    Recruit = new Recruit { FullName = name, Email = $"{name.ToLower()}@x.pl" }, Start = start, End = start.AddMinutes(30),
                    BlockedUntil = start.AddMinutes(30), Status = status, ReminderSentAt = reminded, Sections = [ai],
                };
            }
            var dueInterview = At("Ewa", 23);
            db.Interviews.AddRange(dueInterview, At("Later", 30), At("Cancelled", 22, InterviewStatus.Cancelled),
                At("Reminded", 21, reminded: now.AddHours(-1)), At("Past", -2));
            await db.SaveChangesAsync();
            due = dueInterview.Id;
        }

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
