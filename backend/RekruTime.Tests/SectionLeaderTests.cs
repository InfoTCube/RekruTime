using System.Net.Http.Json;
using System.Text.Json;
using static RekruTime.Tests.TestApp;

namespace RekruTime.Tests;

// Admin marks a user as leader of some of their sections; leading a section you're not in is ignored; /me shows it.
public class SectionLeaderTests : IntegrationTest
{
    static int[] Ids(JsonElement user, string prop) => user.GetProperty(prop).EnumerateArray().Select(e => e.GetInt32()).Order().ToArray();

    [Fact]
    public async Task Leader_sections_are_saved_and_shown_in_me()
    {
        var admin = await App.AdminAsync();
        int ai = await App.SectionAsync(admin, "AI"), web = await App.SectionAsync(admin, "Web"), bio = await App.SectionAsync(admin, "Bio");

        // bio isn't one of their sections → not a leader of it
        var created = await Json(admin.PostAsJsonAsync("/api/users", new
        {
            email = "lead@test.local", displayName = "Lead", role = "Recruiter", sectionIds = new[] { ai, web }, leaderSectionIds = new[] { ai, bio },
        }));
        var user = created.GetProperty("user");
        Assert.Equal([ai, web], Ids(user, "sectionIds"));
        Assert.Equal([ai], Ids(user, "leaderSectionIds"));

        var id = user.GetProperty("id").GetString()!;
        var updated = await Json(admin.PutAsJsonAsync($"/api/users/{id}", new
        {
            email = "lead@test.local", displayName = "Lead", role = "Recruiter", sectionIds = new[] { ai, web }, leaderSectionIds = new[] { web },
        }));
        Assert.Equal([web], Ids(updated, "leaderSectionIds"));

        var listed = (await admin.GetFromJsonAsync<JsonElement>("/api/users")).EnumerateArray().Single(u => u.GetProperty("id").GetString() == id);
        Assert.Equal([web], Ids(listed, "leaderSectionIds"));

        var lead = await App.LoginAsync("lead@test.local", created.GetProperty("temporaryPassword").GetString()!);
        var me = await lead.GetFromJsonAsync<JsonElement>("/api/auth/me");
        var leads = me.GetProperty("sections").EnumerateArray().ToDictionary(s => s.GetProperty("name").GetString()!, s => s.GetProperty("isLeader").GetBoolean());
        Assert.Equal(new Dictionary<string, bool> { ["AI"] = false, ["Web"] = true }, leads);
    }
}
