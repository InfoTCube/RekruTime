using Microsoft.AspNetCore.Identity;

namespace RekruTime.Api.Data;

public class AppUser : IdentityUser
{
    public string DisplayName { get; set; } = "";
    public bool MustChangePassword { get; set; } // has a temporary password an admin created – set own one first
    public List<UserSection> Sections { get; set; } = [];
}
