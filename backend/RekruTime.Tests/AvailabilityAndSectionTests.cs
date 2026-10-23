using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using RekruTime.Api.Data;
using static RekruTime.Tests.TestApp;

namespace RekruTime.Tests;

public class AvailabilityTests : IntegrationTest
{
    static readonly DateTime Day = DateTime.UtcNow.Date.AddDays(5);
    static DateTime At(int h) => Day.AddHours(h);

    static async Task<List<(int, int)>> Blocks(HttpClient c) =>
        (await Json(c.GetAsync($"/api/availability?from={Day:O}&to={Day.AddDays(1):O}"))).EnumerateArray()
            .Select(b => (b.GetProperty("start").GetDateTime().ToUniversalTime().Hour, b.GetProperty("end").GetDateTime().ToUniversalTime().Hour)).ToList();

    static Task<HttpResponseMessage> Add(HttpClient c, int from, int to) => c.PostAsJsonAsync("/api/availability", new { start = At(from), end = At(to) });
    static Task<HttpResponseMessage> Remove(HttpClient c, int from, int to) => c.DeleteAsync($"/api/availability?start={At(from):O}&end={At(to):O}");

    [Fact]
    public async Task Ranges_merge_split_and_stay_private()
    {
        var admin = await App.AdminAsync();
        var me = await App.UserAsync(admin, "Ala");
        var other = await App.UserAsync(admin, "Ola");

        await Json(Add(me.Client, 10, 12));
        await Json(Add(me.Client, 12, 14)); // adjacent → one block
        await Json(Add(me.Client, 13, 15)); // overlapping → still one
        Assert.Equal([(10, 15)], await Blocks(me.Client));

        Assert.Equal(HttpStatusCode.NoContent, await Status(Remove(me.Client, 11, 12))); // a piece out of the middle → split
        Assert.Equal([(10, 11), (12, 15)], await Blocks(me.Client));
        Assert.Empty(await Blocks(other.Client)); // everyone sees only their own
    }

    [Fact]
    public async Task Invalid_ranges_are_rejected()
    {
        var me = await App.AdminAsync();
        Assert.Equal(HttpStatusCode.BadRequest, await Status(Add(me, 12, 10)));
        Assert.Equal(HttpStatusCode.BadRequest, await Status(me.PostAsJsonAsync("/api/availability", new { start = At(0), end = At(25) }))); // > 24 h
        Assert.Equal(HttpStatusCode.BadRequest, await Status(me.GetAsync($"/api/availability?from={Day:O}&to={Day.AddDays(63):O}"))); // > 62 days
    }

    [Fact]
    public async Task Can_not_remove_time_with_an_interview()
    {
        var admin = await App.AdminAsync();
        var me = await App.UserAsync(admin, "Ala");
        await Json(Add(me.Client, 10, 14));
        await App.InterviewAsync("Kasia", At(11), [], [me.Id]);

        Assert.Equal(HttpStatusCode.Conflict, await Status(Remove(me.Client, 10, 12)));
        Assert.Equal(HttpStatusCode.NoContent, await Status(Remove(me.Client, 12, 14))); // after the interview is fine
        Assert.Equal([(10, 12)], await Blocks(me.Client));
    }
}

public class SectionTests : IntegrationTest
{
    [Fact]
    public async Task Create_update_and_unique_names()
    {
        var admin = await App.AdminAsync();
        var ai = await App.SectionAsync(admin, "AI", duration: 45, buffer: 10);
        await App.SectionAsync(admin, "Bio");

        Assert.Equal(HttpStatusCode.Conflict, await Status(admin.PostAsJsonAsync("/api/sections", new { name = " ai " }))); // case and spaces don't matter
        Assert.Equal(HttpStatusCode.BadRequest, await Status(admin.PostAsJsonAsync("/api/sections", new { name = "Web", interviewDurationMinutes = 2 })));
        Assert.Equal(HttpStatusCode.BadRequest, await Status(admin.PostAsJsonAsync("/api/sections", new { name = "" })));

        var updated = await Json(admin.PutAsJsonAsync($"/api/sections/{ai}", new { name = "Artificial Intelligence", interviewDurationMinutes = 30, bufferMinutes = 0 }));
        Assert.Equal(30, updated.GetProperty("interviewDurationMinutes").GetInt32());
        Assert.Equal(HttpStatusCode.Conflict, await Status(admin.PutAsJsonAsync($"/api/sections/{ai}", new { name = "Bio" })));
        Assert.Equal(HttpStatusCode.NotFound, await Status(admin.PutAsJsonAsync("/api/sections/999999", new { name = "X" })));

        var names = (await Json(admin.GetAsync("/api/sections"))).EnumerateArray().Select(s => s.GetProperty("name").GetString());
        Assert.Equal(["Artificial Intelligence", "Bio"], names); // sorted
    }

