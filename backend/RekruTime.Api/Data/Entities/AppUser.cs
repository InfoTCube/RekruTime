using Microsoft.AspNetCore.Identity;

namespace RekruTime.Api.Data;

public class AppUser : IdentityUser
{
    public string DisplayName { get; set; } = "";
    public List<UserSection> Sections { get; set; } = [];
}
