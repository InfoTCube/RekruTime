using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace RekruTime.Tests;

// Every API endpoint × anonymous / recruiter / section leader / admin: who gets in (any status but 401/403) and who is
// turned away. New endpoints fail the coverage check until they get a row here – access is always a decision.
public partial class AuthorizationMatrixTests : IntegrationTest
{
    public enum Access { Public, LoggedIn, Reviewer, Admin } // Reviewer = section leaders + admins

    // Path parameters get harmless values (mostly unknown ids → 404 for whoever is let in).
    record Row(string Method, string Pattern, Access Access, object? Body = null);

    static readonly Row[] Matrix =
    [
        new("GET", "/api/health", Access.Public),
        new("POST", "/api/auth/set-password", Access.Public, new { userId = "x", token = "x", newPassword = "x" }),
        new("GET", "/api/auth/me", Access.LoggedIn),
        new("POST", "/api/auth/change-password", Access.LoggedIn, new { }),
        new("GET", "/api/public/links/{token}", Access.Public),
        new("GET", "/api/public/links/{token}/slots", Access.Public),
        new("POST", "/api/public/links/{token}/bookings", Access.Public, new { }),
        new("GET", "/api/public/forms/{id:int}", Access.Public),
        new("POST", "/api/public/forms/{id:int}/responses", Access.Public, new { }),

        new("GET", "/api/availability", Access.LoggedIn),
        new("POST", "/api/availability", Access.LoggedIn, new { }),
        new("DELETE", "/api/availability", Access.LoggedIn),
        new("GET", "/api/sections", Access.LoggedIn),
        new("GET", "/api/settings/recruitment", Access.LoggedIn),
        new("GET", "/api/interviews", Access.LoggedIn),
        new("GET", "/api/interviews/{id:int}", Access.LoggedIn),
        new("GET", "/api/interviews/{id:int}/prep", Access.LoggedIn),
        new("GET", "/api/interviews/{id:int}/notes", Access.LoggedIn),
        new("PUT", "/api/interviews/{id:int}/notes", Access.LoggedIn, new { text = "x" }),
        new("POST", "/api/applications/{id:int}/votes", Access.LoggedIn, new { decision = "Approve" }),

        new("GET", "/api/recruits", Access.Reviewer),
        new("GET", "/api/recruits/{id:int}", Access.Reviewer),

        new("PATCH", "/api/interviews/{id:int}", Access.Admin, new { }),
        new("DELETE", "/api/recruits/{id:int}", Access.Admin),
        new("POST", "/api/recruits/purge", Access.Admin, new { confirm = "no" }),
        new("POST", "/api/sections", Access.Admin, new { }),
        new("PUT", "/api/sections/{id:int}", Access.Admin, new { name = "x" }),
        new("DELETE", "/api/sections/{id:int}", Access.Admin),
        new("GET", "/api/sections/capacity", Access.Admin),
        new("GET", "/api/sections/{id:int}/questions", Access.LoggedIn),   // leaders of the section + admins, checked in the service
        new("PUT", "/api/sections/{id:int}/questions", Access.LoggedIn, new { questions = Array.Empty<string>() }),
        new("PUT", "/api/settings/recruitment", Access.Admin, new { }),
        new("GET", "/api/settings/reminders", Access.Admin),
        new("PUT", "/api/settings/reminders", Access.Admin, new { enabled = false }),
        new("GET", "/api/users", Access.Admin),
        new("POST", "/api/users", Access.Admin, new { }),
        new("PUT", "/api/users/{id}", Access.Admin, new { }),
        new("POST", "/api/users/{id}/reset-password", Access.Admin),
        new("DELETE", "/api/users/{id}", Access.Admin),
        new("GET", "/api/booking-links", Access.Admin),
        new("POST", "/api/booking-links", Access.Admin, new { sectionIds = Array.Empty<int>() }),
        new("PATCH", "/api/booking-links/{id:int}", Access.Admin, new { isActive = true }),
        new("POST", "/api/booking-links/invite", Access.Admin, new { linkIds = new[] { 999999 } }),
        new("GET", "/api/booking-links/{id:int}/slots", Access.Admin),
        new("POST", "/api/booking-links/import", Access.Admin, new { rows = Array.Empty<object>(), dryRun = true }),
        new("GET", "/api/email/settings", Access.Admin),
        new("POST", "/api/email/test", Access.Admin),
        new("GET", "/api/email/messages", Access.Admin),
        new("POST", "/api/email/messages/{id:int}/retry", Access.Admin),
        new("GET", "/api/email/templates", Access.Admin),
        new("PUT", "/api/email/templates/{key}/{language}", Access.Admin, new { }),
        new("DELETE", "/api/email/templates/{key}/{language}", Access.Admin),
        new("POST", "/api/email/templates/{key}/{language}/preview", Access.Admin, new { subject = "x", body = "x" }),
        new("POST", "/api/email/decisions", Access.Admin, new { recruitIds = new[] { 999999 } }),
        new("GET", "/api/preselection", Access.Reviewer),
        new("POST", "/api/preselection", Access.Reviewer, new { applicationIds = new[] { 999999 }, status = "Preselected" }),
        new("GET", "/api/preselection/invitations", Access.Admin),
        new("POST", "/api/preselection/invitations", Access.Admin, new { recruitIds = new[] { 999999 }, sendEmails = false }),
        new("GET", "/api/forms", Access.Admin),
        new("GET", "/api/forms/{id:int}", Access.Admin),
        new("POST", "/api/forms", Access.Admin, new { }),
        new("PUT", "/api/forms/{id:int}", Access.Admin, new { title = "x", fields = Array.Empty<object>() }),
        new("PATCH", "/api/forms/{id:int}", Access.Admin, new { isOpen = false }),
        new("DELETE", "/api/forms/{id:int}", Access.Admin),

        // Last: these change who the client is logged in as.
        new("POST", "/api/auth/login", Access.Public, new { email = TestApp.AdminEmail, password = TestApp.AdminPassword }),
        new("POST", "/api/auth/logout", Access.Public),
    ];

