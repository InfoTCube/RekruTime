using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RekruTime.Api.Data;
using RekruTime.Api.Features.Email;
using Testcontainers.PostgreSql;

namespace RekruTime.Tests;

public class DecisionTextTests
{
    static readonly DecisionValues Jan = new("Jan Kowalski", ["AI", "Web"], "rekrutacja@kn.pl");

    [Theory]
    [InlineData(Decisions.Accepted, "pl")]
    [InlineData(Decisions.Accepted, "en")]
    [InlineData(Decisions.Rejected, "pl")]
    [InlineData(Decisions.Rejected, "en")]
    public void Defaults_fill_every_placeholder(string key, string language)
    {
        var r = Decisions.Render(EmailTemplates.Defaults[key][language], Jan, language, "Koło");
        Assert.DoesNotContain("{", r.Subject + r.Body);
        Assert.Contains("Jan", r.Body);
        Assert.Contains("AI, Web", r.Body);
        Assert.Contains("rekrutacja@kn.pl", r.Html);
        Assert.Contains("Koło", r.Html);
    }

    [Fact]
    public void Html_has_no_button_or_link_fallback()
    {
        var html = Decisions.Render(Decisions.AcceptedDefaults["pl"], Jan, "pl").Html;
        Assert.DoesNotContain("Przycisk nie działa", html);
        Assert.DoesNotContain("padding:14px 28px", html); // the button style
    }

    [Fact]
    public void Every_kind_has_every_language() =>
        Assert.All(EmailTemplates.Defaults.Values, d => Assert.Equal(Invitations.Defaults.Keys.Order(), d.Keys.Order()));
}

// Admin sends decision e-mails: only for recruits whose applications are all decided, once; votes lock afterwards.
public class DecisionEmailTests : IAsyncLifetime
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
            .UseSetting("Club:ContactEmail", "kontakt@test.local")
            .UseSetting("Email:Host", "smtp.invalid") // queued only; the sender's attempts fail, which doesn't matter here
            .UseSetting("Email:FromAddress", "rekrutacja@test.local"));
    }

    public async Task DisposeAsync()
    {
        await _app.DisposeAsync();
        await _db.DisposeAsync();
    }

    [Fact]
    public async Task Sends_one_decision_per_ready_recruit_and_locks_votes()
    {
        var admin = _app.CreateClient();
        await admin.PostAsJsonAsync("/api/auth/login", new { email = "admin@test.local", password = "Admin123!" });

        int mixed, rejected, pending, mixedAi;
        using (var scope = _app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Section ai = new() { Name = "AI" }, web = new() { Name = "Web" };
            db.Sections.AddRange(ai, web);
            Recruit Recruit(string name, params (Section S, ApplicationStatus St)[] apps) =>
                new() { FullName = name, Email = $"{name.ToLower()}@x.pl", Applications = apps.Select(a => new Application { Section = a.S, Status = a.St }).ToList() };
            var m = Recruit("Marta Nowak", (ai, ApplicationStatus.Accepted), (web, ApplicationStatus.Rejected));
            var r = Recruit("Rafal", (ai, ApplicationStatus.Rejected));
            var p = Recruit("Pola", (ai, ApplicationStatus.Accepted), (web, ApplicationStatus.Disputed));
            db.Recruits.AddRange(m, r, p);
            db.BookingLinks.Add(new BookingLink { Token = "marta", Recruit = m, Language = "en", Sections = [ai, web] }); // Marta booked in English
            db.EmailTemplates.Add(new EmailTemplate { Key = Decisions.Rejected, Language = "pl", Subject = "Niestety", Body = "Cześć {firstName}, tym razem nie ({sections})." });
            await db.SaveChangesAsync();
            (mixed, rejected, pending, mixedAi) = (m.Id, r.Id, p.Id, m.Applications.Single(a => a.Section == ai).Id);
        }

        var res = await admin.PostAsJsonAsync("/api/email/decisions", new { recruitIds = new[] { mixed, rejected, pending, 999999 } });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var body = await res.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(2, body.GetProperty("queued").GetInt32());
        Assert.Equal(2, body.GetProperty("skipped").GetInt32()); // Pola still disputed, unknown id

        using (var scope = _app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var mails = await db.EmailMessages.Where(e => e.RecruitId != null).ToListAsync();
            var toMarta = mails.Single(e => e.RecruitId == mixed);
            Assert.Equal("Recruitment result – welcome aboard!", toMarta.Subject); // accepted wins, her language
            Assert.Contains("(AI)", toMarta.Body); // only the accepted section
            Assert.Contains("kontakt@test.local", toMarta.Body);
            var toRafal = mails.Single(e => e.RecruitId == rejected);
            Assert.Equal("Niestety", toRafal.Subject); // admin's edited template, Polish default language
            Assert.Equal("Cześć Rafal, tym razem nie (AI).", toRafal.Body);
            Assert.DoesNotContain(mails, e => e.RecruitId == pending);
        }

        // Once only, and votes are locked
        var again = await (await admin.PostAsJsonAsync("/api/email/decisions", new { recruitIds = new[] { mixed } })).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0, again.GetProperty("queued").GetInt32());
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync($"/api/applications/{mixedAi}/votes", new { decision = "Reject" })).StatusCode);
        var detail = await admin.GetFromJsonAsync<JsonElement>($"/api/recruits/{mixed}");
        Assert.All(detail.GetProperty("applications").EnumerateArray(), a =>
        {
            Assert.NotEqual(JsonValueKind.Null, a.GetProperty("decisionSentAt").ValueKind);
            Assert.False(a.GetProperty("canVote").GetBoolean());
        });

        // GDPR delete takes the decision e-mail along
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/recruits/{mixed}")).StatusCode);
        using (var scope = _app.Services.CreateScope())
            Assert.False(await scope.ServiceProvider.GetRequiredService<AppDbContext>().EmailMessages.AnyAsync(e => e.RecruitId == mixed));
    }

    [Fact]
    public async Task Decision_templates_are_listed_editable_and_previewed()
    {
        var admin = _app.CreateClient();
        await admin.PostAsJsonAsync("/api/auth/login", new { email = "admin@test.local", password = "Admin123!" });

        var list = await admin.GetFromJsonAsync<JsonElement>("/api/email/templates");
        Assert.Equal(EmailTemplates.Defaults.Keys.Order(), list.EnumerateArray().Select(t => t.GetProperty("key").GetString()).Distinct().Order());

        // {link} is required only in the invitation
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsJsonAsync("/api/email/templates/accepted/pl", new { subject = "Brawo", body = "Witaj {name}" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync("/api/email/templates/invitation/pl", new { subject = "x", body = "no link" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.PutAsJsonAsync("/api/email/templates/nope/pl", new { subject = "x", body = "y" })).StatusCode);

        var preview = await (await admin.PostAsJsonAsync("/api/email/templates/rejected/en/preview", new { subject = "Sorry {firstName}", body = "Hi {name}" }))
            .Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Sorry Jan", preview.GetProperty("subject").GetString());
        Assert.Equal("Hi Jan Kowalski", preview.GetProperty("body").GetString());

        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync("/api/email/templates/accepted/pl")).StatusCode);
        var accepted = (await admin.GetFromJsonAsync<JsonElement>("/api/email/templates")).EnumerateArray()
            .Single(t => t.GetProperty("key").GetString() == "accepted" && t.GetProperty("language").GetString() == "pl");
        Assert.True(accepted.GetProperty("isDefault").GetBoolean());
    }
}
