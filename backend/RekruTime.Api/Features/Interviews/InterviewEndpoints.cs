using System.Security.Claims;
using RekruTime.Api.Common;
using RekruTime.Api.Data;

namespace RekruTime.Api.Features.Interviews;

public static class InterviewEndpoints
{
    public static void MapInterviewEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/interviews").WithTags("Interviews").RequireAuthorization();

        group.MapGet("/", (DateTimeOffset? from, DateTimeOffset? to, int? sectionId, InterviewStatus? status, ClaimsPrincipal me, InterviewService s) =>
            s.ListAsync(new InterviewFilter(from, to, sectionId, status), me.IsInRole(Roles.Admin) ? null : me.Id()));

        group.MapGet("/{id:int}", async (int id, ClaimsPrincipal me, InterviewService s) =>
            (await s.GetAsync(id, me.Id(), me.IsInRole(Roles.Admin))).ToHttp());

        group.MapPatch("/{id:int}", async (int id, UpdateInterviewRequest req, InterviewService s) =>
            (await s.UpdateAsync(id, req)).ToHttp()).RequireAuthorization(Roles.Admin);

        group.MapGet("/{id:int}/notes", async (int id, ClaimsPrincipal me, NoteService s) =>
            (await s.ListAsync(id, me.Id(), me.IsInRole(Roles.Admin))).ToHttp());

        group.MapPut("/{id:int}/notes", async (int id, NoteRequest req, ClaimsPrincipal me, NoteService s) =>
            (await s.SaveAsync(id, me.Id(), req)).ToHttp(note => note is null ? Results.NoContent() : Results.Ok(note))); // 204 = deleted (blank text)
    }
}
