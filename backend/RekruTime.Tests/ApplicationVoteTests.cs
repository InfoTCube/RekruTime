using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using RekruTime.Api.Data;

namespace RekruTime.Tests;

// Votes through the API: who may vote, status recomputed and stored, final decision by an uninvolved admin, then locked.
public class ApplicationVoteTests : IntegrationTest
{
    static async Task<(HttpStatusCode Code, string? Status)> Vote(HttpClient c, int appId, string decision, bool isFinal = false, string? comment = null)
    {
        var r = await c.PostAsJsonAsync($"/api/applications/{appId}/votes", new { decision, isFinal, comment });
        return (r.StatusCode, r.IsSuccessStatusCode ? (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString() : null);
    }

    [Fact]
    public async Task Leader_and_admin_decide_and_an_uninvolved_admin_settles_a_dispute()
    {
        var admin = await App.AdminAsync();
        int ai = await App.SectionAsync(admin, "AI"), web = await App.SectionAsync(admin, "Web");
        var aiLeader = (await App.UserAsync(admin, "Ala", [ai], leads: [ai])).Client;
        var webLeader = (await App.UserAsync(admin, "Wera", [web], leads: [web])).Client;
        var member = (await App.UserAsync(admin, "Ola", [ai])).Client;
        var admin2 = (await App.UserAsync(admin, "Adam", [web], role: "Admin")).Client;

        var (appA, appB) = await App.DbAsync(async db =>
        {
            Application a = new() { SectionId = ai }, b = new() { SectionId = ai };
            db.Recruits.AddRange(new Recruit { FullName = "Kasia", Email = "kasia@x.pl", Applications = [a] }, new Recruit { FullName = "Zosia", Email = "zosia@x.pl", Applications = [b] });
            await db.SaveChangesAsync();
            return (a.Id, b.Id);
        });

        // Who may vote
        Assert.Equal(HttpStatusCode.Forbidden, (await Vote(member, appA, "Approve")).Code);
        Assert.Equal(HttpStatusCode.Forbidden, (await Vote(webLeader, appA, "Approve")).Code);
        Assert.Equal(HttpStatusCode.NotFound, (await Vote(admin, 999999, "Approve")).Code);

        // A: leader + admin agree → Accepted; a vote change moves it back
        Assert.Equal((HttpStatusCode.OK, "Pending"), await Vote(aiLeader, appA, "Approve"));
        Assert.Equal((HttpStatusCode.OK, "Accepted"), await Vote(admin, appA, "Approve"));
        Assert.Equal((HttpStatusCode.OK, "Disputed"), await Vote(admin, appA, "Reject"));
        Assert.Equal((HttpStatusCode.OK, "Rejected"), await Vote(aiLeader, appA, "Reject", comment: " Weak on math "));

        // B: split → Disputed; final decision only by an admin who hasn't voted, then everything is locked
        await Vote(aiLeader, appB, "Approve");
        Assert.Equal(HttpStatusCode.Conflict, (await Vote(admin2, appB, "Approve", isFinal: true)).Code); // not disputed yet
        Assert.Equal((HttpStatusCode.OK, "Disputed"), await Vote(admin, appB, "Reject"));
        Assert.Equal(HttpStatusCode.Forbidden, (await Vote(admin, appB, "Approve", isFinal: true)).Code); // already voted
        Assert.Equal(HttpStatusCode.Forbidden, (await Vote(aiLeader, appB, "Approve", isFinal: true)).Code); // not an admin
        Assert.Equal((HttpStatusCode.OK, "Accepted"), await Vote(admin2, appB, "Approve", isFinal: true));
        Assert.Equal(HttpStatusCode.Conflict, (await Vote(admin, appB, "Approve")).Code);

        var votes = (await TestApp.Json(admin2.PostAsJsonAsync($"/api/applications/{appA}/votes", new { decision = "Approve" })))
            .GetProperty("votes").EnumerateArray().ToList();
        Assert.Equal(3, votes.Count); // Ala, the admin, Adam – one per user
        Assert.Equal("Weak on math", votes.Single(v => v.GetProperty("name").GetString() == "Ala").GetProperty("comment").GetString());
    }
}
