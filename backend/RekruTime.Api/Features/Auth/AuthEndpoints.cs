using System.Security.Claims;
using RekruTime.Api.Common;
using RekruTime.Api.Features.Booking;

namespace RekruTime.Api.Features.Auth;

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var auth = app.MapGroup("/api/auth").WithTags("Auth");

        auth.MapPost("/login", async (LoginRequest req, AuthService s) => (await s.LoginAsync(req)).ToHttp());

        auth.MapPost("/logout", async (AuthService s) =>
        {
            await s.LogoutAsync();
            return Results.NoContent();
        });

        auth.MapPost("/change-password", async (ChangePasswordRequest req, ClaimsPrincipal me, AuthService s) =>
            (await s.ChangePasswordAsync(me.Id(), req)).ToHttp()).RequireAuthorization();

        // Public (the token is the key), rate-limited like the booking endpoints.
        auth.MapPost("/set-password", async (SetPasswordRequest req, AuthService s) => (await s.SetPasswordAsync(req)).ToHttp())
            .RequireRateLimiting(PublicBookingEndpoints.RateLimitPolicy);

        auth.MapGet("/me", async (ClaimsPrincipal me, AuthService s) => (await s.MeAsync(me.Id())).ToHttp()).RequireAuthorization();
    }
}
