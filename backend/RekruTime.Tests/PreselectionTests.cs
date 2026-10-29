using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RekruTime.Api.Data;

namespace RekruTime.Tests;

// Leaders (their sections) and admins read form answers and mark applications Preselected / NotPreselected.
public class PreselectionTests : IntegrationTest
{
    record Club(HttpClient Admin, int Ai, int Web, HttpClient AiLeader, HttpClient Recruiter);

    async Task<Club> ClubWithApplicantsAsync()
    {
        var admin = await App.AdminAsync();
        int ai = await App.SectionAsync(admin, "AI"), web = await App.SectionAsync(admin, "Web");
        var leader = (await App.UserAsync(admin, "Ala", [ai], leads: [ai])).Client;
        var recruiter = (await App.UserAsync(admin, "Rita", [ai])).Client;
        var form = (await TestApp.Json(admin.PostAsJsonAsync("/api/forms", new
        {
            title = "Rekrutacja",
            fields = new object[]
            {
                new { id = "sections", type = "Sections", label = "Sections", required = true },
                new { id = "why", type = "Textarea", label = "Why?" },
            },
        }))).GetProperty("id").GetInt32();
        await TestApp.Json(admin.PatchAsJsonAsync($"/api/forms/{form}", new { isOpen = true }));
        foreach (var (name, sections) in new[] { ("Zosia", new[] { ai, web }), ("Bartek", new[] { ai }), ("Wiktor", new[] { web }) })
            await TestApp.Json(App.CreateClient().PostAsJsonAsync($"/api/public/forms/{form}/responses", new
            {
                fullName = name, email = $"{name.ToLowerInvariant()}@x.pl", gdprConsent = true,
                answers = new Dictionary<string, string[]> { ["sections"] = sections.Select(s => $"{s}").ToArray(), ["why"] = [$"I am {name}"] },
            }));
        return new(admin, ai, web, leader, recruiter);
    }

    static async Task<JsonElement> ListAsync(HttpClient c, int section) => await c.GetFromJsonAsync<JsonElement>($"/api/preselection?sectionId={section}");

    [Fact]
    public async Task Leaders_see_their_sections_applicants_with_answers_admins_see_all()
    {
        var club = await ClubWithApplicantsAsync();

        var ai = await ListAsync(club.AiLeader, club.Ai);
        Assert.Equal(["Bartek", "Zosia"], ai.GetProperty("applicants").EnumerateArray().Select(a => a.GetProperty("fullName").GetString()));
        var zosia = ai.GetProperty("applicants")[1];
        Assert.Equal("Submitted", zosia.GetProperty("status").GetString());
        Assert.Equal("I am Zosia", zosia.GetProperty("answers").EnumerateArray().Single(a => a.GetProperty("fieldId").GetString() == "why").GetProperty("values")[0].GetString());
        var form = Assert.Single(ai.GetProperty("forms").EnumerateArray());
        Assert.Equal(zosia.GetProperty("formId").GetInt32(), form.GetProperty("id").GetInt32());
        Assert.Equal(["Sections", "Why?"], form.GetProperty("fields").EnumerateArray().Select(f => f.GetProperty("label").GetString()));

        Assert.Equal(HttpStatusCode.Forbidden, (await club.AiLeader.GetAsync($"/api/preselection?sectionId={club.Web}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await club.Recruiter.GetAsync($"/api/preselection?sectionId={club.Ai}")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await club.Admin.GetAsync("/api/preselection")).StatusCode);
        Assert.Equal(["Wiktor", "Zosia"], (await ListAsync(club.Admin, club.Web)).GetProperty("applicants").EnumerateArray().Select(a => a.GetProperty("fullName").GetString()));
    }

    [Fact]
    public async Task Bulk_marking_changes_only_applications_in_preselection_of_the_leaders_sections()
    {
        var club = await ClubWithApplicantsAsync();
        var ids = (await ListAsync(club.AiLeader, club.Ai)).GetProperty("applicants").EnumerateArray().Select(a => a.GetProperty("applicationId").GetInt32()).ToArray();
        var webIds = (await ListAsync(club.Admin, club.Web)).GetProperty("applicants").EnumerateArray().Select(a => a.GetProperty("applicationId").GetInt32()).ToArray();

        Assert.Equal(HttpStatusCode.Forbidden, (await club.AiLeader.PostAsJsonAsync("/api/preselection", new { applicationIds = ids.Append(webIds[0]), status = "Preselected" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await club.AiLeader.PostAsJsonAsync("/api/preselection", new { applicationIds = ids, status = "Accepted" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await club.AiLeader.PostAsJsonAsync("/api/preselection", new { applicationIds = new[] { 999999 }, status = "Preselected" })).StatusCode);

        // One AI application already invited – left alone.
        await App.DbAsync(db => db.Applications.Where(a => a.Id == ids[0]).ExecuteUpdateAsync(a => a.SetProperty(x => x.Status, ApplicationStatus.InterviewInvited)));
        var result = await TestApp.Json(club.AiLeader.PostAsJsonAsync("/api/preselection", new { applicationIds = ids, status = "Preselected" }));
        Assert.Equal((1, 1), (result.GetProperty("updated").GetInt32(), result.GetProperty("skipped").GetInt32()));
        await TestApp.Json(club.Admin.PostAsJsonAsync("/api/preselection", new { applicationIds = webIds, status = "NotPreselected" }));

        var statuses = await App.DbAsync(db => db.Applications.AsNoTracking().ToDictionaryAsync(a => a.Id, a => a.Status));
        Assert.Equal(ApplicationStatus.InterviewInvited, statuses[ids[0]]);
        Assert.Equal(ApplicationStatus.Preselected, statuses[ids[1]]);
        Assert.All(webIds, id => Assert.Equal(ApplicationStatus.NotPreselected, statuses[id]));
    }

    [Fact]
    public async Task Applications_in_preselection_cant_be_voted_on()
    {
        var club = await ClubWithApplicantsAsync();
        var id = (await ListAsync(club.AiLeader, club.Ai)).GetProperty("applicants")[0].GetProperty("applicationId").GetInt32();

        Assert.Equal(HttpStatusCode.Conflict, (await club.AiLeader.PostAsJsonAsync($"/api/applications/{id}/votes", new { decision = "Approve" })).StatusCode);
        var row = (await club.AiLeader.GetFromJsonAsync<JsonElement>($"/api/recruits?sectionId={club.Ai}")).EnumerateArray().First();
        Assert.False(row.GetProperty("applications")[0].GetProperty("canVote").GetBoolean());
        Assert.Equal(ApplicationStatus.Submitted, await App.DbAsync(db => db.Applications.Where(a => a.Id == id).Select(a => a.Status).SingleAsync()));
    }
}
