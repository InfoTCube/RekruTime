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

        group.MapPatch("/{id:int}", async (int id, UpdateInterviewRequest req, InterviewService s) =>
            (await s.UpdateAsync(id, req)).ToHttp()).RequireAuthorization(Roles.Admin);
    }
}
