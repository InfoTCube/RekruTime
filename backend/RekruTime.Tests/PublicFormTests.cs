using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RekruTime.Api.Data;

namespace RekruTime.Tests;

// Recruits fill in an open form without logging in: recruit upserted by e-mail, answers saved, one Submitted
// application per chosen section.
public class PublicFormTests : IntegrationTest
{
    record Club(HttpClient Admin, int Ai, int Web, int FormId);

    async Task<Club> OpenFormAsync()
    {
        var admin = await App.AdminAsync();
        int ai = await App.SectionAsync(admin, "AI"), web = await App.SectionAsync(admin, "Web");
        var form = await TestApp.Json(admin.PostAsJsonAsync("/api/forms", new
        {
            title = "Rekrutacja",
            fields = new object[]
            {
                new { id = "sections", type = "Sections", label = "Sections", required = true },
                new { id = "year", type = "SingleChoice", label = "Year", required = true, options = new[] { "1", "2" } },
                new { id = "phone", type = "Phone", label = "Phone" },
                new { id = "why", type = "Textarea", label = "Why?" },
            },
        }));
        var id = form.GetProperty("id").GetInt32();
        await TestApp.Json(admin.PatchAsJsonAsync($"/api/forms/{id}", new { isOpen = true }));
        return new(admin, ai, web, id);
    }

    static object Submission(string email, Dictionary<string, string[]> answers, string name = "Kasia Nowak", bool consent = true, string? language = "en") =>
        new { fullName = name, email, gdprConsent = consent, language, answers };

    [Fact]
    public async Task Submitting_creates_the_recruit_answers_and_applications_and_resubmitting_replaces_them()
    {
        var club = await OpenFormAsync();
        var anon = App.CreateClient();
        var url = $"/api/public/forms/{club.FormId}";

        var form = await anon.GetFromJsonAsync<JsonElement>(url);
        Assert.Equal("Rekrutacja", form.GetProperty("title").GetString());
        Assert.Equal(["AI", "Web"], form.GetProperty("sections").EnumerateArray().Select(s => s.GetProperty("name").GetString()));

        var first = await anon.PostAsJsonAsync($"{url}/responses", Submission(" Kasia@X.pl ", new()
        {
            ["sections"] = [$"{club.Ai}", $"{club.Web}"], ["year"] = ["1"], ["phone"] = [" 600 100 200 "], ["why"] = ["  "], ["unknown"] = ["x"],
        }));
        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);

        var recruit = await App.DbAsync(db => db.Recruits.Include(r => r.Applications).AsNoTracking().SingleAsync());
        Assert.Equal(("kasia@x.pl", "Kasia Nowak", "600 100 200"), (recruit.Email, recruit.FullName, recruit.Phone));
        Assert.NotNull(recruit.ConsentAt);
        Assert.All(recruit.Applications, a => Assert.Equal(ApplicationStatus.Submitted, a.Status));
        Assert.Equal([club.Ai, club.Web], recruit.Applications.Select(a => a.SectionId).Order());
        var response = await App.DbAsync(db => db.FormResponses.AsNoTracking().SingleAsync());
        Assert.Equal(["sections", "year", "phone"], response.Answers.Select(a => a.FieldId)); // blank and unknown dropped
        Assert.Equal("en", response.Language);

        // An admin already moved the AI application on; the recruit then submits again with Web only.
        await App.DbAsync(db => db.Applications.Where(a => a.SectionId == club.Ai).ExecuteUpdateAsync(a => a.SetProperty(x => x.Status, ApplicationStatus.Preselected)));
        var again = await anon.PostAsJsonAsync($"{url}/responses", Submission("kasia@x.pl", new() { ["sections"] = [$"{club.Ai}"], ["year"] = ["2"] }, name: "Katarzyna Nowak"));
        Assert.Equal(HttpStatusCode.NoContent, again.StatusCode);
        var third = await anon.PostAsJsonAsync($"{url}/responses", Submission("kasia@x.pl", new() { ["sections"] = [$"{club.Web}"], ["year"] = ["2"] }));
        Assert.Equal(HttpStatusCode.NoContent, third.StatusCode);

