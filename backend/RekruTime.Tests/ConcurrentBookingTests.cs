using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Testcontainers.PostgreSql;

namespace RekruTime.Tests;

// The real app against a throwaway Postgres (needs Docker): the DB constraints are what stop double bookings.
public class ConcurrentBookingTests : IAsyncLifetime
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

    [Fact]
    public async Task Same_slot_booked_by_two_recruits_at_once_only_one_wins()
    {
        var (links, start) = await Setup(linkCount: 2);

        var results = await Task.WhenAll(links.Select((token, n) => Book(token, start, $"r{n}@test.local")));

        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.Conflict], results.Order());
    }

    [Fact]
    public async Task Same_link_used_twice_at_once_books_only_one_interview()
    {
        var (links, start) = await Setup(linkCount: 1);
        var slots = await Slots(links[0]);
        var later = slots.First(s => s >= start.AddHours(1)); // a different, non-overlapping time

        var results = await Task.WhenAll(Book(links[0], start, "a@test.local"), Book(links[0], later, "a@test.local"));

        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.Conflict], results.Order());
    }

    // Section + 2 recruiters free on a day 3 days from now (beyond the 12 h notice), recruitment period around it, N links.
    async Task<(List<string> Links, DateTime FirstSlot)> Setup(int linkCount)
    {
        var admin = _app.CreateClient();
        await Ok(admin.PostAsJsonAsync("/api/auth/login", new { email = "admin@test.local", password = "Admin123!" }));
        var section = await Json(admin.PostAsJsonAsync("/api/sections", new { name = "AI", interviewDurationMinutes = 30, bufferMinutes = 10 }));
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        await Ok(admin.PutAsJsonAsync("/api/settings/recruitment", new { interviewsFrom = today, interviewsTo = today.AddDays(10) }));

        var day = DateTime.UtcNow.Date.AddDays(3);
        foreach (var name in new[] { "a", "b" })
        {
            var user = await Json(admin.PostAsJsonAsync("/api/users", new { email = $"{name}@club.local", displayName = name, role = "Recruiter", sectionIds = new[] { section.GetProperty("id").GetInt32() } }));
            var recruiter = _app.CreateClient();
            await Ok(recruiter.PostAsJsonAsync("/api/auth/login", new { email = $"{name}@club.local", password = user.GetProperty("temporaryPassword").GetString() }));
            await Ok(recruiter.PostAsJsonAsync("/api/availability", new { start = day.AddHours(10), end = day.AddHours(14) }));
        }

        var links = await Json(admin.PostAsJsonAsync("/api/booking-links", new { sectionIds = new[] { section.GetProperty("id").GetInt32() }, count = linkCount }));
        var tokens = links.EnumerateArray().Select(l => l.GetProperty("token").GetString()!).ToList();
        return (tokens, (await Slots(tokens[0])).First());
    }

    async Task<List<DateTime>> Slots(string token) =>
        (await Json(_app.CreateClient().GetAsync($"/api/public/links/{token}/slots"))).EnumerateArray()
            .Select(s => s.GetProperty("start").GetDateTime().ToUniversalTime()).ToList();

    async Task<HttpStatusCode> Book(string token, DateTime start, string email) =>
        (await _app.CreateClient().PostAsJsonAsync($"/api/public/links/{token}/bookings",
            new { start, fullName = "Test Recruit", email, gdprConsent = true })).StatusCode;

    static async Task Ok(Task<HttpResponseMessage> call) => (await call).EnsureSuccessStatusCode();

    static async Task<JsonElement> Json(Task<HttpResponseMessage> call)
    {
        var res = (await call).EnsureSuccessStatusCode();
        return await res.Content.ReadFromJsonAsync<JsonElement>();
    }
}
