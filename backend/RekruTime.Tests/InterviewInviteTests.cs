using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RekruTime.Api.Data;

namespace RekruTime.Tests;

// After preselection an admin invites: one link per recruit for all preselected sections, applications → InterviewInvited,
// invitation in the form's language. Not preselected = a final "no" for the decision e-mail.
public class InterviewInviteTests() : IntegrationTest(TestApp.QueuedEmail)
{
    record Club(HttpClient Admin, int Ai, int Web, Dictionary<string, int> Recruits);

    // Ada: AI + Web (EN form), Bob: AI + Web, Cyd: Web. Then: Ada AI+Web preselected, Bob AI preselected (Web still to
    // review), Cyd not preselected.
    async Task<Club> PreselectedClubAsync()
    {
        var admin = await App.AdminAsync();
        int ai = await App.SectionAsync(admin, "AI"), web = await App.SectionAsync(admin, "Web");
        var form = (await TestApp.Json(admin.PostAsJsonAsync("/api/forms", new
        {
            title = "Rekrutacja",
            fields = new object[] { new { id = "sections", type = "Sections", label = "Sections", required = true } },
        }))).GetProperty("id").GetInt32();
        await TestApp.Json(admin.PatchAsJsonAsync($"/api/forms/{form}", new { isOpen = true }));
        foreach (var (name, sections, language) in new[] { ("Ada", new[] { ai, web }, "en"), ("Bob", new[] { ai, web }, "pl"), ("Cyd", new[] { web }, "pl") })
            await TestApp.Json(App.CreateClient().PostAsJsonAsync($"/api/public/forms/{form}/responses", new
            {
                fullName = name, email = $"{name.ToLowerInvariant()}@x.pl", gdprConsent = true, language,
                answers = new Dictionary<string, string[]> { ["sections"] = sections.Select(s => $"{s}").ToArray() },
            }));

        var apps = await App.DbAsync(db => db.Applications.Join(db.Recruits, a => a.RecruitId, r => r.Id, (a, r) => new { a.Id, a.SectionId, r.FullName }).ToListAsync());
        int App_(string name, int section) => apps.Single(a => a.FullName == name && a.SectionId == section).Id;
        await TestApp.Json(admin.PostAsJsonAsync("/api/preselection", new { applicationIds = new[] { App_("Ada", ai), App_("Ada", web), App_("Bob", ai) }, status = "Preselected" }));
        await TestApp.Json(admin.PostAsJsonAsync("/api/preselection", new { applicationIds = new[] { App_("Cyd", web) }, status = "NotPreselected" }));
        var recruits = await App.DbAsync(db => db.Recruits.ToDictionaryAsync(r => r.FullName, r => r.Id));
        return new(admin, ai, web, recruits);
    }

    [Fact]
    public async Task Admin_invites_preselected_recruits_with_one_link_for_all_their_preselected_sections()
    {
        var club = await PreselectedClubAsync();

        var candidates = (await club.Admin.GetFromJsonAsync<JsonElement>("/api/preselection/invitations")).EnumerateArray().ToList();
        Assert.Equal(["Ada", "Bob"], candidates.Select(c => c.GetProperty("fullName").GetString()));
        Assert.Equal(["AI", "Web"], candidates[0].GetProperty("sections").EnumerateArray().Select(s => s.GetString()));
        Assert.Equal(["Web"], candidates[1].GetProperty("pendingReview").EnumerateArray().Select(s => s.GetString()));

        var mailsBefore = await App.LastMailIdAsync();
        var result = await TestApp.Json(club.Admin.PostAsJsonAsync("/api/preselection/invitations",
            new { recruitIds = new[] { club.Recruits["Ada"], club.Recruits["Bob"], club.Recruits["Cyd"] }, sendEmails = true }));
        Assert.Equal((2, 2, 1), (result.GetProperty("created").GetInt32(), result.GetProperty("emailsQueued").GetInt32(), result.GetProperty("skipped").GetInt32()));

        var links = await App.DbAsync(db => db.BookingLinks.Include(l => l.Sections).Include(l => l.Recruit).AsNoTracking().ToListAsync());
        var ada = links.Single(l => l.Recruit!.FullName == "Ada");
        Assert.Equal(["AI", "Web"], ada.Sections.Select(s => s.Name).Order());
        Assert.Equal("en", ada.Language);
        Assert.Equal(["AI"], links.Single(l => l.Recruit!.FullName == "Bob").Sections.Select(s => s.Name));

        var statuses = await App.DbAsync(db => db.Applications.Join(db.Recruits, a => a.RecruitId, r => r.Id, (a, r) => new { r.FullName, a.SectionId, a.Status }).ToListAsync());
        Assert.Equal(ApplicationStatus.InterviewInvited, statuses.Single(a => a.FullName == "Bob" && a.SectionId == club.Ai).Status);
        Assert.Equal(ApplicationStatus.Submitted, statuses.Single(a => a.FullName == "Bob" && a.SectionId == club.Web).Status); // still to review

        var mails = await App.MailsAsync(mailsBefore);
        var adaMail = mails.Single(m => m.To == "ada@x.pl");
        Assert.Contains($"{TestApp.PublicUrl}/book/{ada.Token}", adaMail.Body);
        Assert.Equal("Invitation to a recruitment interview – AI, Web", adaMail.Subject); // the form was filled in English

        // Bob's Web gets preselected later: he already has a working link → listed as such and skipped.
        var webApp = await App.DbAsync(db => db.Applications.Where(a => a.RecruitId == club.Recruits["Bob"] && a.SectionId == club.Web).Select(a => a.Id).SingleAsync());
        await TestApp.Json(club.Admin.PostAsJsonAsync("/api/preselection", new { applicationIds = new[] { webApp }, status = "Preselected" }));
        var bob = Assert.Single((await club.Admin.GetFromJsonAsync<JsonElement>("/api/preselection/invitations")).EnumerateArray());
        Assert.True(bob.GetProperty("alreadyInvited").GetBoolean());
        var again = await TestApp.Json(club.Admin.PostAsJsonAsync("/api/preselection/invitations", new { recruitIds = new[] { club.Recruits["Bob"] }, sendEmails = false }));
        Assert.Equal((0, 1), (again.GetProperty("created").GetInt32(), again.GetProperty("skipped").GetInt32()));
    }

