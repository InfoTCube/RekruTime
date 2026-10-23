using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using static RekruTime.Tests.TestApp;

namespace RekruTime.Tests;

// Admin side of booking links: creating, statuses, deactivating, expiry, invitations, coverage preview.
public class BookingLinkTests() : IntegrationTest(QueuedEmail)
{
    static async Task<JsonElement> LinkAsync(HttpClient admin, int id) =>
        (await Json(admin.GetAsync("/api/booking-links"))).EnumerateArray().Single(l => l.GetProperty("id").GetInt32() == id);

    [Fact]
    public async Task Create_validates_and_matches_recruits_by_email()
    {
        var admin = await App.AdminAsync();
        var ai = await App.SectionAsync(admin, "AI");
        object Req(object? recipients = null, int? count = null, int[]? sections = null) => new { sectionIds = sections ?? [ai], recipients, count };

        Assert.Equal(HttpStatusCode.BadRequest, await Status(admin.PostAsJsonAsync("/api/booking-links", Req()))); // neither
        Assert.Equal(HttpStatusCode.BadRequest, await Status(admin.PostAsJsonAsync("/api/booking-links", Req(new[] { new { name = "A", email = "a@x.pl" } }, 1)))); // both
        Assert.Equal(HttpStatusCode.BadRequest, await Status(admin.PostAsJsonAsync("/api/booking-links", Req(new[] { new { name = "A", email = "nope" } }))));
        Assert.Equal(HttpStatusCode.BadRequest, await Status(admin.PostAsJsonAsync("/api/booking-links", Req(count: 1, sections: [999999]))));

        var anonymous = await Json(admin.PostAsJsonAsync("/api/booking-links", Req(count: 3)));
        Assert.Equal(3, anonymous.GetArrayLength());
        Assert.All(anonymous.EnumerateArray(), l => Assert.Equal(JsonValueKind.Null, l.GetProperty("recruitEmail").ValueKind));
        Assert.Equal(3, anonymous.EnumerateArray().Select(l => l.GetProperty("token").GetString()).Distinct().Count());

        var named = await Json(admin.PostAsJsonAsync("/api/booking-links", Req(new[] { new { name = "Zofia Nowak", email = " Zofia@X.pl" } })));
        Assert.Equal(("Zofia Nowak", "zofia@x.pl", "Unused"),
            (named[0].GetProperty("label").GetString(), named[0].GetProperty("recruitEmail").GetString(), named[0].GetProperty("status").GetString()));
        await Json(admin.PostAsJsonAsync("/api/booking-links", Req(new[] { new { name = "Zofia", email = "zofia@x.pl" } }))); // second link, same recruit
        Assert.Equal(1, await App.DbAsync(db => db.Recruits.CountAsync()));
    }

    [Fact]
    public async Task Inactive_and_expired_links_stop_working()
    {
        var admin = await App.AdminAsync();
        var ai = await App.SectionAsync(admin, "AI");
        var links = await Json(admin.PostAsJsonAsync("/api/booking-links", new { sectionIds = new[] { ai }, count = 1 }));
        var (id, token) = (links[0].GetProperty("id").GetInt32(), links[0].GetProperty("token").GetString()!);
        var expired = await Json(admin.PostAsJsonAsync("/api/booking-links", new { sectionIds = new[] { ai }, count = 1, expiresAt = DateTime.UtcNow.AddMinutes(-1) }));

        var pub = App.CreateClient();
        Assert.Equal(HttpStatusCode.OK, await Status(pub.GetAsync($"/api/public/links/{token}")));
        await Json(admin.PatchAsJsonAsync($"/api/booking-links/{id}", new { isActive = false }));
        Assert.Equal("Inactive", (await LinkAsync(admin, id)).GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.NotFound, await Status(pub.GetAsync($"/api/public/links/{token}"))); // same as an unknown token
        Assert.Equal(HttpStatusCode.NotFound, await Status(pub.GetAsync("/api/public/links/does-not-exist")));
        await Json(admin.PatchAsJsonAsync($"/api/booking-links/{id}", new { isActive = true }));
        Assert.Equal(HttpStatusCode.OK, await Status(pub.GetAsync($"/api/public/links/{token}")));

        Assert.Equal("Expired", (await LinkAsync(admin, expired[0].GetProperty("id").GetInt32())).GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.Gone, await Status(pub.GetAsync($"/api/public/links/{expired[0].GetProperty("token").GetString()}")));
    }

