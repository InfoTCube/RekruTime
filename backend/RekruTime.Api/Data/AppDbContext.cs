using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace RekruTime.Api.Data;

public class AppUser : IdentityUser
{
    public string DisplayName { get; set; } = "";
}

public static class Roles
{
    public const string Admin = "Admin"; // board member
    public const string Recruiter = "Recruiter";
    public static readonly string[] All = [Admin, Recruiter];
}

public class AppDbContext(DbContextOptions<AppDbContext> options) : IdentityDbContext<AppUser>(options)
{
}
