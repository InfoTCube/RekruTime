using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace RekruTime.Tests;

// With e-mail set up, new users get a one-time link to set their own password instead of a temporary one.
public class AccountInviteTests() : IntegrationTest(new Dictionary<string, string?>(TestApp.QueuedEmail) { ["Club:Name"] = "Koło Testowe" })
{
    Task<HttpResponseMessage> Login(string email, string password) =>
        App.CreateClient().PostAsJsonAsync("/api/auth/login", new { email, password });

    [Fact]
    public async Task Invite_link_sets_the_password_once_and_logs_in()
    {
        var admin = await App.AdminAsync();

        var created = await TestApp.Json(admin.PostAsJsonAsync("/api/users", new { email = "nowy@club.local", displayName = "Nowy Członek", role = "Recruiter" }));
        Assert.Equal(JsonValueKind.Null, created.GetProperty("temporaryPassword").ValueKind);
        Assert.True(created.GetProperty("linkSent").GetBoolean());

        var (userId, token, mail) = await App.LinkAsync("nowy@club.local");
        Assert.Equal("Your RekruTime account – Koło Testowe", mail.Subject);
        Assert.Contains("Hi Nowy!", mail.Body);
        Assert.Contains($"{TestApp.PublicUrl}/set-password?u=", mail.Body);
        Assert.Equal(created.GetProperty("user").GetProperty("id").GetString(), userId);

        Assert.Equal(HttpStatusCode.Unauthorized, (await Login("nowy@club.local", "")).StatusCode); // no password yet

        var user = App.CreateClient();
        async Task<HttpResponseMessage> Set(string t, string password) =>
            await user.PostAsJsonAsync("/api/auth/set-password", new { userId, token = t, newPassword = password });
        var bad = await Set("not-a-token", "NewPass123!");
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        Assert.Contains("Token", await bad.Content.ReadAsStringAsync());
        var weak = await Set(token, "short");
        Assert.Contains("NewPassword", await weak.Content.ReadAsStringAsync()); // and the token still works:

        Assert.Equal(HttpStatusCode.OK, (await Set(token, "NewPass123!")).StatusCode);
        var me = await user.GetFromJsonAsync<JsonElement>("/api/auth/me"); // logged in right away
        Assert.Equal("nowy@club.local", me.GetProperty("email").GetString());
        Assert.False(me.GetProperty("mustChangePassword").GetBoolean());

        Assert.Equal(HttpStatusCode.BadRequest, (await Set(token, "Other123!")).StatusCode); // used up
        Assert.Equal(HttpStatusCode.OK, (await Login("nowy@club.local", "NewPass123!")).StatusCode);
    }

    [Fact]
    public async Task Reset_sends_a_link_and_the_old_password_works_until_it_is_used()
    {
        var admin = await App.AdminAsync();
        var created = await TestApp.Json(admin.PostAsJsonAsync("/api/users", new { email = "ala@club.local", displayName = "Ala", role = "Recruiter" }));
        var id = created.GetProperty("user").GetProperty("id").GetString()!;
        Task<HttpResponseMessage> Set(string token, string password) =>
            App.CreateClient().PostAsJsonAsync("/api/auth/set-password", new { userId = id, token, newPassword = password });

        // Never used the invite → a reset sends the invite again
        await admin.PostAsync($"/api/users/{id}/reset-password", null);
        var (_, invite, again) = await App.LinkAsync("ala@club.local");
        Assert.StartsWith("Your RekruTime account", again.Subject);
        await Set(invite, "First123!");

        var reset = await TestApp.Json(admin.PostAsync($"/api/users/{id}/reset-password", null));
        Assert.True(reset.GetProperty("linkSent").GetBoolean());
        var (_, token, mail) = await App.LinkAsync("ala@club.local");
        Assert.Equal("Set a new RekruTime password", mail.Subject);
        Assert.Equal(HttpStatusCode.OK, (await Login("ala@club.local", "First123!")).StatusCode);

        await Set(token, "Second123!");
        Assert.Equal(HttpStatusCode.Unauthorized, (await Login("ala@club.local", "First123!")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Login("ala@club.local", "Second123!")).StatusCode);
    }
}
