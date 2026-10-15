using Microsoft.EntityFrameworkCore;
using RekruTime.Api.Data;

namespace RekruTime.Api.Features.Settings;

public record RecruitmentPeriodDto(DateOnly? InterviewsFrom, DateOnly? InterviewsTo);

public static class SettingsEndpoints
{
    public static void MapSettingsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/settings").WithTags("Settings").RequireAuthorization();

        group.MapGet("/recruitment", async (AppDbContext db) =>
        {
            var s = await db.RecruitmentSettings.FindAsync(1);
            return new RecruitmentPeriodDto(s?.InterviewsFrom, s?.InterviewsTo);
        });

        // Both dates set → booking open for that period; both null → booking closed.
        group.MapPut("/recruitment", async (RecruitmentPeriodDto req, AppDbContext db) =>
        {
            if (req.InterviewsFrom is null != req.InterviewsTo is null || req.InterviewsFrom > req.InterviewsTo)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["InterviewsTo"] = ["Set both dates, with the end on or after the start (or clear both)."] });

            var s = await db.RecruitmentSettings.FindAsync(1);
            if (s is null) db.RecruitmentSettings.Add(s = new RecruitmentSettings());
            (s.InterviewsFrom, s.InterviewsTo) = (req.InterviewsFrom, req.InterviewsTo);
            await db.SaveChangesAsync();
            return Results.Ok(req);
        }).RequireAuthorization(Roles.Admin);
    }
}
