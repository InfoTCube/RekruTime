using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using static RekruTime.Tests.TestApp;

namespace RekruTime.Tests;

// The recruit's side: what a link shows, which times are offered, and what a booking needs.
public class PublicBookingTests() : IntegrationTest(new Dictionary<string, string?> { ["Club:Name"] = "Koło Testowe", ["Club:InterviewLocation"] = "Sala 101" })
{
    [Fact]
    public async Task Link_shows_what_the_recruit_needs_and_then_their_booking()
    {
        var setup = await Booking.FullSetupAsync(App, linkCount: 1);
        var named = await Json(setup.Admin.PostAsJsonAsync("/api/booking-links", new
        {
            sectionIds = new[] { setup.SectionId }, recipients = new[] { new { name = "Ewa Nowak", email = "ewa@x.pl" } },
        }));
        var token = named[0].GetProperty("token").GetString()!;
        var pub = App.CreateClient();

        var info = await Json(pub.GetAsync($"/api/public/links/{token}"));
        Assert.Equal(("Koło Testowe", "Sala 101", "Ewa Nowak", 30), (info.GetProperty("clubName").GetString(), info.GetProperty("location").GetString(),
            info.GetProperty("name").GetString(), info.GetProperty("durationMinutes").GetInt32()));
        Assert.Equal(["AI"], info.GetProperty("sections").EnumerateArray().Select(s => s.GetString()));
        Assert.Equal(JsonValueKind.Null, info.GetProperty("booking").ValueKind);

        // Booking with another e-mail, phone and Discord updates the recruit
        Assert.Equal(HttpStatusCode.OK, await Status(pub.PostAsJsonAsync($"/api/public/links/{token}/bookings", new
        {
            start = setup.FirstSlot, fullName = " Ewa Nowak-Kowalska ", email = "EWA.NEW@x.pl", phone = "500 600 700", discordNickname = "@ewa", gdprConsent = true,
        })));
        var recruit = await App.DbAsync(db => db.Recruits.Include(r => r.Applications).SingleAsync());
        Assert.Equal(("Ewa Nowak-Kowalska", "ewa.new@x.pl", "500 600 700", "ewa"), (recruit.FullName, recruit.Email, recruit.Phone, recruit.DiscordNickname));
        Assert.NotNull(recruit.ConsentAt);
        Assert.Single(recruit.Applications); // one application per section of the link

        info = await Json(pub.GetAsync($"/api/public/links/{token}"));
        Assert.Equal(setup.FirstSlot, info.GetProperty("booking").GetProperty("start").GetDateTime().ToUniversalTime());
        Assert.Empty(await Booking.SlotsAsync(App, token)); // booked → nothing more to pick
        Assert.Equal(HttpStatusCode.Conflict, await Booking.BookAsync(App, token, setup.FirstSlot.AddHours(1), "ewa@x.pl"));
    }

    [Fact]
    public async Task Booking_needs_consent_valid_data_and_an_offered_time()
    {
        var setup = await Booking.FullSetupAsync(App, linkCount: 1);
        var token = setup.Links[0];

        Assert.Equal(HttpStatusCode.BadRequest, await Booking.BookAsync(App, token, setup.FirstSlot, "a@x.pl", consent: false));
        Assert.Equal(HttpStatusCode.BadRequest, await Booking.BookAsync(App, token, setup.FirstSlot, "not-an-email"));
        Assert.Equal(HttpStatusCode.BadRequest, await Booking.BookAsync(App, token, setup.FirstSlot, "a@x.pl", name: "A"));
        Assert.Equal(HttpStatusCode.Conflict, await Booking.BookAsync(App, token, setup.FirstSlot.AddMinutes(7), "a@x.pl")); // not a slot
        Assert.Equal(HttpStatusCode.Conflict, await Booking.BookAsync(App, token, setup.FirstSlot.AddDays(1), "a@x.pl")); // nobody free
        Assert.Equal(HttpStatusCode.OK, await Booking.BookAsync(App, token, setup.FirstSlot, "a@x.pl"));
    }

