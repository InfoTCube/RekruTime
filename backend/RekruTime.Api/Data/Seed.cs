using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace RekruTime.Api.Data;

public static class Seed
{
    // ponytail: migrate on startup – fine for a single instance; move to a deploy step if we ever scale out.
    public static async Task RunAsync(IServiceProvider services, IConfiguration config)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync();

        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        foreach (var role in Roles.All)
            if (!await roles.RoleExistsAsync(role))
                await roles.CreateAsync(new IdentityRole(role));

        // First admin, only when there is no admin yet.
        var email = config["SeedAdmin:Email"];
        var password = config["SeedAdmin:Password"];
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(password) || (await users.GetUsersInRoleAsync(Roles.Admin)).Count > 0)
            return;

        var admin = new AppUser { UserName = email, Email = email, EmailConfirmed = true, DisplayName = config["SeedAdmin:DisplayName"] ?? "Admin" };
        var result = await users.CreateAsync(admin, password);
        if (!result.Succeeded)
            throw new InvalidOperationException("Seeding admin failed: " + string.Join("; ", result.Errors.Select(e => e.Description)));
        await users.AddToRoleAsync(admin, Roles.Admin);
    }
}
