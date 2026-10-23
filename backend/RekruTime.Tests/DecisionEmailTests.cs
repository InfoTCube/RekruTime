using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RekruTime.Api.Data;
using RekruTime.Api.Features.Email;

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
public class DecisionEmailTests() : IntegrationTest(new Dictionary<string, string?>(TestApp.QueuedEmail) { ["Club:ContactEmail"] = "kontakt@test.local" })
{
    [Fact]
    public async Task Sends_one_decision_per_ready_recruit_and_locks_votes()
    {
        var admin = await App.AdminAsync();

        var (mixed, rejected, pending, mixedAi) = await App.DbAsync(async db =>
        {
            Section ai = new() { Name = "AI" }, web = new() { Name = "Web" };
            db.Sections.AddRange(ai, web);
            Recruit Recruit(string name, params (Section S, ApplicationStatus St)[] apps) =>
                new() { FullName = name, Email = $"{name.Split(' ')[0].ToLower()}@x.pl", Applications = apps.Select(a => new Application { Section = a.S, Status = a.St }).ToList() };
            var m = Recruit("Marta Nowak", (ai, ApplicationStatus.Accepted), (web, ApplicationStatus.Rejected));
            var r = Recruit("Rafal", (ai, ApplicationStatus.Rejected));
            var p = Recruit("Pola", (ai, ApplicationStatus.Accepted), (web, ApplicationStatus.Disputed));
            db.Recruits.AddRange(m, r, p);
            db.BookingLinks.Add(new BookingLink { Token = "marta", Recruit = m, Language = "en", Sections = [ai, web] }); // Marta booked in English
            db.EmailTemplates.Add(new EmailTemplate { Key = Decisions.Rejected, Language = "pl", Subject = "Niestety", Body = "Cześć {firstName}, tym razem nie ({sections})." });
            await db.SaveChangesAsync();
            return (m.Id, r.Id, p.Id, m.Applications.Single(a => a.Section == ai).Id);
        });

        var body = await TestApp.Json(admin.PostAsJsonAsync("/api/email/decisions", new { recruitIds = new[] { mixed, rejected, pending, 999999 } }));
        Assert.Equal(2, body.GetProperty("queued").GetInt32());
        Assert.Equal(2, body.GetProperty("skipped").GetInt32()); // Pola still disputed, unknown id

        var mails = (await App.MailsAsync()).Where(e => e.RecruitId != null).ToList();
        var toMarta = mails.Single(e => e.RecruitId == mixed);
        Assert.Equal("Recruitment result – welcome aboard!", toMarta.Subject); // accepted wins, her language
        Assert.Contains("(AI)", toMarta.Body); // only the accepted section
        Assert.Contains("kontakt@test.local", toMarta.Body);
        var toRafal = mails.Single(e => e.RecruitId == rejected);
        Assert.Equal("Niestety", toRafal.Subject); // admin's edited template, Polish default language
        Assert.Equal("Cześć Rafal, tym razem nie (AI).", toRafal.Body);
        Assert.DoesNotContain(mails, e => e.RecruitId == pending);

        // Once only, and votes are locked
        var again = await TestApp.Json(admin.PostAsJsonAsync("/api/email/decisions", new { recruitIds = new[] { mixed } }));
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
        Assert.DoesNotContain(await App.MailsAsync(), e => e.RecruitId == mixed);
    }

    [Fact]
    public async Task Decision_templates_are_listed_editable_and_previewed()
    {
        var admin = await App.AdminAsync();

        var list = await admin.GetFromJsonAsync<JsonElement>("/api/email/templates");
        Assert.Equal(EmailTemplates.Defaults.Keys.Order(), list.EnumerateArray().Select(t => t.GetProperty("key").GetString()).Distinct().Order());

        // {link} is required only in the invitation
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsJsonAsync("/api/email/templates/accepted/pl", new { subject = "Brawo", body = "Witaj {name}" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync("/api/email/templates/invitation/pl", new { subject = "x", body = "no link" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.PutAsJsonAsync("/api/email/templates/nope/pl", new { subject = "x", body = "y" })).StatusCode);

        var preview = await TestApp.Json(admin.PostAsJsonAsync("/api/email/templates/rejected/en/preview", new { subject = "Sorry {firstName}", body = "Hi {name}" }));
        Assert.Equal("Sorry Jan", preview.GetProperty("subject").GetString());
        Assert.Equal("Hi Jan Kowalski", preview.GetProperty("body").GetString());

        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync("/api/email/templates/accepted/pl")).StatusCode);
        var accepted = (await admin.GetFromJsonAsync<JsonElement>("/api/email/templates")).EnumerateArray()
            .Single(t => t.GetProperty("key").GetString() == "accepted" && t.GetProperty("language").GetString() == "pl");
        Assert.True(accepted.GetProperty("isDefault").GetBoolean());
    }
}
