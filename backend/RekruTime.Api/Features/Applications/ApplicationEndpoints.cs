using System.Security.Claims;
using RekruTime.Api.Common;

namespace RekruTime.Api.Features.Applications;

public static class ApplicationEndpoints
{
    public static void MapApplicationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/applications").WithTags("Applications").RequireAuthorization();

        group.MapPost("/{id:int}/votes", async (int id, VoteRequest req, ClaimsPrincipal me, VoteService s) =>
            (await s.VoteAsync(id, me.Id(), req)).ToHttp());
    }
}
