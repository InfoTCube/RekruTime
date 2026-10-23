using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using RekruTime.Api.Data;
using Testcontainers.PostgreSql;

namespace RekruTime.Tests;

// The real app on a fresh database. One Postgres container serves the whole test run (needs Docker); every TestApp
// gets its own database in it, so tests stay isolated and still run in parallel. Runs as "Testing", so
// appsettings.Development.json (local DB, Mailpit…) never leaks in – e-mail is off unless a test turns it on.
public partial class TestApp(IReadOnlyDictionary<string, string?>? settings = null) : WebApplicationFactory<Program>
{
    public const string AdminEmail = "admin@test.local", AdminPassword = "Admin123!", PublicUrl = "https://kn.test";

    // E-mail "configured" with an unreachable server: messages are only queued (the sender's attempts fail quietly).
    public static readonly Dictionary<string, string?> QueuedEmail = new()
    {
        ["Email:Host"] = "smtp.invalid",
        ["Email:FromAddress"] = "rekrutacja@test.local",
        ["Club:PublicUrl"] = PublicUrl,
    };

    static readonly Lazy<Task<string>> Postgres = new(async () =>
    {
        var container = new PostgreSqlBuilder("postgres:17").Build(); // removed by Testcontainers' reaper after the run
        await container.StartAsync();
        return container.GetConnectionString();
    });

    string _connection = "";

    // Creates the database (migrations + seeded admin) before the first request.
    public async Task StartAsync()
    {
        _connection = new NpgsqlConnectionStringBuilder(await Postgres.Value) { Database = "t" + Guid.NewGuid().ToString("N") }.ConnectionString;
        _ = Services;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Default", _connection);
        builder.UseSetting("SeedAdmin:Email", AdminEmail);
        builder.UseSetting("SeedAdmin:Password", AdminPassword);
        foreach (var (key, value) in settings ?? new Dictionary<string, string?>()) builder.UseSetting(key, value);
    }

    // ---- HTTP ----

    public async Task<HttpClient> LoginAsync(string email, string password)
    {
        var client = CreateClient();
        var r = await client.PostAsJsonAsync("/api/auth/login", new { email, password });
        Assert.True(r.IsSuccessStatusCode, $"login {email}: {(int)r.StatusCode} {await r.Content.ReadAsStringAsync()}");
        return client;
    }

    public Task<HttpClient> AdminAsync() => LoginAsync(AdminEmail, AdminPassword);

    public static async Task<JsonElement> Json(Task<HttpResponseMessage> call)
    {
        var r = await call;
        Assert.True(r.IsSuccessStatusCode, $"{r.RequestMessage?.Method} {r.RequestMessage?.RequestUri} → {(int)r.StatusCode} {await r.Content.ReadAsStringAsync()}");
        return r.StatusCode == HttpStatusCode.NoContent ? default : await r.Content.ReadFromJsonAsync<JsonElement>();
    }

    public static async Task<HttpStatusCode> Status(Task<HttpResponseMessage> call) => (await call).StatusCode;

    public async Task<int> SectionAsync(HttpClient admin, string name, int duration = 30, int buffer = 0) =>
        (await Json(admin.PostAsJsonAsync("/api/sections", new { name, interviewDurationMinutes = duration, bufferMinutes = buffer }))).GetProperty("id").GetInt32();

    public record User(string Id, string Email, HttpClient Client);

    // A club member, logged in: with e-mail set up through the invite link, otherwise with the temporary password.
    public async Task<User> UserAsync(HttpClient admin, string name, int[]? sections = null, int[]? leads = null, string role = "Recruiter")
    {
        var email = $"{name.ToLowerInvariant()}@club.local";
        var body = await Json(admin.PostAsJsonAsync("/api/users", new { email, displayName = name, role, sectionIds = sections ?? [], leaderSectionIds = leads ?? [] }));
        var id = body.GetProperty("user").GetProperty("id").GetString()!;
        if (body.GetProperty("temporaryPassword").GetString() is { } temp) return new(id, email, await LoginAsync(email, temp));
        var (userId, token, _) = await LinkAsync(email);
        var client = CreateClient();
        await Json(client.PostAsJsonAsync("/api/auth/set-password", new { userId, token, newPassword = "Member123!" })); // logs in
        return new(id, email, client);
    }

