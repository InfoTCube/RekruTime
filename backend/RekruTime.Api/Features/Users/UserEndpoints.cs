using System.Security.Claims;
using RekruTime.Api.Common;
using RekruTime.Api.Data;

namespace RekruTime.Api.Features.Users;

public static class UserEndpoints
{
    public static void MapUserEndpoints(this IEndpointRouteBuilder app)
    {
        var users = app.MapGroup("/api/users").WithTags("Users").RequireAuthorization(Roles.Admin);

        users.MapGet("/", (UserService s) => s.ListAsync());

        users.MapPost("/", async (UserRequest req, UserService s) =>
            (await s.CreateAsync(req)).ToHttp(dto => Results.Created($"/api/users/{dto.User.Id}", dto)));

        users.MapPut("/{id}", async (string id, UserRequest req, ClaimsPrincipal me, UserService s) =>
            (await s.UpdateAsync(id, req, me.Id())).ToHttp());

        users.MapPost("/{id}/reset-password", async (string id, UserService s) =>
            (await s.ResetPasswordAsync(id)).ToHttp());

        users.MapDelete("/{id}", async (string id, ClaimsPrincipal me, UserService s) =>
            (await s.DeleteAsync(id, me.Id())).ToHttp());
    }
}
