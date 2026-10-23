using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Testcontainers.PostgreSql;

namespace RekruTime.Tests;

// Temporary password from an admin → must be changed; changing keeps you logged in and the old password stops working.
public class ChangePasswordTests : IAsyncLifetime
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

    async Task<HttpClient> LoginAsync(string email, string password, HttpStatusCode expected = HttpStatusCode.OK)
    {
        var client = _app.CreateClient();
        var r = await client.PostAsJsonAsync("/api/auth/login", new { email, password });
        Assert.Equal(expected, r.StatusCode);
        return client;
    }

    static async Task<bool> MustChange(HttpClient c) =>
        (await c.GetFromJsonAsync<JsonElement>("/api/auth/me")).GetProperty("mustChangePassword").GetBoolean();

    [Fact]
    public async Task Temporary_password_must_be_changed_and_change_keeps_the_session()
    {
        var admin = await LoginAsync("admin@test.local", "Admin123!");
        Assert.False(await MustChange(admin)); // seed admin chose their own password

        var created = await admin.PostAsJsonAsync("/api/users", new { email = "rec@test.local", displayName = "Rec", role = "Recruiter" });
        var body = await created.Content.ReadFromJsonAsync<JsonElement>();
        var temp = body.GetProperty("temporaryPassword").GetString()!;
        var userId = body.GetProperty("user").GetProperty("id").GetString()!;

        var rec = await LoginAsync("rec@test.local", temp);
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

        await LoginAsync("rec@test.local", temp, HttpStatusCode.Unauthorized);
        await LoginAsync("rec@test.local", "NewPass123!");

        // An admin reset gives a temporary password again.
        var reset = await admin.PostAsync($"/api/users/{userId}/reset-password", null);
        var temp2 = (await reset.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("temporaryPassword").GetString()!;
        Assert.True(await MustChange(await LoginAsync("rec@test.local", temp2)));
    }
}