    static string Url(string pattern) => Param().Replace(pattern, m => m.Groups[1].Value switch
    {
        "id" when m.Value.Contains(":int") => "999999",
        "key" => "accepted",
        "language" => "pl",
        _ => "nope",
    });

    [GeneratedRegex(@"\{(\w+)(:[^}]*)?\}")]
    private static partial Regex Param();

    [Fact]
    public void Every_api_endpoint_has_a_row()
    {
        var endpoints = App.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText?.StartsWith("/api/") == true && !e.RoutePattern.RawText.Contains("**"))
            .SelectMany(e => (e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? []).Select(m => $"{m} {e.RoutePattern.RawText!.TrimEnd('/')}"))
            .ToHashSet();
        var rows = Matrix.Select(r => $"{r.Method} {r.Pattern}").ToHashSet();
        Assert.Empty(endpoints.Except(rows)); // endpoint without a row – add it to Matrix
        Assert.Empty(rows.Except(endpoints)); // row for an endpoint that no longer exists
    }

    [Fact]
    public async Task Only_the_right_people_get_in()
    {
        var admin = await App.AdminAsync();
        var ai = await App.SectionAsync(admin, "AI");
        var who = new Dictionary<string, HttpClient>
        {
            ["anonymous"] = App.CreateClient(),
            ["recruiter"] = (await App.UserAsync(admin, "Rita", [ai])).Client,
            ["leader"] = (await App.UserAsync(admin, "Leo", [ai], leads: [ai])).Client,
            ["admin"] = admin,
        };
        bool Allowed(Access access, string role) => access switch
        {
            Access.Public => true,
            Access.LoggedIn => role != "anonymous",
            Access.Reviewer => role is "leader" or "admin",
            _ => role == "admin",
        };

        var wrong = new List<string>();
        foreach (var row in Matrix)
            foreach (var (role, client) in who)
            {
                var request = new HttpRequestMessage(new HttpMethod(row.Method), Url(row.Pattern));
                if (row.Body is not null) request.Content = JsonContent.Create(row.Body);
                var status = (await client.SendAsync(request)).StatusCode;
                var denied = status is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden;
                if (Allowed(row.Access, role) == denied) wrong.Add($"{row.Method} {row.Pattern} as {role} → {(int)status}");
            }
        Assert.Empty(wrong);
    }
}
