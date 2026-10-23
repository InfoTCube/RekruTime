using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RekruTime.Api.Data;
using Testcontainers.PostgreSql;

namespace RekruTime.Tests;

// Recruits list/detail: admins see all, leaders only applications to sections they lead, others 403; vote flags; notes included.
public class RecruitReviewTests : IAsyncLifetime
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

    async Task<HttpClient> LoginAsync(string email, string password)
    {
        var client = _app.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/auth/login", new { email, password })).StatusCode);
        return client;
    }

    static List<string> Sections(JsonElement row) => row.GetProperty("applications").EnumerateArray().Select(a => a.GetProperty("sectionName").GetString()!).ToList();

    [Fact]
    public async Task Admins_see_all_leaders_their_sections()
    {
        var admin = await LoginAsync("admin@test.local", "Admin123!");
        async Task<int> Section(string name) =>
            (await (await admin.PostAsJsonAsync("/api/sections", new { name })).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
        int ai = await Section("AI"), web = await Section("Web");
        async Task<(string Id, HttpClient Client)> User(string name, int section, bool leader)
        {
            var body = await (await admin.PostAsJsonAsync("/api/users", new
            {
                email = $"{name}@test.local", displayName = name, role = "Recruiter", sectionIds = new[] { section }, leaderSectionIds = leader ? new[] { section } : [],
            })).Content.ReadFromJsonAsync<JsonElement>();
            return (body.GetProperty("user").GetProperty("id").GetString()!, await LoginAsync($"{name}@test.local", body.GetProperty("temporaryPassword").GetString()!));
        }
        var (_, aiLeader) = await User("Ala", ai, leader: true);
        var (inaId, ina) = await User("Ina", ai, leader: false);

        int kasia, zosia, kasiaAi;
        using (var scope = _app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var start = DateTime.UtcNow.AddDays(-1);
            var k = new Recruit { FullName = "Kasia", Email = "kasia@x.pl", Applications = [new() { SectionId = ai }, new() { SectionId = web }] };
            var z = new Recruit { FullName = "Zosia", Email = "zosia@x.pl", Applications = [new() { SectionId = web }] };
            db.Recruits.AddRange(k, z);
            db.Interviews.Add(new Interview
            {
                Recruit = k, Start = start, End = start.AddMinutes(30), BlockedUntil = start.AddMinutes(30), Status = InterviewStatus.Done,
                Sections = await db.Sections.ToListAsync(), Interviewers = [await db.Users.SingleAsync(u => u.Id == inaId)],
            });
            await db.SaveChangesAsync();
            (kasia, zosia, kasiaAi) = (k.Id, z.Id, k.Applications.Single(a => a.SectionId == ai).Id);
        }
        var interviewId = (await admin.GetFromJsonAsync<JsonElement>("/api/interviews"))[0].GetProperty("id").GetInt32();
        await ina.PutAsJsonAsync($"/api/interviews/{interviewId}/notes", new { text = "Solid." });

        // List
        var all = await admin.GetFromJsonAsync<JsonElement>("/api/recruits");
        Assert.Equal(["Kasia", "Zosia"], all.EnumerateArray().Select(r => r.GetProperty("fullName").GetString()));
        Assert.Equal(["AI", "Web"], Sections(all[0]));
        Assert.Equal(["Ina"], all[0].GetProperty("interview").GetProperty("interviewers").EnumerateArray().Select(e => e.GetString()));

        var mine = await aiLeader.GetFromJsonAsync<JsonElement>("/api/recruits");
        var row = Assert.Single(mine.EnumerateArray());
        Assert.Equal(["AI"], Sections(row)); // not the Web application
        Assert.True(row.GetProperty("applications")[0].GetProperty("canVote").GetBoolean());

        Assert.Equal(HttpStatusCode.Forbidden, (await ina.GetAsync("/api/recruits")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await ina.GetAsync($"/api/recruits/{kasia}")).StatusCode);

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
