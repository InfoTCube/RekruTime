using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Testcontainers.PostgreSql;

namespace RekruTime.Tests;

// Admin marks a user as leader of some of their sections; leading a section you're not in is ignored; /me shows it.
public class SectionLeaderTests : IAsyncLifetime
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
            .UseSetting("Email:Host", "")); // no e-mail (overrides appsettings.Development) → temporary passwords
    }

    public async Task DisposeAsync()
    {
        await _app.DisposeAsync();
        await _db.DisposeAsync();
    }

    static int[] Ids(JsonElement user, string prop) => user.GetProperty(prop).EnumerateArray().Select(e => e.GetInt32()).Order().ToArray();

    [Fact]
    public async Task Leader_sections_are_saved_and_shown_in_me()
    {
        var admin = _app.CreateClient();
        await admin.PostAsJsonAsync("/api/auth/login", new { email = "admin@test.local", password = "Admin123!" });
        async Task<int> Section(string name) =>
            (await (await admin.PostAsJsonAsync("/api/sections", new { name })).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
        int ai = await Section("AI"), web = await Section("Web"), bio = await Section("Bio");

        // bio isn't one of their sections → not a leader of it
        var created = await (await admin.PostAsJsonAsync("/api/users", new
        {
            email = "lead@test.local", displayName = "Lead", role = "Recruiter", sectionIds = new[] { ai, web }, leaderSectionIds = new[] { ai, bio },
        })).Content.ReadFromJsonAsync<JsonElement>();
        var user = created.GetProperty("user");
        Assert.Equal([ai, web], Ids(user, "sectionIds"));
        Assert.Equal([ai], Ids(user, "leaderSectionIds"));

        var id = user.GetProperty("id").GetString()!;
        var updated = await (await admin.PutAsJsonAsync($"/api/users/{id}", new
        {
            email = "lead@test.local", displayName = "Lead", role = "Recruiter", sectionIds = new[] { ai, web }, leaderSectionIds = new[] { web },
        })).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal([web], Ids(updated, "leaderSectionIds"));

        var listed = (await admin.GetFromJsonAsync<JsonElement>("/api/users")).EnumerateArray().Single(u => u.GetProperty("id").GetString() == id);
        Assert.Equal([web], Ids(listed, "leaderSectionIds"));

        var lead = _app.CreateClient();
        await lead.PostAsJsonAsync("/api/auth/login", new { email = "lead@test.local", password = created.GetProperty("temporaryPassword").GetString() });
        var me = await lead.GetFromJsonAsync<JsonElement>("/api/auth/me");
        var leads = me.GetProperty("sections").EnumerateArray().ToDictionary(s => s.GetProperty("name").GetString()!, s => s.GetProperty("isLeader").GetBoolean());
        Assert.Equal(new Dictionary<string, bool> { ["AI"] = false, ["Web"] = true }, leads);
    }
}