    public Task SetPeriodAsync(HttpClient admin, DateOnly from, DateOnly to) =>
        Json(admin.PutAsJsonAsync("/api/settings/recruitment", new { interviewsFrom = from, interviewsTo = to }));

    // ---- Database ----

    public async Task<T> DbAsync<T>(Func<AppDbContext, Task<T>> work)
    {
        using var scope = Services.CreateScope();
        return await work(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    public Task DbAsync(Func<AppDbContext, Task> work) => DbAsync(async db => { await work(db); return 0; });

    // An interview straight in the DB (skips booking), 30 min; recruit e-mail = first name lower-case @x.pl.
    public Task<int> InterviewAsync(string recruit, DateTime start, int[] sections, string[] interviewerIds,
        InterviewStatus status = InterviewStatus.Scheduled, DateTime? reminded = null) => DbAsync(async db =>
    {
        var interview = new Interview
        {
            Recruit = await db.Recruits.FirstOrDefaultAsync(r => r.FullName == recruit)
                ?? new Recruit { FullName = recruit, Email = $"{recruit.Split(' ')[0].ToLowerInvariant()}@x.pl" },
            Start = start, End = start.AddMinutes(30), BlockedUntil = start.AddMinutes(30), Status = status, ReminderSentAt = reminded,
            Sections = await db.Sections.Where(s => sections.Contains(s.Id)).ToListAsync(),
            Interviewers = await db.Users.Where(u => interviewerIds.Contains(u.Id)).ToListAsync(),
        };
        db.Interviews.Add(interview);
        await db.SaveChangesAsync();
        return interview.Id;
    });

    public Task<string> AdminIdAsync() => DbAsync(db => db.Users.Where(u => u.Email == AdminEmail).Select(u => u.Id).SingleAsync());

    // An app service in its own scope (e.g. ImportService, ReminderJob).
    public async Task<TResult> UseAsync<TService, TResult>(Func<TService, Task<TResult>> work) where TService : notnull
    {
        using var scope = Services.CreateScope();
        return await work(scope.ServiceProvider.GetRequiredService<TService>());
    }

    public Task<List<EmailMessage>> MailsAsync(int afterId = 0) =>
        DbAsync(db => db.EmailMessages.AsNoTracking().Where(m => m.Id > afterId).OrderBy(m => m.Id).ToListAsync());

    public async Task<int> LastMailIdAsync() => (await MailsAsync()).Select(m => m.Id).DefaultIfEmpty(0).Max();

    // The newest set-password link e-mailed to this address.
    public async Task<(string UserId, string Token, EmailMessage Mail)> LinkAsync(string to)
    {
        var mail = (await MailsAsync()).Last(m => m.To == to);
        var m = LinkPattern().Match(mail.Body);
        Assert.True(m.Success, mail.Body);
        return (Uri.UnescapeDataString(m.Groups["u"].Value), m.Groups["t"].Value, mail);
    }

    [GeneratedRegex(@"/set-password\?u=(?<u>[^&\s]+)&t=(?<t>[\w-]+)")]
    private static partial Regex LinkPattern();
}

// Base for tests against the real app: a fresh TestApp (own database) per test.
public abstract class IntegrationTest(IReadOnlyDictionary<string, string?>? settings = null) : IAsyncLifetime
{
    protected TestApp App { get; } = new(settings);

    public Task InitializeAsync() => App.StartAsync();

    public async Task DisposeAsync() => await App.DisposeAsync();
}
