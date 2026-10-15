using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using RekruTime.Api.Common;
using RekruTime.Api.Data;

namespace RekruTime.Api.Features.Auth;

public record LoginRequest(string Email, string Password);
public record SectionRef(int Id, string Name);
public record MeResponse(string Id, string Email, string DisplayName, IList<string> Roles, List<SectionRef> Sections);

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

    public async Task<Result<MeResponse>> MeAsync(string userId) =>
        await users.FindByIdAsync(userId) is { } user ? await MeAsync(user) : Error.Unauthorized();

    async Task<MeResponse> MeAsync(AppUser user) =>
        new(user.Id, user.Email!, user.DisplayName, await users.GetRolesAsync(user),
            await db.UserSections.Where(us => us.UserId == user.Id).OrderBy(us => us.Section.Name)
                .Select(us => new SectionRef(us.SectionId, us.Section.Name)).ToListAsync());
}
