using RekruTime.Api.Common;

namespace RekruTime.Api.Features.Booking;

// No auth: the unguessable token is the key. Rate limited per IP.
public static class PublicBookingEndpoints
{
    public const string RateLimitPolicy = "public";

    public static void MapPublicBookingEndpoints(this IEndpointRouteBuilder app)
    {
        var links = app.MapGroup("/api/public/links/{token}").WithTags("Public booking").RequireRateLimiting(RateLimitPolicy);

        links.MapGet("/", async (string token, PublicBookingService s) => (await s.GetLinkAsync(token)).ToHttp());

        links.MapGet("/slots", async (string token, DateTimeOffset? from, DateTimeOffset? to, bool? includeYellow, PublicBookingService s) =>
            (await s.GetSlotsAsync(token, from, to, includeYellow == true)).ToHttp());

        links.MapPost("/bookings", async (string token, BookRequest req, PublicBookingService s) =>
            (await s.BookAsync(token, req)).ToHttp());
    }
}
