using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RekruTime.Api.Data;
using Testcontainers.PostgreSql;

namespace RekruTime.Tests;

// Interviewers write their own note; leaders of the interview's sections and admins only read; others get 403.
public class InterviewNoteTests : IAsyncLifetime
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

    [Fact]
    public async Task Only_interviewers_write_and_leaders_and_admins_read()
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
        var (interviewerId, interviewer) = await User("Ina", ai, leader: false);
        var (_, aiLeader) = await User("Ala", ai, leader: true);
        var (_, webLeader) = await User("Wera", web, leader: true);
        var (_, outsider) = await User("Ola", ai, leader: false);

        int interviewId;
        using (var scope = _app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var start = DateTime.UtcNow.AddDays(-1); // past, so the interviewer can be deleted at the end
            var interview = new Interview
            {
                Recruit = new Recruit { FullName = "Kasia", Email = "kasia@x.pl" }, Start = start, End = start.AddMinutes(30), BlockedUntil = start.AddMinutes(30),
                Sections = [await db.Sections.SingleAsync(s => s.Id == ai)], Interviewers = [await db.Users.SingleAsync(u => u.Id == interviewerId)],
            };
            db.Interviews.Add(interview);
            await db.SaveChangesAsync();
            interviewId = interview.Id;
        }
        var url = $"/api/interviews/{interviewId}/notes";

        var saved = await interviewer.PutAsJsonAsync(url, new { text = "  Strong in Python.  " });
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        Assert.Equal("Strong in Python.", (await saved.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("text").GetString());
        await interviewer.PutAsJsonAsync(url, new { text = "Strong in Python, weak in math." }); // updates, doesn't add a second note

        var mine = await interviewer.GetFromJsonAsync<JsonElement>(url);
        Assert.True(mine.GetProperty("canWrite").GetBoolean());
        var note = Assert.Single(mine.GetProperty("notes").EnumerateArray());
        Assert.Equal("Strong in Python, weak in math.", note.GetProperty("text").GetString());
        Assert.Equal("Ina", note.GetProperty("authorName").GetString());

        foreach (var reader in new[] { aiLeader, admin })
        {
            var read = await reader.GetFromJsonAsync<JsonElement>(url);
            Assert.False(read.GetProperty("canWrite").GetBoolean());
            Assert.Single(read.GetProperty("notes").EnumerateArray());
            Assert.Equal(HttpStatusCode.Forbidden, (await reader.PutAsJsonAsync(url, new { text = "x" })).StatusCode);
        }
        foreach (var stranger in new[] { webLeader, outsider })
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await stranger.GetAsync(url)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await stranger.GetAsync($"/api/interviews/{interviewId}")).StatusCode);
        }
        foreach (var reader in new[] { interviewer, aiLeader, admin }) // the interview itself: same readers as the notes
            Assert.Equal("Kasia", (await reader.GetFromJsonAsync<JsonElement>($"/api/interviews/{interviewId}")).GetProperty("recruit").GetProperty("fullName").GetString());
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync("/api/interviews/999999/notes")).StatusCode);

        // The note outlives its author's account.
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/users/{interviewerId}")).StatusCode);
        var orphan = Assert.Single((await admin.GetFromJsonAsync<JsonElement>(url)).GetProperty("notes").EnumerateArray());
        Assert.Equal("(deleted user)", orphan.GetProperty("authorName").GetString());
    }

    [Fact]
    public async Task Blank_text_deletes_the_note()
    {
        var admin = await LoginAsync("admin@test.local", "Admin123!");
        int interviewId;
        using (var scope = _app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var start = DateTime.UtcNow.AddDays(1);
            var interview = new Interview
            {
                Recruit = new Recruit { FullName = "Zosia", Email = "zosia@x.pl" }, Start = start, End = start.AddMinutes(30), BlockedUntil = start.AddMinutes(30),
                Interviewers = [await db.Users.SingleAsync(u => u.Email == "admin@test.local")],
            };
            db.Interviews.Add(interview);
            await db.SaveChangesAsync();
            interviewId = interview.Id;
        }
        var url = $"/api/interviews/{interviewId}/notes";

        await admin.PutAsJsonAsync(url, new { text = "Note" });
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsJsonAsync(url, new { text = "   " })).StatusCode);
        Assert.Empty((await admin.GetFromJsonAsync<JsonElement>(url)).GetProperty("notes").EnumerateArray());
    }
}