    [Fact]
    public async Task Sections_in_use_can_not_be_deleted()
    {
        var admin = await App.AdminAsync();
        int used = await App.SectionAsync(admin, "AI"), free = await App.SectionAsync(admin, "Web");
        var member = await App.UserAsync(admin, "Ala", [free]);
        await Json(admin.PostAsJsonAsync("/api/booking-links", new { sectionIds = new[] { used }, count = 1 }));

        Assert.Equal(HttpStatusCode.Conflict, await Status(admin.DeleteAsync($"/api/sections/{used}")));
        Assert.Equal(HttpStatusCode.NoContent, await Status(admin.DeleteAsync($"/api/sections/{free}"))); // member assignments go with it
        var me = await Json(member.Client.GetAsync("/api/auth/me"));
        Assert.Equal(0, me.GetProperty("sections").GetArrayLength());
    }
}

public class UserManagementTests : IntegrationTest
{
    static object Body(string email, string role = "Recruiter", int[]? sections = null) => new { email, displayName = "X", role, sectionIds = sections ?? [] };

    [Fact]
    public async Task Validation_and_duplicates()
    {
        var admin = await App.AdminAsync();
        await App.UserAsync(admin, "Ala");
        Assert.Equal(HttpStatusCode.Conflict, await Status(admin.PostAsJsonAsync("/api/users", Body("ALA@club.local"))));
        Assert.Equal(HttpStatusCode.BadRequest, await Status(admin.PostAsJsonAsync("/api/users", Body("x@club.local", role: "Boss"))));
        Assert.Equal(HttpStatusCode.BadRequest, await Status(admin.PostAsJsonAsync("/api/users", Body("x@club.local", sections: [999999]))));
        Assert.Equal(HttpStatusCode.BadRequest, await Status(admin.PostAsJsonAsync("/api/users", Body("not-an-email"))));
    }

    [Fact]
    public async Task Admins_can_not_lock_themselves_out()
    {
        var admin = await App.AdminAsync();
        var myId = await App.AdminIdAsync();
        Assert.Equal(HttpStatusCode.Conflict, await Status(admin.PutAsJsonAsync($"/api/users/{myId}", Body(AdminEmail))));
        Assert.Equal(HttpStatusCode.Conflict, await Status(admin.DeleteAsync($"/api/users/{myId}")));
    }

    [Fact]
    public async Task Role_change_and_delete()
    {
        var admin = await App.AdminAsync();
        var ala = await App.UserAsync(admin, "Ala");
        Assert.Equal(HttpStatusCode.Forbidden, await Status(ala.Client.GetAsync("/api/users")));

        await Json(admin.PutAsJsonAsync($"/api/users/{ala.Id}", Body(ala.Email, role: "Admin")));
        // Saved now; her open session picks it up within a minute (SecurityStampValidator interval) or at the next login.
        var listed = (await Json(admin.GetAsync("/api/users"))).EnumerateArray().Single(u => u.GetProperty("id").GetString() == ala.Id);
        Assert.Equal("Admin", listed.GetProperty("role").GetString());

        // Upcoming interview → reassign first; past ones don't block
        var interview = await App.InterviewAsync("Kasia", DateTime.UtcNow.AddDays(2), [], [ala.Id]);
        Assert.Equal(HttpStatusCode.Conflict, await Status(admin.DeleteAsync($"/api/users/{ala.Id}")));
        await Json(admin.PatchAsJsonAsync($"/api/interviews/{interview}", new { status = "Cancelled", notify = false }));
        Assert.Equal(HttpStatusCode.NoContent, await Status(admin.DeleteAsync($"/api/users/{ala.Id}")));
        Assert.Equal(HttpStatusCode.Unauthorized, await Status(ala.Client.GetAsync("/api/auth/me"))); // session gone with the account
    }
}

