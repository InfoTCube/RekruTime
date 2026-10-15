using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using RekruTime.Api.Common;
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

// ponytail: admin hands out temporary passwords until set-password e-mails replace them (#37).
public class UserService(AppDbContext db, UserManager<AppUser> users)
{
    public async Task<List<UserDto>> ListAsync()
    {
        var list = await db.Users.OrderBy(u => u.DisplayName)
            .Select(u => new
            {
                u.Id, u.Email, u.DisplayName,
                Role = db.UserRoles.Where(ur => ur.UserId == u.Id).Join(db.Roles, ur => ur.RoleId, r => r.Id, (ur, r) => r.Name).FirstOrDefault(),
                SectionIds = u.Sections.Select(s => s.SectionId).ToArray(),
            })
            .ToListAsync();
        return list.Select(u => new UserDto(u.Id, u.Email!, u.DisplayName, u.Role ?? "", u.SectionIds)).ToList();
    }

    public async Task<Result<CreatedUserDto>> CreateAsync(UserRequest req)
    {
        if (await ValidateAsync(req) is { } invalid) return invalid;

        await using var tx = await db.Database.BeginTransactionAsync();
        var email = req.Email.Trim();
        var user = new AppUser { UserName = email, Email = email, EmailConfirmed = true, DisplayName = req.DisplayName.Trim() };
        var password = TemporaryPassword();
        var result = await users.CreateAsync(user, password);
        if (!result.Succeeded) return IdentityError(result);
        await users.AddToRoleAsync(user, req.Role);
        db.UserSections.AddRange(req.Sections.Select(id => new UserSection { UserId = user.Id, SectionId = id }));
        await db.SaveChangesAsync();
        await tx.CommitAsync();

        return new CreatedUserDto(new UserDto(user.Id, email, user.DisplayName, req.Role, req.Sections), password);
    }

    public async Task<Result<UserDto>> UpdateAsync(string id, UserRequest req, string actingUserId)
    {
        var user = await db.Users.Include(u => u.Sections).FirstOrDefaultAsync(u => u.Id == id);
        if (user is null) return Error.NotFound();
        if (await ValidateAsync(req) is { } invalid) return invalid;

        var currentRole = (await users.GetRolesAsync(user)).FirstOrDefault();
        // The caller is an admin and can't demote/delete themselves → there is always at least one admin left.
        if (currentRole == Roles.Admin && req.Role != Roles.Admin && id == actingUserId)
            return Error.Conflict("You can't remove your own admin role.");

        await using var tx = await db.Database.BeginTransactionAsync();
        var email = req.Email.Trim();
        user.DisplayName = req.DisplayName.Trim();
        user.Email = user.UserName = email;
        var result = await users.UpdateAsync(user); // also normalizes email/username and checks uniqueness
        if (!result.Succeeded) return IdentityError(result);
        if (currentRole != req.Role)
        {
            if (currentRole is not null) await users.RemoveFromRoleAsync(user, currentRole);
            await users.AddToRoleAsync(user, req.Role);
        }
        user.Sections.RemoveAll(s => !req.Sections.Contains(s.SectionId));
        user.Sections.AddRange(req.Sections.Where(sid => user.Sections.All(s => s.SectionId != sid))
            .Select(sid => new UserSection { UserId = id, SectionId = sid }));
        await db.SaveChangesAsync();
        await tx.CommitAsync();

        return new UserDto(id, email, user.DisplayName, req.Role, user.Sections.Select(s => s.SectionId).ToArray());
    }

    public async Task<Result<PasswordDto>> ResetPasswordAsync(string id)
    {
        var user = await users.FindByIdAsync(id);
        if (user is null) return Error.NotFound();
        var password = TemporaryPassword();
        var token = await users.GeneratePasswordResetTokenAsync(user);
        var result = await users.ResetPasswordAsync(user, token, password); // also signs out other sessions (security stamp)
        return result.Succeeded ? new PasswordDto(password) : IdentityError(result);
    }

    public async Task<Result> DeleteAsync(string id, string actingUserId)
    {
        var user = await users.FindByIdAsync(id);
        if (user is null) return Error.NotFound();
        if (id == actingUserId) return Error.Conflict("You can't delete yourself.");
        var hasUpcoming = await db.Interviews.AnyAsync(i =>
            i.Status == InterviewStatus.Scheduled && i.Start > DateTime.UtcNow && i.Interviewers.Any(u => u.Id == id));
        if (hasUpcoming) return Error.Conflict("User has upcoming interviews. Reassign them first.");

        await users.DeleteAsync(user); // availability + section assignments cascade
        return Result.Ok;
    }

    async Task<Error?> ValidateAsync(UserRequest req)
    {
        if (!Roles.All.Contains(req.Role)) return Error.Validation("Role", $"Role must be one of: {string.Join(", ", Roles.All)}.");
        var ids = req.Sections;
        if (await db.Sections.CountAsync(s => ids.Contains(s.Id)) != ids.Length) return Error.Validation("SectionIds", "Unknown section.");
        return null;
    }

    // Duplicate email etc. → 409/400 with Identity's message.
    static Error IdentityError(IdentityResult result) =>
        result.Errors.Any(e => e.Code is "DuplicateEmail" or "DuplicateUserName")
            ? Error.Conflict("A user with this email already exists.")
            : Error.BadRequest(string.Join(" ", result.Errors.Select(e => e.Description)));

    // 12 random chars + one of each class Identity's default policy requires. No look-alike characters.
    static string TemporaryPassword() =>
        RandomNumberGenerator.GetString("abcdefghjkmnpqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ23456789", 12) + "aA7!";
}
