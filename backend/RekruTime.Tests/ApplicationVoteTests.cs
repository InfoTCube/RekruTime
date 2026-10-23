using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using RekruTime.Api.Data;
using Testcontainers.PostgreSql;

namespace RekruTime.Tests;

// Votes through the API: who may vote, status recomputed and stored, final decision by an uninvolved admin, then locked.
public class ApplicationVoteTests : IAsyncLifetime
{
    readonly PostgreSqlContainer _db = new PostgreSqlBuilder("postgres:17").Build();
    WebApplicationFactory<Program> _app = null!;

    public async Task InitializeAsync()
    {
        await _db.StartAsync();
        _app = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b
            .UseSetting("ConnectionStrings:Default", _db.GetConnectionString())
            .UseSetting("SeedAdmin:Email", "admin@test.local")
            .UseSetting("SeedAdmin:Password", "Admin123!"));
    }

    public async Task DisposeAsync()
    {
        await _app.DisposeAsync();
        await _db.DisposeAsync();
    }

    async Task<HttpClient> LoginAsync(string email, string password)
    {
        var client = _app.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/auth/login", new { email, password })).StatusCode);
        return client;
    }

    static async Task<(HttpStatusCode Code, string? Status)> Vote(HttpClient c, int appId, string decision, bool isFinal = false, string? comment = null)
    {
        var r = await c.PostAsJsonAsync($"/api/applications/{appId}/votes", new { decision, isFinal, comment });
        return (r.StatusCode, r.IsSuccessStatusCode ? (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString() : null);
    }

    [Fact]
    public async Task Leader_and_admin_decide_and_an_uninvolved_admin_settles_a_dispute()
    {
        var admin = await LoginAsync("admin@test.local", "Admin123!");
        async Task<int> Section(string name) =>
            (await (await admin.PostAsJsonAsync("/api/sections", new { name })).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
        int ai = await Section("AI"), web = await Section("Web");
        async Task<HttpClient> User(string name, string role, int section, bool leader)
        {
            var body = await (await admin.PostAsJsonAsync("/api/users", new
            {
                email = $"{name}@test.local", displayName = name, role, sectionIds = new[] { section }, leaderSectionIds = leader ? new[] { section } : [],
            })).Content.ReadFromJsonAsync<JsonElement>();
            return await LoginAsync($"{name}@test.local", body.GetProperty("temporaryPassword").GetString()!);
        }
        var aiLeader = await User("Ala", "Recruiter", ai, leader: true);
        var webLeader = await User("Wera", "Recruiter", web, leader: true);
        var member = await User("Ola", "Recruiter", ai, leader: false);
        var admin2 = await User("Adam", "Admin", web, leader: false);

        int appA, appB;
        using (var scope = _app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var a = new Application { SectionId = ai };
            var b = new Application { SectionId = ai };
            db.Recruits.AddRange(new Recruit { FullName = "Kasia", Email = "kasia@x.pl", Applications = [a] }, new Recruit { FullName = "Zosia", Email = "zosia@x.pl", Applications = [b] });
            await db.SaveChangesAsync();
            (appA, appB) = (a.Id, b.Id);
        }

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

        var votes = (await (await admin2.PostAsJsonAsync($"/api/applications/{appA}/votes", new { decision = "Approve" })).Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("votes").EnumerateArray().ToList();
        Assert.Equal(3, votes.Count); // Ala, Dev admin, Adam – one per user
        Assert.Equal("Weak on math", votes.Single(v => v.GetProperty("name").GetString() == "Ala").GetProperty("comment").GetString());
    }
}