    [Fact]
    public async Task Notice_is_set_by_the_admin()
    {
        var setup = await Booking.FullSetupAsync(App, linkCount: 1); // recruiters free 3 days from now
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        Assert.Equal(12, (await Json(setup.Admin.GetAsync("/api/settings/recruitment"))).GetProperty("minNoticeHours").GetInt32()); // default
        Task<HttpResponseMessage> Notice(int hours) => setup.Admin.PutAsJsonAsync("/api/settings/recruitment",
            new { interviewsFrom = today, interviewsTo = today.AddDays(10), minNoticeHours = hours });

        await Json(Notice(336)); // two weeks ahead → the free day is too soon
        Assert.Empty(await Booking.SlotsAsync(App, setup.Links[0]));
        Assert.Equal(HttpStatusCode.Conflict, await Booking.BookAsync(App, setup.Links[0], setup.FirstSlot, "a@x.pl"));

        Assert.Equal(HttpStatusCode.BadRequest, await Status(Notice(-1)));
        Assert.Equal(HttpStatusCode.BadRequest, await Status(Notice(337)));
        await Json(Notice(0));
        Assert.Equal(setup.FirstSlot, (await Booking.SlotsAsync(App, setup.Links[0])).First());
    }

    [Fact]
    public async Task Times_respect_the_period_and_the_notice()
    {
        var setup = await Booking.FullSetupAsync(App, linkCount: 1);
        var admin = setup.Admin;
        var slots = await Booking.SlotsAsync(App, setup.Links[0]);
        Assert.NotEmpty(slots);
        Assert.All(slots, s => Assert.True(s >= DateTime.UtcNow.AddHours(12))); // 12 h notice

        // A period that ends before the recruiters' free day → nothing; closed → nothing; just that day → the same times again
        var day = DateOnly.FromDateTime(setup.FirstSlot);
        await App.SetPeriodAsync(admin, day.AddDays(-3), day.AddDays(-1));
        Assert.Empty(await Booking.SlotsAsync(App, setup.Links[0]));
        await Json(admin.PutAsJsonAsync("/api/settings/recruitment", new { })); // closed
        Assert.Empty(await Booking.SlotsAsync(App, setup.Links[0]));
        await App.SetPeriodAsync(admin, day, day);
        Assert.Equal(slots, await Booking.SlotsAsync(App, setup.Links[0]));
    }

    [Fact]
    public async Task Times_next_to_a_booked_interview_are_suggested_and_go_to_the_same_team()
    {
        var setup = await Booking.FullSetupAsync(App, linkCount: 2, names: ["Ala", "Ola", "Ela"]); // all free 10–14
        Assert.Equal(HttpStatusCode.OK, await Booking.BookAsync(App, setup.Links[0], setup.FirstSlot, "a@x.pl"));
        var firstTeam = await App.DbAsync(db => db.Interviews.SelectMany(i => i.Interviewers.Select(u => u.Id)).OrderBy(id => id).ToListAsync());

        var slots = (await Json(App.CreateClient().GetAsync($"/api/public/links/{setup.Links[1]}/slots"))).EnumerateArray()
            .ToDictionary(x => x.GetProperty("start").GetDateTime().ToUniversalTime(), x => x.GetProperty("suggested").GetBoolean());
        var next = setup.FirstSlot.AddMinutes(30);
        Assert.True(slots[next]);
        Assert.False(slots[setup.FirstSlot.AddHours(2)]);

        // The third recruiter has fewer interviews, but the two already there get it – one trip for them.
        Assert.Equal(HttpStatusCode.OK, await Booking.BookAsync(App, setup.Links[1], next, "b@x.pl"));
        var secondTeam = await App.DbAsync(db => db.Interviews.Where(i => i.Start == next).SelectMany(i => i.Interviewers.Select(u => u.Id)).OrderBy(id => id).ToListAsync());
        Assert.Equal(firstTeam, secondTeam);
    }
}
