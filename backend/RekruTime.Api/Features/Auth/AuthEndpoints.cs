using Microsoft.AspNetCore.Identity;
using RekruTime.Api.Data;

namespace RekruTime.Api.Features.Auth;

public record LoginRequest(string Email, string Password);
public record MeResponse(string Id, string Email, string DisplayName, IList<string> Roles);

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var auth = app.MapGroup("/api/auth").WithTags("Auth");

        auth.MapPost("/login", async (LoginRequest req, UserManager<AppUser> users, SignInManager<AppUser> signIn) =>
        {
            var user = await users.FindByEmailAsync(req.Email);
            if (user is null) return Results.Unauthorized();
            var result = await signIn.PasswordSignInAsync(user, req.Password, isPersistent: true, lockoutOnFailure: true);
            if (result.IsLockedOut) return Results.Problem("Too many failed attempts, try again later.", statusCode: 429);
            return result.Succeeded ? Results.Ok(await Me(user, users)) : Results.Unauthorized();
        });

        auth.MapPost("/logout", async (SignInManager<AppUser> signIn) =>
        {
            await signIn.SignOutAsync();
            return Results.NoContent();
        });

        auth.MapGet("/me", async (HttpContext http, UserManager<AppUser> users) =>
            await users.GetUserAsync(http.User) is { } user ? Results.Ok(await Me(user, users)) : Results.Unauthorized())
            .RequireAuthorization();
    }

    static async Task<MeResponse> Me(AppUser user, UserManager<AppUser> users) =>
        new(user.Id, user.Email!, user.DisplayName, await users.GetRolesAsync(user));
}
