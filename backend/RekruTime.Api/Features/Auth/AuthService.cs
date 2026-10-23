using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using RekruTime.Api.Common;
using RekruTime.Api.Data;

namespace RekruTime.Api.Features.Auth;

public record LoginRequest(string Email, string Password);
public record SectionRef(int Id, string Name);
public record MySection(int Id, string Name, bool IsLeader);
public record MeResponse(string Id, string Email, string DisplayName, IList<string> Roles, List<MySection> Sections, bool MustChangePassword);
public record ChangePasswordRequest([Required] string CurrentPassword, [Required, StringLength(128)] string NewPassword);

public class AuthService(AppDbContext db, UserManager<AppUser> users, SignInManager<AppUser> signIn)
{
    // Unknown e-mail and wrong password look the same (401); repeated failures lock the account for a while.
    public async Task<Result<MeResponse>> LoginAsync(LoginRequest req)
    {
        var user = await users.FindByEmailAsync(req.Email);
        if (user is null) return Error.Unauthorized();
        var result = await signIn.PasswordSignInAsync(user, req.Password, isPersistent: true, lockoutOnFailure: true);
        if (result.IsLockedOut) return Error.TooManyRequests("Too many failed attempts, try again later.");
        return result.Succeeded ? await MeAsync(user) : Error.Unauthorized();
    }

    public Task LogoutAsync() => signIn.SignOutAsync();

    // Own password; Identity's rules apply. Other sessions are signed out (new security stamp), this one stays.
    public async Task<Result> ChangePasswordAsync(string userId, ChangePasswordRequest req)
    {
        if (await users.FindByIdAsync(userId) is not { } user) return Error.Unauthorized();
        if (req.NewPassword == req.CurrentPassword) return Error.Validation("NewPassword", "Choose a different password than the current one.");
        var result = await users.ChangePasswordAsync(user, req.CurrentPassword, req.NewPassword);
        if (!result.Succeeded)
            return result.Errors.Any(e => e.Code == "PasswordMismatch")
                ? Error.Validation("CurrentPassword", "The current password is wrong.")
                : Error.Validation("NewPassword", string.Join(" ", result.Errors.Select(e => e.Description)));
        user.MustChangePassword = false;
        await users.UpdateAsync(user);
        await signIn.RefreshSignInAsync(user);
        return Result.Ok;
    }

    public async Task<Result<MeResponse>> MeAsync(string userId) =>
        await users.FindByIdAsync(userId) is { } user ? await MeAsync(user) : Error.Unauthorized();

    async Task<MeResponse> MeAsync(AppUser user) =>
        new(user.Id, user.Email!, user.DisplayName, await users.GetRolesAsync(user),
            await db.UserSections.Where(us => us.UserId == user.Id).OrderBy(us => us.Section.Name)
                .Select(us => new MySection(us.SectionId, us.Section.Name, us.IsLeader)).ToListAsync(), user.MustChangePassword);
}
