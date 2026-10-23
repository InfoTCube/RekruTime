using System.Security.Claims;
using RekruTime.Api.Common;
using RekruTime.Api.Data;

namespace RekruTime.Api.Features.Recruits;

public static class RecruitEndpoints
{
    public static void MapRecruitEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/recruits").WithTags("Recruits").RequireAuthorization();
        static RecruitReviewService.Viewer Viewer(ClaimsPrincipal me) => new(me.Id(), me.IsInRole(Roles.Admin));

        // Admins and section leaders (checked in the service).
        group.MapGet("/", async (int? sectionId, ApplicationStatus? status, ClaimsPrincipal me, RecruitReviewService s) =>
            (await s.ListAsync(Viewer(me), sectionId, status)).ToHttp());

        group.MapGet("/{id:int}", async (int id, ClaimsPrincipal me, RecruitReviewService s) => (await s.GetAsync(Viewer(me), id)).ToHttp());

        group.MapDelete("/{id:int}", async (int id, RecruitService s) => (await s.DeleteAsync(id)).ToHttp()).RequireAuthorization(Roles.Admin);

        group.MapPost("/purge", async (PurgeRequest req, RecruitService s) => (await s.PurgeAsync(req.Confirm)).ToHttp()).RequireAuthorization(Roles.Admin);
    }
}