        recruit = await App.DbAsync(db => db.Recruits.Include(r => r.Applications).AsNoTracking().SingleAsync());
        Assert.Equal("Kasia Nowak", recruit.FullName);
        // AI stays (no longer just Submitted); Web was dropped by the 2nd submission and added again by the 3rd.
        Assert.Equal([(club.Ai, ApplicationStatus.Preselected), (club.Web, ApplicationStatus.Submitted)],
            recruit.Applications.OrderBy(a => a.SectionId).Select(a => (a.SectionId, a.Status)));
        response = await App.DbAsync(db => db.FormResponses.AsNoTracking().SingleAsync()); // still one response, replaced
        Assert.Equal("2", response.Answers.Single(a => a.FieldId == "year").Values.Single());
        Assert.Equal(1, (await club.Admin.GetFromJsonAsync<JsonElement>($"/api/forms/{club.FormId}")).GetProperty("responses").GetInt32());
    }

    [Theory]
    [InlineData("""{"year":["1"]}""", "sections")]                       // required question missing
    [InlineData("""{"sections":["999999"],"year":["1"]}""", "sections")] // unknown section
    [InlineData("""{"sections":["AI"],"year":["1"]}""", "sections")]     // not an id
    [InlineData("""{"sections":["{ai}"],"year":["3"]}""", "year")]       // not one of the options
    [InlineData("""{"sections":["{ai}"],"year":["1","2"]}""", "year")]   // two answers to a single choice
    public async Task Invalid_answers_are_rejected_on_their_question(string answers, string field)
    {
        var club = await OpenFormAsync();
        var body = new { fullName = "Kasia", email = "kasia@x.pl", gdprConsent = true, answers = JsonDocument.Parse(answers.Replace("{ai}", $"{club.Ai}")).RootElement };
        var r = await App.CreateClient().PostAsJsonAsync($"/api/public/forms/{club.FormId}/responses", body);
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.True((await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors").TryGetProperty(field, out _));
        Assert.Equal(0, await App.DbAsync(db => db.Recruits.CountAsync()));
    }

    [Fact]
    public async Task Consent_is_required_and_closed_or_unknown_forms_refuse()
    {
        var club = await OpenFormAsync();
        var anon = App.CreateClient();
        var answers = new Dictionary<string, string[]> { ["sections"] = [$"{club.Ai}"], ["year"] = ["1"] };
        var url = $"/api/public/forms/{club.FormId}";

        Assert.Equal(HttpStatusCode.BadRequest, (await anon.PostAsJsonAsync($"{url}/responses", Submission("a@x.pl", answers, consent: false))).StatusCode);

        await TestApp.Json(club.Admin.PatchAsJsonAsync(url.Replace("/public", ""), new { isOpen = false }));
        Assert.Equal(HttpStatusCode.Gone, (await anon.GetAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.Gone, (await anon.PostAsJsonAsync($"{url}/responses", Submission("a@x.pl", answers))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await anon.GetAsync("/api/public/forms/999999")).StatusCode);
        Assert.Equal(0, await App.DbAsync(db => db.Recruits.CountAsync()));
    }

    [Fact]
    public async Task Double_submit_of_a_new_recruit_saves_one_recruit_and_one_response()
    {
        var club = await OpenFormAsync();
        var body = Submission("new@x.pl", new() { ["sections"] = [$"{club.Ai}"], ["year"] = ["1"] });
        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => App.CreateClient().PostAsJsonAsync($"/api/public/forms/{club.FormId}/responses", body)));
        Assert.All(results, r => Assert.Equal(HttpStatusCode.NoContent, r.StatusCode));
        Assert.Equal((1, 1, 1), await App.DbAsync(async db => (await db.Recruits.CountAsync(), await db.FormResponses.CountAsync(), await db.Applications.CountAsync())));
    }
}
