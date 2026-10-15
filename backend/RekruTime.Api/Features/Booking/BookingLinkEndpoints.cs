using System.Security.Claims;
using RekruTime.Api.Common;
using RekruTime.Api.Data;

namespace RekruTime.Api.Features.Booking;

public static class BookingLinkEndpoints
{
    public static void MapBookingLinkEndpoints(this IEndpointRouteBuilder app)
    {
        var links = app.MapGroup("/api/booking-links").WithTags("Booking links").RequireAuthorization(Roles.Admin);

        links.MapGet("/", (int? sectionId, LinkStatus? status, InvitationState? invitation, BookingLinkService s) =>
            s.ListAsync(sectionId, status, invitation));

        links.MapPost("/", async (CreateLinksRequest req, ClaimsPrincipal me, BookingLinkService s) =>
            (await s.CreateAsync(req, me.Id())).ToHttp());

        links.MapPatch("/{id:int}", async (int id, SetActiveRequest req, BookingLinkService s) =>
            (await s.SetActiveAsync(id, req.IsActive)).ToHttp());

        links.MapPost("/invite", async (InviteRequest req, BookingLinkService s) =>
            (await s.InviteAsync(req.LinkIds)).ToHttp());

        links.MapGet("/{id:int}/slots", async (int id, DateTimeOffset from, DateTimeOffset to, BookingLinkService s) =>
            (await s.CoverageAsync(id, from, to)).ToHttp());

        links.MapPost("/import", async (ImportRequest req, ClaimsPrincipal me, ImportService s) =>
            (await s.ImportAsync(req, me.Id())).ToHttp());
    }
}
