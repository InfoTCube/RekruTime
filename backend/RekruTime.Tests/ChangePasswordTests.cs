using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using static RekruTime.Tests.TestApp;

namespace RekruTime.Tests;

// Temporary password from an admin (no e-mail set up) → must be changed; changing keeps you logged in and the old password stops working.
public class ChangePasswordTests : IntegrationTest
{
    static async Task<bool> MustChange(HttpClient c) =>
        (await c.GetFromJsonAsync<JsonElement>("/api/auth/me")).GetProperty("mustChangePassword").GetBoolean();

    async Task<HttpStatusCode> Login(string email, string password) =>
        await Status(App.CreateClient().PostAsJsonAsync("/api/auth/login", new { email, password }));

    [Fact]
    public async Task Temporary_password_must_be_changed_and_change_keeps_the_session()
    {
        var admin = await App.AdminAsync();
        Assert.False(await MustChange(admin)); // seed admin chose their own password

        var body = await Json(admin.PostAsJsonAsync("/api/users", new { email = "rec@test.local", displayName = "Rec", role = "Recruiter" }));
        var temp = body.GetProperty("temporaryPassword").GetString()!;
        var userId = body.GetProperty("user").GetProperty("id").GetString()!;

        var rec = await App.LoginAsync("rec@test.local", temp);
        Assert.True(await MustChange(rec));

        var wrong = await rec.PostAsJsonAsync("/api/auth/change-password", new { currentPassword = "nope", newPassword = "NewPass123!" });
        Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);
        Assert.Contains("CurrentPassword", await wrong.Content.ReadAsStringAsync());

        var weak = await rec.PostAsJsonAsync("/api/auth/change-password", new { currentPassword = temp, newPassword = "short" });
        Assert.Equal(HttpStatusCode.BadRequest, weak.StatusCode);
        Assert.Contains("NewPassword", await weak.Content.ReadAsStringAsync());

        var ok = await rec.PostAsJsonAsync("/api/auth/change-password", new { currentPassword = temp, newPassword = "NewPass123!" });
        Assert.Equal(HttpStatusCode.NoContent, ok.StatusCode);
        Assert.False(await MustChange(rec)); // same session still works (cookie refreshed)

        Assert.Equal(HttpStatusCode.Unauthorized, await Login("rec@test.local", temp));
        Assert.Equal(HttpStatusCode.OK, await Login("rec@test.local", "NewPass123!"));

        // An admin reset gives a temporary password again.
        var temp2 = (await Json(admin.PostAsync($"/api/users/{userId}/reset-password", null))).GetProperty("temporaryPassword").GetString()!;
        Assert.True(await MustChange(await App.LoginAsync("rec@test.local", temp2)));
    }
}
