using RekruTime.Api.Common;
using RekruTime.Api.Data;

namespace RekruTime.Api.Features.Settings;

public record RecruitmentPeriodDto(DateOnly? InterviewsFrom, DateOnly? InterviewsTo);

public class SettingsService(AppDbContext db)
{
    public async Task<RecruitmentPeriodDto> GetPeriodAsync()
    {
        var s = await db.RecruitmentSettings.FindAsync(1);
        return new(s?.InterviewsFrom, s?.InterviewsTo);
    }

    // Both dates set → booking open for that period; both null → booking closed.
    public async Task<Result<RecruitmentPeriodDto>> SetPeriodAsync(RecruitmentPeriodDto req)
    {
        if (req.InterviewsFrom is null != req.InterviewsTo is null || req.InterviewsFrom > req.InterviewsTo)
            return Error.Validation("InterviewsTo", "Set both dates, with the end on or after the start (or clear both).");

        var s = await db.RecruitmentSettings.FindAsync(1);
        if (s is null) db.RecruitmentSettings.Add(s = new RecruitmentSettings());
        (s.InterviewsFrom, s.InterviewsTo) = (req.InterviewsFrom, req.InterviewsTo);
        await db.SaveChangesAsync();
        return req;
    }
}
