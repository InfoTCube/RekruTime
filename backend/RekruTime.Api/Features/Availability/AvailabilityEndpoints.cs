using System.Security.Claims;
using RekruTime.Api.Common;
using RekruTime.Api.Data;

namespace RekruTime.Api.Features.Availability;

public static class AvailabilityEndpoints
{
    public static void MapAvailabilityEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/availability").WithTags("Availability").RequireAuthorization();

        // userId: admins may look at anyone's (read-only) – who filled it in, and when people can interview.
        group.MapGet("/", async (DateTimeOffset from, DateTimeOffset to, string? userId, ClaimsPrincipal me, AvailabilityService s) =>
            userId is not null && userId != me.Id() && !me.IsInRole(Roles.Admin)
                ? Error.Forbidden("Only admins can see other people's availability.").ToHttp()
                : (await s.ListAsync(userId ?? me.Id(), from, to)).ToHttp());

        group.MapPost("/", async (RangeRequest req, ClaimsPrincipal me, AvailabilityService s) =>
            (await s.AddAsync(me.Id(), req)).ToHttp());

        group.MapDelete("/", async (DateTimeOffset start, DateTimeOffset end, ClaimsPrincipal me, AvailabilityService s) =>
            (await s.RemoveAsync(me.Id(), new RangeRequest(start, end))).ToHttp());
    }
}