    [Fact]
    public async Task Invited_applications_can_be_voted_on_and_not_preselected_recruits_get_the_rejection()
    {
        var club = await PreselectedClubAsync();
        await TestApp.Json(club.Admin.PostAsJsonAsync("/api/preselection/invitations", new { recruitIds = new[] { club.Recruits["Ada"] }, sendEmails = false }));

        // Invited → voting is open again (leader + admin agree → Accepted).
        var leader = (await App.UserAsync(club.Admin, "Lea", [club.Ai], leads: [club.Ai])).Client;
        var adaAi = await App.DbAsync(db => db.Applications.Where(a => a.RecruitId == club.Recruits["Ada"] && a.SectionId == club.Ai).Select(a => a.Id).SingleAsync());
        await TestApp.Json(leader.PostAsJsonAsync($"/api/applications/{adaAi}/votes", new { decision = "Approve" }));
        var voted = await TestApp.Json(club.Admin.PostAsJsonAsync($"/api/applications/{adaAi}/votes", new { decision = "Approve" }));
        Assert.Equal("Accepted", voted.GetProperty("status").GetString());

        // Cyd (only "not preselected") is ready for the decision e-mail: the rejection, in Polish.
        var before = await App.LastMailIdAsync();
        var sent = await TestApp.Json(club.Admin.PostAsJsonAsync("/api/email/decisions", new { recruitIds = new[] { club.Recruits["Cyd"], club.Recruits["Ada"] } }));
        Assert.Equal((1, 1), (sent.GetProperty("queued").GetInt32(), sent.GetProperty("skipped").GetInt32())); // Ada's Web is still open
        var mail = Assert.Single(await App.MailsAsync(before));
        Assert.Equal("cyd@x.pl", mail.To);

        // Told → preselection can't change it any more.
        var cydWeb = await App.DbAsync(db => db.Applications.Where(a => a.RecruitId == club.Recruits["Cyd"]).Select(a => a.Id).SingleAsync());
        var moved = await TestApp.Json(club.Admin.PostAsJsonAsync("/api/preselection", new { applicationIds = new[] { cydWeb }, status = "Preselected" }));
        Assert.Equal(0, moved.GetProperty("updated").GetInt32());
    }

    [Fact]
    public async Task Sending_invitations_needs_email_set_up()
    {
        var app = new TestApp(); // no e-mail configured
        await app.StartAsync();
        try
        {
            var admin = await app.AdminAsync();
            Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/preselection/invitations", new { recruitIds = new[] { 1 }, sendEmails = true })).StatusCode);
            var noMail = await TestApp.Json(admin.PostAsJsonAsync("/api/preselection/invitations", new { recruitIds = new[] { 1 }, sendEmails = false }));
            Assert.Equal(0, noMail.GetProperty("created").GetInt32());
        }
        finally { await app.DisposeAsync(); }
    }
}
