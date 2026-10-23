using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace RekruTime.Tests;

// Interviewers write their own note; leaders of the interview's sections and admins only read; others get 403.
public class InterviewNoteTests : IntegrationTest
{
    [Fact]
    public async Task Only_interviewers_write_and_leaders_and_admins_read()
    {
        var admin = await App.AdminAsync();
        int ai = await App.SectionAsync(admin, "AI"), web = await App.SectionAsync(admin, "Web");
        var interviewer = await App.UserAsync(admin, "Ina", [ai]);
        var aiLeader = (await App.UserAsync(admin, "Ala", [ai], leads: [ai])).Client;
        var webLeader = (await App.UserAsync(admin, "Wera", [web], leads: [web])).Client;
        var outsider = (await App.UserAsync(admin, "Ola", [ai])).Client;

        // In the past, so the interviewer can be deleted at the end.
        var interviewId = await App.InterviewAsync("Kasia", DateTime.UtcNow.AddDays(-1), [ai], [interviewer.Id]);
        var url = $"/api/interviews/{interviewId}/notes";

        var saved = await interviewer.Client.PutAsJsonAsync(url, new { text = "  Strong in Python.  " });
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        Assert.Equal("Strong in Python.", (await saved.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("text").GetString());
        await interviewer.Client.PutAsJsonAsync(url, new { text = "Strong in Python, weak in math." }); // updates, doesn't add a second note

        var mine = await interviewer.Client.GetFromJsonAsync<JsonElement>(url);
        Assert.True(mine.GetProperty("canWrite").GetBoolean());
        var note = Assert.Single(mine.GetProperty("notes").EnumerateArray());
        Assert.Equal("Strong in Python, weak in math.", note.GetProperty("text").GetString());
        Assert.Equal("Ina", note.GetProperty("authorName").GetString());

        foreach (var reader in new[] { aiLeader, admin })
        {
            var read = await reader.GetFromJsonAsync<JsonElement>(url);
            Assert.False(read.GetProperty("canWrite").GetBoolean());
            Assert.Single(read.GetProperty("notes").EnumerateArray());
            Assert.Equal(HttpStatusCode.Forbidden, (await reader.PutAsJsonAsync(url, new { text = "x" })).StatusCode);
        }
        foreach (var stranger in new[] { webLeader, outsider })
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await stranger.GetAsync(url)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await stranger.GetAsync($"/api/interviews/{interviewId}")).StatusCode);
        }
        foreach (var reader in new[] { interviewer.Client, aiLeader, admin }) // the interview itself: same readers as the notes
            Assert.Equal("Kasia", (await reader.GetFromJsonAsync<JsonElement>($"/api/interviews/{interviewId}")).GetProperty("recruit").GetProperty("fullName").GetString());
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync("/api/interviews/999999/notes")).StatusCode);

        // The note outlives its author's account.
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/users/{interviewer.Id}")).StatusCode);
        var orphan = Assert.Single((await admin.GetFromJsonAsync<JsonElement>(url)).GetProperty("notes").EnumerateArray());
        Assert.Equal("(deleted user)", orphan.GetProperty("authorName").GetString());
    }

    [Fact]
    public async Task Blank_text_deletes_the_note()
    {
        var admin = await App.AdminAsync();
        var interviewId = await App.InterviewAsync("Zosia", DateTime.UtcNow.AddDays(1), [], [await App.AdminIdAsync()]);
        var url = $"/api/interviews/{interviewId}/notes";

        await admin.PutAsJsonAsync(url, new { text = "Note" });
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsJsonAsync(url, new { text = "   " })).StatusCode);
        Assert.Empty((await admin.GetFromJsonAsync<JsonElement>(url)).GetProperty("notes").EnumerateArray());
    }
}
