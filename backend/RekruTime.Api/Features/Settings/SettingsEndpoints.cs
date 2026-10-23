using RekruTime.Api.Common;
using RekruTime.Api.Data;

namespace RekruTime.Api.Features.Settings;

public static class SettingsEndpoints
{
    public static void MapSettingsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/settings").WithTags("Settings").RequireAuthorization();

        group.MapGet("/recruitment", (SettingsService s) => s.GetPeriodAsync());

        group.MapPut("/recruitment", async (RecruitmentPeriodDto req, SettingsService s) =>
            (await s.SetPeriodAsync(req)).ToHttp()).RequireAuthorization(Roles.Admin);

        group.MapGet("/reminders", (SettingsService s) => s.GetRemindersAsync()).RequireAuthorization(Roles.Admin);

        group.MapPut("/reminders", (RemindersDto req, SettingsService s) => s.SetRemindersAsync(req)).RequireAuthorization(Roles.Admin);
    }
}
