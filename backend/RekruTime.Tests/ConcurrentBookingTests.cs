using System.Net;
using System.Net.Http.Json;
using static RekruTime.Tests.TestApp;

namespace RekruTime.Tests;

// The DB constraints are what stop double bookings – checked against the real app and Postgres.
public class ConcurrentBookingTests : IntegrationTest
{
    [Fact]
    public async Task Same_slot_booked_by_two_recruits_at_once_only_one_wins()
    {
        var (links, start) = await Booking.SetupAsync(App, linkCount: 2);

        var results = await Task.WhenAll(links.Select((token, n) => Booking.BookAsync(App, token, start, $"r{n}@test.local")));

        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.Conflict], results.Order());
    }

    [Fact]
    public async Task Same_link_used_twice_at_once_books_only_one_interview()
    {
        var (links, start) = await Booking.SetupAsync(App, linkCount: 1);
        var later = (await Booking.SlotsAsync(App, links[0])).First(s => s >= start.AddHours(1)); // a different, non-overlapping time

        var results = await Task.WhenAll(Booking.BookAsync(App, links[0], start, "a@test.local"), Booking.BookAsync(App, links[0], later, "a@test.local"));

        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.Conflict], results.Order());
    }

    [Fact]
    public async Task Interview_marked_done_early_still_blocks_its_time()
    {
        var (links, start) = await Booking.SetupAsync(App, linkCount: 2, buffer: 10);
        Assert.Equal(HttpStatusCode.OK, await Booking.BookAsync(App, links[0], start, "a@test.local"));

        var admin = await App.AdminAsync();
        var interview = (await Json(admin.GetAsync("/api/interviews"))).EnumerateArray().Single();
        await Json(admin.PatchAsJsonAsync($"/api/interviews/{interview.GetProperty("id").GetInt32()}", new { status = "Done" }));

        // The DB keeps the room blocked for Done interviews – the slot list must agree, or booking it always fails with 409.
        var blockedUntil = start.AddMinutes(40); // 30 min + 10 min buffer
        Assert.DoesNotContain(await Booking.SlotsAsync(App, links[1]), s => s < blockedUntil && s.AddMinutes(40) > start);
    }
}

// A bookable setup through the API: section, two recruiters free 10:00–14:00 UTC three days from now
// (beyond the default 12 h notice), the recruitment period around it, N anonymous links.
public static class Booking
{
    public record Setup(List<string> Links, DateTime FirstSlot, int SectionId, HttpClient Admin, TestApp.User[] Recruiters);

    public static async Task<(List<string> Links, DateTime FirstSlot)> SetupAsync(TestApp app, int linkCount, int buffer = 0)
    {
        var s = await FullSetupAsync(app, linkCount, buffer);
        return (s.Links, s.FirstSlot);
    }

    public static async Task<Setup> FullSetupAsync(TestApp app, int linkCount, int buffer = 0, string[]? names = null)
    {
        var admin = await app.AdminAsync();
        var section = await app.SectionAsync(admin, "AI", buffer: buffer);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        await app.SetPeriodAsync(admin, today, today.AddDays(10));

        var day = DateTime.UtcNow.Date.AddDays(3);
        var recruiters = new List<TestApp.User>();
        foreach (var name in names ?? ["Ala", "Ola"])
        {
            var user = await app.UserAsync(admin, name, [section]);
            await Json(user.Client.PostAsJsonAsync("/api/availability", new { start = day.AddHours(10), end = day.AddHours(14) }));
            recruiters.Add(user);
        }

        var links = await Json(admin.PostAsJsonAsync("/api/booking-links", new { sectionIds = new[] { section }, count = linkCount }));
        var tokens = links.EnumerateArray().Select(l => l.GetProperty("token").GetString()!).ToList();
        return new(tokens, (await SlotsAsync(app, tokens[0])).First(), section, admin, [.. recruiters]);
    }

    public static async Task<List<DateTime>> SlotsAsync(TestApp app, string token) =>
        (await Json(app.CreateClient().GetAsync($"/api/public/links/{token}/slots"))).EnumerateArray()
            .Select(s => s.GetProperty("start").GetDateTime().ToUniversalTime()).ToList();

    public static async Task<HttpStatusCode> BookAsync(TestApp app, string token, DateTime start, string email, string name = "Test Recruit", bool consent = true) =>
        (await app.CreateClient().PostAsJsonAsync($"/api/public/links/{token}/bookings", new { start, fullName = name, email, gdprConsent = consent })).StatusCode;
}
