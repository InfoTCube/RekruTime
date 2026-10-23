using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using RekruTime.Api.Data;

namespace RekruTime.Tests;

// Recruits list/detail: admins see all, leaders only applications to sections they lead, others 403; vote flags; notes included.
public class RecruitReviewTests : IntegrationTest
{
    static List<string> Sections(JsonElement row) => row.GetProperty("applications").EnumerateArray().Select(a => a.GetProperty("sectionName").GetString()!).ToList();

    [Fact]
    public async Task Admins_see_all_leaders_their_sections()
    {
        var admin = await App.AdminAsync();
        int ai = await App.SectionAsync(admin, "AI"), web = await App.SectionAsync(admin, "Web");
        var aiLeader = (await App.UserAsync(admin, "Ala", [ai], leads: [ai])).Client;
        var ina = await App.UserAsync(admin, "Ina", [ai]);

        var (kasia, zosia, kasiaAi) = await App.DbAsync(async db =>
        {
            var k = new Recruit { FullName = "Kasia", Email = "kasia@x.pl", Applications = [new() { SectionId = ai }, new() { SectionId = web }] };
            var z = new Recruit { FullName = "Zosia", Email = "zosia@x.pl", Applications = [new() { SectionId = web }] };
            db.Recruits.AddRange(k, z);
            await db.SaveChangesAsync();
            return (k.Id, z.Id, k.Applications.Single(a => a.SectionId == ai).Id);
        });
        var interviewId = await App.InterviewAsync("Kasia", DateTime.UtcNow.AddDays(-1), [ai, web], [ina.Id], InterviewStatus.Done);
        await ina.Client.PutAsJsonAsync($"/api/interviews/{interviewId}/notes", new { text = "Solid." });

        // List
        var all = await admin.GetFromJsonAsync<JsonElement>("/api/recruits");
        Assert.Equal(["Kasia", "Zosia"], all.EnumerateArray().Select(r => r.GetProperty("fullName").GetString()));
        Assert.Equal(["AI", "Web"], Sections(all[0]));
        Assert.Equal(["Ina"], all[0].GetProperty("interview").GetProperty("interviewers").EnumerateArray().Select(e => e.GetString()));

        var mine = await aiLeader.GetFromJsonAsync<JsonElement>("/api/recruits");
        var row = Assert.Single(mine.EnumerateArray());
        Assert.Equal(["AI"], Sections(row)); // not the Web application
        Assert.True(row.GetProperty("applications")[0].GetProperty("canVote").GetBoolean());

        Assert.Equal(HttpStatusCode.Forbidden, (await ina.Client.GetAsync("/api/recruits")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await ina.Client.GetAsync($"/api/recruits/{kasia}")).StatusCode);

        // Filters
        Assert.Equal(new[] { "Kasia", "Zosia" }, (await admin.GetFromJsonAsync<JsonElement>($"/api/recruits?sectionId={web}")).EnumerateArray().Select(r => r.GetProperty("fullName").GetString()).Order());
        await aiLeader.PostAsJsonAsync($"/api/applications/{kasiaAi}/votes", new { decision = "Approve" });
        await admin.PostAsJsonAsync($"/api/applications/{kasiaAi}/votes", new { decision = "Reject" });
        var disputed = await admin.GetFromJsonAsync<JsonElement>("/api/recruits?status=Disputed");
        Assert.Equal(["AI"], Sections(Assert.Single(disputed.EnumerateArray())));
        Assert.False(disputed[0].GetProperty("applications")[0].GetProperty("canDecide").GetBoolean()); // admin already voted

        // Detail
        var detail = await aiLeader.GetFromJsonAsync<JsonElement>($"/api/recruits/{kasia}");
        Assert.Equal("kasia@x.pl", detail.GetProperty("recruit").GetProperty("email").GetString());
        Assert.Equal(["AI"], Sections(detail));
        Assert.Equal(2, detail.GetProperty("applications")[0].GetProperty("votes").GetArrayLength());
        var note = detail.GetProperty("interviews")[0].GetProperty("notes").GetProperty("notes")[0];
        Assert.Equal("Solid.", note.GetProperty("text").GetString());
        Assert.Equal(HttpStatusCode.NotFound, (await aiLeader.GetAsync($"/api/recruits/{zosia}")).StatusCode); // Web only
        Assert.Equal(["Web"], Sections(await admin.GetFromJsonAsync<JsonElement>($"/api/recruits/{zosia}")));
    }
}
