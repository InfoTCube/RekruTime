using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace RekruTime.Tests;

// The recruit's form answers (with follow-up questions) and the sections' suggested questions on the interview and
// recruit pages – for the same people as the notes. Follow-ups never reach the public form.
public class InterviewPrepTests : IntegrationTest
{
    [Fact]
    public async Task Interviewers_see_the_answers_follow_ups_and_their_sections_questions()
    {
        var admin = await App.AdminAsync();
        int ai = await App.SectionAsync(admin, "AI"), web = await App.SectionAsync(admin, "Web");
        var interviewer = await App.UserAsync(admin, "Ina", [ai]);
        var aiLeader = (await App.UserAsync(admin, "Ala", [ai], leads: [ai])).Client;
        var outsider = (await App.UserAsync(admin, "Ola", [web])).Client;

        var form = (await TestApp.Json(admin.PostAsJsonAsync("/api/forms", new
        {
            title = "Rekrutacja",
            fields = new object[]
            {
                new { id = "sections", type = "Sections", label = "Sections", required = true },
                new { id = "python", type = "Text", label = "Experience with Python", followUp = "  Show us something you built with it.  " },
                new { id = "unanswered", type = "Text", label = "Anything else?" },
            },
        }))).GetProperty("id").GetInt32();
        await TestApp.Json(admin.PatchAsJsonAsync($"/api/forms/{form}", new { isOpen = true }));
        var publicForm = await App.CreateClient().GetStringAsync($"/api/public/forms/{form}");
        Assert.DoesNotContain("Show us something", publicForm); // interview questions stay internal

        await TestApp.Json(App.CreateClient().PostAsJsonAsync($"/api/public/forms/{form}/responses", new
        {
            fullName = "Kasia", email = "kasia@x.pl", gdprConsent = true,
            answers = new Dictionary<string, string[]> { ["sections"] = [$"{ai}", $"{web}"], ["python"] = ["2 years"] },
        }));
        await TestApp.Json(aiLeader.PutAsJsonAsync($"/api/sections/{ai}/questions", new { questions = new[] { " Which ML project are you proudest of? ", "  " } }));
        await TestApp.Json(admin.PutAsJsonAsync($"/api/sections/{web}/questions", new { questions = new[] { "Favourite framework?" } }));
        var interviewId = await App.InterviewAsync("Kasia", DateTime.UtcNow.AddDays(2), [ai], [interviewer.Id]); // AI only

        var prep = await interviewer.Client.GetFromJsonAsync<JsonElement>($"/api/interviews/{interviewId}/prep");
        var answers = Assert.Single(prep.GetProperty("forms").EnumerateArray()).GetProperty("answers").EnumerateArray().ToList();
        Assert.Equal(["Sections", "Experience with Python"], answers.Select(a => a.GetProperty("question").GetString()));
        Assert.Equal(["AI", "Web"], answers[0].GetProperty("values").EnumerateArray().Select(v => v.GetString())); // ids → names
        Assert.Equal("Show us something you built with it.", answers[1].GetProperty("followUp").GetString());
        var section = Assert.Single(prep.GetProperty("sections").EnumerateArray()); // the interview's sections only
        Assert.Equal(["Which ML project are you proudest of?"], section.GetProperty("questions").EnumerateArray().Select(q => q.GetString()));

        Assert.Equal(HttpStatusCode.OK, (await aiLeader.GetAsync($"/api/interviews/{interviewId}/prep")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await outsider.GetAsync($"/api/interviews/{interviewId}/prep")).StatusCode);

        // The recruit page carries the same prep, for the sections the viewer reviews (admin: both).
        var recruitId = (await admin.GetFromJsonAsync<JsonElement>("/api/recruits")).EnumerateArray().Single().GetProperty("id").GetInt32();
        var detail = await admin.GetFromJsonAsync<JsonElement>($"/api/recruits/{recruitId}");
        Assert.Equal(["AI", "Web"], detail.GetProperty("prep").GetProperty("sections").EnumerateArray().Select(s => s.GetProperty("name").GetString()));
        Assert.Single(detail.GetProperty("prep").GetProperty("forms").EnumerateArray());
    }

    [Fact]
    public async Task Section_questions_are_edited_by_its_leaders_and_admins()
    {
        var admin = await App.AdminAsync();
        int ai = await App.SectionAsync(admin, "AI"), web = await App.SectionAsync(admin, "Web");
        var aiLeader = (await App.UserAsync(admin, "Ala", [ai], leads: [ai])).Client;
        var recruiter = (await App.UserAsync(admin, "Rita", [ai])).Client;
        var url = $"/api/sections/{ai}/questions";

        var saved = await TestApp.Json(aiLeader.PutAsJsonAsync(url, new { questions = new[] { "Why AI?", "", "Your last project?" } }));
        Assert.Equal(["Why AI?", "Your last project?"], saved.GetProperty("questions").EnumerateArray().Select(q => q.GetString()));
        Assert.Equal(2, (await admin.GetFromJsonAsync<JsonElement>(url)).GetProperty("questions").GetArrayLength());

        Assert.Equal(HttpStatusCode.Forbidden, (await aiLeader.PutAsJsonAsync($"/api/sections/{web}/questions", new { questions = new[] { "x" } })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await recruiter.GetAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync(url, new { questions = new[] { new string('x', 501) } })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync("/api/sections/999999/questions")).StatusCode);
    }
}
