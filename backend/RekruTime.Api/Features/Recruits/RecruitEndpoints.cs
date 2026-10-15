using RekruTime.Api.Common;
using RekruTime.Api.Data;

namespace RekruTime.Api.Features.Recruits;

public static class RecruitEndpoints
{
    public static void MapRecruitEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/recruits").WithTags("Recruits").RequireAuthorization(Roles.Admin);

        group.MapDelete("/{id:int}", async (int id, RecruitService s) => (await s.DeleteAsync(id)).ToHttp());

        group.MapPost("/purge", async (PurgeRequest req, RecruitService s) => (await s.PurgeAsync(req.Confirm)).ToHttp());
    }
}
