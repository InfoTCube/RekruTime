using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using RekruTime.Api.Data;

namespace RekruTime.Api.Features.Users;

public record UserRequest(
    [Required, EmailAddress, StringLength(256)] string Email,
    [Required, StringLength(100, MinimumLength = 1)] string DisplayName,
    [Required] string Role,
    int[]? SectionIds = null)
{
    public int[] Sections => (SectionIds ?? []).Distinct().ToArray();
}

public record UserDto(string Id, string Email, string DisplayName, string Role, int[] SectionIds);
public record CreatedUserDto(UserDto User, string TemporaryPassword);
public record PasswordDto(string TemporaryPassword);

// ponytail: admin hands out temporary passwords until phase 4 (#37) replaces them with set-password emails.
public static class UserEndpoints
{
    public static void MapUserEndpoints(this IEndpointRouteBuilder app)
    {
        var users = app.MapGroup("/api/users").WithTags("Users").RequireAuthorization(Roles.Admin);

        users.MapGet("/", async (AppDbContext db) =>
        {
            var list = await db.Users.OrderBy(u => u.DisplayName)
                .Select(u => new
                {
                    u.Id, u.Email, u.DisplayName,
                    Role = db.UserRoles.Where(ur => ur.UserId == u.Id).Join(db.Roles, ur => ur.RoleId, r => r.Id, (ur, r) => r.Name).FirstOrDefault(),
                    SectionIds = u.Sections.Select(s => s.SectionId).ToArray(),
                })
                .ToListAsync();
            return list.Select(u => new UserDto(u.Id, u.Email!, u.DisplayName, u.Role ?? "", u.SectionIds));
        });

        users.MapPost("/", async (UserRequest req, AppDbContext db, UserManager<AppUser> userManager) =>
        {
            if (await Validate(req, db) is { } invalid) return invalid;

            await using var tx = await db.Database.BeginTransactionAsync();
            var email = req.Email.Trim();
            var user = new AppUser { UserName = email, Email = email, EmailConfirmed = true, DisplayName = req.DisplayName.Trim() };
            var password = TemporaryPassword();
            var result = await userManager.CreateAsync(user, password);
            if (!result.Succeeded) return IdentityProblem(result);
            await userManager.AddToRoleAsync(user, req.Role);
            db.UserSections.AddRange(req.Sections.Select(id => new UserSection { UserId = user.Id, SectionId = id }));
            await db.SaveChangesAsync();
            await tx.CommitAsync();

            var dto = new UserDto(user.Id, email, user.DisplayName, req.Role, req.Sections);
            return Results.Created($"/api/users/{user.Id}", new CreatedUserDto(dto, password));
        });

        users.MapPut("/{id}", async (string id, UserRequest req, HttpContext http, AppDbContext db, UserManager<AppUser> userManager) =>
        {
            var user = await db.Users.Include(u => u.Sections).FirstOrDefaultAsync(u => u.Id == id);
            if (user is null) return Results.NotFound();
            if (await Validate(req, db) is { } invalid) return invalid;

            var currentRole = (await userManager.GetRolesAsync(user)).FirstOrDefault();
            // The caller is an admin and can't demote/delete themselves → there is always at least one admin left.
            if (currentRole == Roles.Admin && req.Role != Roles.Admin && id == userManager.GetUserId(http.User))
                return Results.Problem("You can't remove your own admin role.", statusCode: 409);

            await using var tx = await db.Database.BeginTransactionAsync();
            var email = req.Email.Trim();
            user.DisplayName = req.DisplayName.Trim();
            user.Email = user.UserName = email;
            var result = await userManager.UpdateAsync(user); // also normalizes email/username and checks uniqueness
            if (!result.Succeeded) return IdentityProblem(result);
            if (currentRole != req.Role)
            {
                if (currentRole is not null) await userManager.RemoveFromRoleAsync(user, currentRole);
                await userManager.AddToRoleAsync(user, req.Role);
            }
            user.Sections.RemoveAll(s => !req.Sections.Contains(s.SectionId));
            user.Sections.AddRange(req.Sections.Where(sid => user.Sections.All(s => s.SectionId != sid))
                .Select(sid => new UserSection { UserId = id, SectionId = sid }));
            await db.SaveChangesAsync();
            await tx.CommitAsync();

            return Results.Ok(new UserDto(id, email, user.DisplayName, req.Role, user.Sections.Select(s => s.SectionId).ToArray()));
        });

        users.MapPost("/{id}/reset-password", async (string id, UserManager<AppUser> userManager) =>
        {
            var user = await userManager.FindByIdAsync(id);
            if (user is null) return Results.NotFound();
            var password = TemporaryPassword();
            var token = await userManager.GeneratePasswordResetTokenAsync(user);
            var result = await userManager.ResetPasswordAsync(user, token, password); // also signs out other sessions (security stamp)
            return result.Succeeded ? Results.Ok(new PasswordDto(password)) : IdentityProblem(result);
        });

        users.MapDelete("/{id}", async (string id, HttpContext http, AppDbContext db, UserManager<AppUser> userManager) =>
        {
            var user = await userManager.FindByIdAsync(id);
            if (user is null) return Results.NotFound();
            if (id == userManager.GetUserId(http.User)) return Results.Problem("You can't delete yourself.", statusCode: 409);
            var hasUpcoming = await db.Interviews.AnyAsync(i =>
                i.Status == InterviewStatus.Scheduled && i.Start > DateTime.UtcNow && i.Interviewers.Any(u => u.Id == id));
            if (hasUpcoming) return Results.Problem("User has upcoming interviews. Reassign them first.", statusCode: 409);

            await userManager.DeleteAsync(user); // availability + section assignments cascade
            return Results.NoContent();
        });
    }

    static async Task<IResult?> Validate(UserRequest req, AppDbContext db)
    {
        if (!Roles.All.Contains(req.Role))
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["Role"] = [$"Role must be one of: {string.Join(", ", Roles.All)}."] });
        var ids = req.Sections;
        if (await db.Sections.CountAsync(s => ids.Contains(s.Id)) != ids.Length)
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["SectionIds"] = ["Unknown section."] });
        return null;
    }

    // Duplicate email etc. → 409/400 with Identity's message.
    static IResult IdentityProblem(IdentityResult result) =>
        result.Errors.Any(e => e.Code is "DuplicateEmail" or "DuplicateUserName")
            ? Results.Problem("A user with this email already exists.", statusCode: 409)
            : Results.Problem(string.Join(" ", result.Errors.Select(e => e.Description)), statusCode: 400);

    // 12 random chars + one of each class Identity's default policy requires. No look-alike characters.
    static string TemporaryPassword() =>
        RandomNumberGenerator.GetString("abcdefghjkmnpqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ23456789", 12) + "aA7!";
}
