using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RekruTime.Api.Data;

namespace RekruTime.Tests;

// Admin CRUD of recruitment forms: the schema round-trips through jsonb, is validated, and an open form needs sections.
public class FormTests : IntegrationTest
{
    static object Body(params object[] fields) => new { title = "  Rekrutacja 2026 ", description = " ", fields };

    [Fact]
    public async Task Admin_creates_edits_opens_and_deletes_a_form()
    {
        var admin = await App.AdminAsync();
        var created = await admin.PostAsJsonAsync("/api/forms", Body(
            new { type = "Textarea", label = " Why us? ", required = true },
            new { id = "year", type = "SingleChoice", label = "Year", options = new[] { " 1 ", "2", "2", "" } }));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var form = await created.Content.ReadFromJsonAsync<JsonElement>();
        var id = form.GetProperty("id").GetInt32();
        Assert.Equal("Rekrutacja 2026", form.GetProperty("title").GetString());
        Assert.Equal(JsonValueKind.Null, form.GetProperty("description").ValueKind);
        Assert.False(form.GetProperty("isOpen").GetBoolean());
        var fields = form.GetProperty("fields").EnumerateArray().ToList();
        var why = fields[0].GetProperty("id").GetString()!;
        Assert.Matches("^[0-9a-f]{8}$", why); // made by the server
        Assert.Equal("Why us?", fields[0].GetProperty("label").GetString());
        Assert.Equal(["1", "2"], fields[1].GetProperty("options").EnumerateArray().Select(o => o.GetString()));

        // Without a sections question the form can't open.
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PatchAsJsonAsync($"/api/forms/{id}", new { isOpen = true })).StatusCode);
        await TestApp.Json(admin.PutAsJsonAsync($"/api/forms/{id}", Body(
            new { id = "sections", type = "Sections", label = "Sections", required = true, options = new[] { "ignored" } },
            new { id = why, type = "Textarea", label = "Why us?", required = true })));
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PatchAsJsonAsync($"/api/forms/{id}", new { isOpen = true })).StatusCode);

        var read = await admin.GetFromJsonAsync<JsonElement>($"/api/forms/{id}");
        Assert.True(read.GetProperty("isOpen").GetBoolean());
        Assert.Equal(["sections", why], read.GetProperty("fields").EnumerateArray().Select(f => f.GetProperty("id").GetString()));
        Assert.Equal("Sections", read.GetProperty("fields")[0].GetProperty("type").GetString());
        Assert.Empty(read.GetProperty("fields")[0].GetProperty("options").EnumerateArray()); // options come from the sections
        // Removing the sections question from an open form is refused too.
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync($"/api/forms/{id}", Body(new { type = "Text", label = "x" }))).StatusCode);

        var list = await admin.GetFromJsonAsync<JsonElement>("/api/forms");
        Assert.Equal(0, Assert.Single(list.EnumerateArray()).GetProperty("responses").GetInt32());

        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/forms/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/api/forms/{id}")).StatusCode);
    }

    [Theory]
    [InlineData("""[{"type":"Text","label":"  "}]""")]                                                    // no label
    [InlineData("""[{"type":"SingleChoice","label":"Year","options":[" "]}]""")]                         // choice without options
    [InlineData("""[{"id":"a","type":"Text","label":"A"},{"id":"a","type":"Text","label":"B"}]""")]     // duplicate id
    [InlineData("""[{"id":"no spaces","type":"Text","label":"A"}]""")]                                 // bad id
    [InlineData("""[{"type":"Sections","label":"A"},{"type":"Sections","label":"B"}]""")]              // two sections questions
    public async Task Invalid_schemas_are_rejected(string fields)
    {
        var admin = await App.AdminAsync();
        var body = new { title = "F", fields = JsonDocument.Parse(fields).RootElement };
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/forms", body)).StatusCode);
    }

    [Fact]
    public async Task A_form_with_responses_cant_be_deleted_and_the_recruits_gdpr_delete_takes_their_response()
    {
        var admin = await App.AdminAsync();
        var id = (await TestApp.Json(admin.PostAsJsonAsync("/api/forms", Body(new { id = "about", type = "Text", label = "About you" }))))
            .GetProperty("id").GetInt32();
        var recruitId = await App.DbAsync(async db =>
        {
            var recruit = new Recruit { FullName = "Kasia", Email = "kasia@x.pl" };
            db.Recruits.Add(recruit);
            await db.SaveChangesAsync();
            db.FormResponses.Add(new FormResponse { FormId = id, RecruitId = recruit.Id, Answers = [new() { FieldId = "about", Values = ["Hi"] }] });
            await db.SaveChangesAsync();
            return recruit.Id;
        });
        var answer = await App.DbAsync(db => db.FormResponses.AsNoTracking().SingleAsync());
        Assert.Equal("Hi", Assert.Single(Assert.Single(answer.Answers).Values));
        Assert.Equal(1, (await admin.GetFromJsonAsync<JsonElement>($"/api/forms/{id}")).GetProperty("responses").GetInt32());

        Assert.Equal(HttpStatusCode.Conflict, (await admin.DeleteAsync($"/api/forms/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/recruits/{recruitId}")).StatusCode);
        Assert.Equal(0, await App.DbAsync(db => db.FormResponses.CountAsync()));
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/forms/{id}")).StatusCode);
    }
}