public class SettingsTests : IntegrationTest
{
    [Fact]
    public async Task Recruitment_period_needs_both_dates_in_order()
    {
        var admin = await App.AdminAsync();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        Assert.Equal(HttpStatusCode.BadRequest, await Status(admin.PutAsJsonAsync("/api/settings/recruitment", new { interviewsFrom = today })));
        Assert.Equal(HttpStatusCode.BadRequest, await Status(admin.PutAsJsonAsync("/api/settings/recruitment", new { interviewsFrom = today, interviewsTo = today.AddDays(-1) })));
        await App.SetPeriodAsync(admin, today, today.AddDays(7));
        var period = await Json(admin.GetAsync("/api/settings/recruitment"));
        Assert.Equal(today.AddDays(7).ToString("yyyy-MM-dd"), period.GetProperty("interviewsTo").GetString());
        await Json(admin.PutAsJsonAsync("/api/settings/recruitment", new { })); // clearing both closes booking
        Assert.Equal(JsonValueKind.Null, (await Json(admin.GetAsync("/api/settings/recruitment"))).GetProperty("interviewsFrom").ValueKind);
    }

    [Fact]
    public async Task Outbox_retry_and_purge()
    {
        var admin = await App.AdminAsync();
        var (failed, sent) = await App.DbAsync(async db =>
        {
            EmailMessage f = new() { To = "a@x.pl", Subject = "s", Body = "b", Status = EmailStatus.Failed, Attempts = 5, LastError = "boom" },
                s = new() { To = "b@x.pl", Subject = "s", Body = "b", Status = EmailStatus.Sent };
            db.EmailMessages.AddRange(f, s);
            await db.SaveChangesAsync();
            return (f.Id, s.Id);
        });
        Assert.Equal(HttpStatusCode.NoContent, await Status(admin.PostAsync($"/api/email/messages/{failed}/retry", null)));
        var retried = (await App.MailsAsync()).Single(m => m.Id == failed);
        Assert.Equal((EmailStatus.Pending, 0), (retried.Status, retried.Attempts));
        Assert.Equal(HttpStatusCode.NotFound, await Status(admin.PostAsync($"/api/email/messages/{sent}/retry", null))); // sent stays sent
        var pending = (await Json(admin.GetAsync("/api/email/messages?status=Pending"))).EnumerateArray().Select(m => m.GetProperty("to").GetString());
        Assert.Equal(["a@x.pl"], pending); // the list filters by status

        // Purge: everything about recruits goes, the club stays
        var ai = await App.SectionAsync(admin, "AI");
        await App.InterviewAsync("Kasia", DateTime.UtcNow.AddDays(1), [ai], []);
        await Json(admin.PostAsJsonAsync("/api/booking-links", new { sectionIds = new[] { ai }, count = 2 }));
        Assert.Equal(HttpStatusCode.BadRequest, await Status(admin.PostAsJsonAsync("/api/recruits/purge", new { confirm = "yes" })));
        var purged = await Json(admin.PostAsJsonAsync("/api/recruits/purge", new { confirm = "DELETE ALL" }));
        Assert.Equal(1, purged.GetProperty("deletedRecruits").GetInt32());
        Assert.Empty((await Json(admin.GetAsync("/api/booking-links"))).EnumerateArray());
        Assert.Empty((await Json(admin.GetAsync("/api/interviews"))).EnumerateArray());
        Assert.Single((await Json(admin.GetAsync("/api/sections"))).EnumerateArray());
    }
}
