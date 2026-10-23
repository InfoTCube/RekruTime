using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RekruTime.Api.Data;
using Testcontainers.PostgreSql;

namespace RekruTime.Tests;

// With e-mail set up, new users get a one-time link to set their own password instead of a temporary one.
public partial class AccountInviteTests : IAsyncLifetime
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
            .UseSetting("Club:Name", "Koło Testowe")
            .UseSetting("Club:PublicUrl", "https://kn.test")
            .UseSetting("Email:Host", "smtp.invalid") // queued only
            .UseSetting("Email:FromAddress", "rekrutacja@test.local"));
    }

    public async Task DisposeAsync()
    {
        await _app.DisposeAsync();
        await _db.DisposeAsync();
    }

    // The newest set-password link e-mailed to this address → (userId, token).
    public static async Task<(string User, string Token, EmailMessage Mail)> LinkFor(WebApplicationFactory<Program> app, string to)
    {
        using var scope = app.Services.CreateScope();
        var mail = await scope.ServiceProvider.GetRequiredService<AppDbContext>().EmailMessages.Where(m => m.To == to).OrderBy(m => m.Id).LastAsync();
        var m = LinkPattern().Match(mail.Body);
        Assert.True(m.Success, mail.Body);
        return (Uri.UnescapeDataString(m.Groups["u"].Value), m.Groups["t"].Value, mail);
    }

    [GeneratedRegex(@"https://kn\.test/set-password\?u=(?<u>[^&\s]+)&t=(?<t>[\w-]+)")]
    private static partial Regex LinkPattern();

    async Task<HttpResponseMessage> Login(HttpClient c, string email, string password) =>
        await c.PostAsJsonAsync("/api/auth/login", new { email, password });

    [Fact]
    public async Task Invite_link_sets_the_password_once_and_logs_in()
    {
        var admin = _app.CreateClient();
        await Login(admin, "admin@test.local", "Admin123!");

        var created = await (await admin.PostAsJsonAsync("/api/users", new { email = "nowy@club.local", displayName = "Nowy Członek", role = "Recruiter" }))
            .Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(JsonValueKind.Null, created.GetProperty("temporaryPassword").ValueKind);
        Assert.True(created.GetProperty("linkSent").GetBoolean());

        var (userId, token, mail) = await LinkFor(_app, "nowy@club.local");
        Assert.Equal("Your RekruTime account – Koło Testowe", mail.Subject);
        Assert.Contains("Hi Nowy!", mail.Body);
        Assert.Equal(created.GetProperty("user").GetProperty("id").GetString(), userId);

        var user = _app.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await Login(user, "nowy@club.local", "")).StatusCode); // no password yet

        async Task<HttpResponseMessage> Set(string t, string password) =>
            await user.PostAsJsonAsync("/api/auth/set-password", new { userId, token = t, newPassword = password });
        var bad = await Set("not-a-token", "NewPass123!");
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        Assert.Contains("Token", await bad.Content.ReadAsStringAsync());
        var weak = await Set(token, "short");
        Assert.Contains("NewPassword", await weak.Content.ReadAsStringAsync()); // and the token still works:

        var ok = await Set(token, "NewPass123!");
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        var me = await user.GetFromJsonAsync<JsonElement>("/api/auth/me"); // logged in right away
        Assert.Equal("nowy@club.local", me.GetProperty("email").GetString());
        Assert.False(me.GetProperty("mustChangePassword").GetBoolean());

        Assert.Equal(HttpStatusCode.BadRequest, (await Set(token, "Other123!")).StatusCode); // used up
        Assert.Equal(HttpStatusCode.OK, (await Login(_app.CreateClient(), "nowy@club.local", "NewPass123!")).StatusCode);
    }

    [Fact]
    public async Task Reset_sends_a_link_and_the_old_password_works_until_it_is_used()
    {
        var admin = _app.CreateClient();
        await Login(admin, "admin@test.local", "Admin123!");
        var created = await (await admin.PostAsJsonAsync("/api/users", new { email = "ala@club.local", displayName = "Ala", role = "Recruiter" }))
            .Content.ReadFromJsonAsync<JsonElement>();
        var id = created.GetProperty("user").GetProperty("id").GetString()!;

        // Never used the invite → a reset sends the invite again
        await admin.PostAsync($"/api/users/{id}/reset-password", null);
        var (_, invite, again) = await LinkFor(_app, "ala@club.local");
        Assert.StartsWith("Your RekruTime account", again.Subject);
        await _app.CreateClient().PostAsJsonAsync("/api/auth/set-password", new { userId = id, token = invite, newPassword = "First123!" });

        var reset = await (await admin.PostAsync($"/api/users/{id}/reset-password", null)).Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(reset.GetProperty("linkSent").GetBoolean());
        var (_, token, mail) = await LinkFor(_app, "ala@club.local");
        Assert.Equal("Set a new RekruTime password", mail.Subject);
        Assert.Equal(HttpStatusCode.OK, (await Login(_app.CreateClient(), "ala@club.local", "First123!")).StatusCode);

        await _app.CreateClient().PostAsJsonAsync("/api/auth/set-password", new { userId = id, token, newPassword = "Second123!" });
        Assert.Equal(HttpStatusCode.Unauthorized, (await Login(_app.CreateClient(), "ala@club.local", "First123!")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Login(_app.CreateClient(), "ala@club.local", "Second123!")).StatusCode);
    }
}