    [Fact]
    public async Task Invitations_go_only_to_usable_links_with_a_recruit()
    {
        var setup = await Booking.FullSetupAsync(App, linkCount: 1); // one anonymous link
        var admin = setup.Admin;
        var named = await Json(admin.PostAsJsonAsync("/api/booking-links", new
        {
            sectionIds = new[] { setup.SectionId },
            recipients = new[] { new { name = "Ewa", email = "ewa@x.pl" }, new { name = "Piotr", email = "piotr@x.pl" }, new { name = "Kuba", email = "kuba@x.pl" } },
        }));
        int ewa = named[0].GetProperty("id").GetInt32(), piotr = named[1].GetProperty("id").GetInt32(), kuba = named[2].GetProperty("id").GetInt32();
        var anonymous = (await Json(admin.GetAsync("/api/booking-links"))).EnumerateArray().Single(l => l.GetProperty("recruitEmail").ValueKind == JsonValueKind.Null).GetProperty("id").GetInt32();
        await Json(admin.PatchAsJsonAsync($"/api/booking-links/{kuba}", new { isActive = false }));
        Assert.Equal(HttpStatusCode.OK, await Booking.BookAsync(App, named[1].GetProperty("token").GetString()!, setup.FirstSlot, "piotr@x.pl", "Piotr"));

        var last = await App.LastMailIdAsync();
        var result = await Json(admin.PostAsJsonAsync("/api/booking-links/invite", new { linkIds = new[] { ewa, piotr, kuba, anonymous } }));
        Assert.Equal((1, 3), (result.GetProperty("queued").GetInt32(), result.GetProperty("skipped").GetInt32())); // booked, inactive, no recruit
        var mail = Assert.Single(await App.MailsAsync(last));
        Assert.Equal("ewa@x.pl", mail.To);
        Assert.Contains($"{PublicUrl}/book/{named[0].GetProperty("token").GetString()}", mail.Body);
        Assert.Equal("Queued", (await LinkAsync(admin, ewa)).GetProperty("invitation").GetProperty("state").GetString());
        Assert.Equal("Booked", (await LinkAsync(admin, piotr)).GetProperty("status").GetString());

        // The admin's preview of free times names the interviewers (the public one doesn't)
        var day = setup.FirstSlot.Date;
        var preview = await Json(admin.GetAsync($"/api/booking-links/{ewa}/slots?from={day:O}&to={day.AddDays(1):O}"));
        Assert.Contains(preview.EnumerateArray(), s => s.GetProperty("team").EnumerateArray().Select(t => t.GetString()).Order().SequenceEqual(["Ala", "Ola"]));
        Assert.All((await Json(App.CreateClient().GetAsync($"/api/public/links/{named[0].GetProperty("token").GetString()}/slots"))).EnumerateArray(),
            s => Assert.False(s.TryGetProperty("team", out _)));
    }

    [Fact]
    public async Task Invitations_need_email_set_up()
    {
        await using var noEmail = new TestApp();
        await noEmail.StartAsync();
        var admin = await noEmail.AdminAsync();
        var ai = await noEmail.SectionAsync(admin, "AI");
        var link = await Json(admin.PostAsJsonAsync("/api/booking-links", new { sectionIds = new[] { ai }, recipients = new[] { new { name = "Ewa", email = "ewa@x.pl" } } }));
        var r = await admin.PostAsJsonAsync("/api/booking-links/invite", new { linkIds = new[] { link[0].GetProperty("id").GetInt32() } });
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Contains("E-mail isn't configured", await r.Content.ReadAsStringAsync());
    }
}
