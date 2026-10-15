using System.Security.Claims;
using RekruTime.Api.Common;

namespace RekruTime.Api.Features.Availability;

public static class AvailabilityEndpoints
{
    public static void MapAvailabilityEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/availability").WithTags("Availability").RequireAuthorization();

        group.MapGet("/", async (DateTimeOffset from, DateTimeOffset to, ClaimsPrincipal me, AvailabilityService s) =>
            (await s.ListAsync(me.Id(), from, to)).ToHttp());

        group.MapPost("/", async (RangeRequest req, ClaimsPrincipal me, AvailabilityService s) =>
            (await s.AddAsync(me.Id(), req)).ToHttp());

        group.MapDelete("/", async (DateTimeOffset start, DateTimeOffset end, ClaimsPrincipal me, AvailabilityService s) =>
            (await s.RemoveAsync(me.Id(), new RangeRequest(start, end))).ToHttp());
    }
}
