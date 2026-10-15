namespace RekruTime.Api.Data;

public static class Roles
{
    public const string Admin = "Admin"; // board member
    public const string Recruiter = "Recruiter";
    public static readonly string[] All = [Admin, Recruiter];
}
