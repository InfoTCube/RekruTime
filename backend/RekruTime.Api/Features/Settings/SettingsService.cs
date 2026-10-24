using System.ComponentModel.DataAnnotations;
using RekruTime.Api.Common;
using RekruTime.Api.Data;

namespace RekruTime.Api.Features.Settings;

// MinNoticeHours: how far ahead recruits must book (0 = up to the last moment, max. 2 weeks).
public record RecruitmentPeriodDto(DateOnly? InterviewsFrom, DateOnly? InterviewsTo, [Range(0, 336)] int MinNoticeHours = 12);
public record RemindersDto(bool Enabled);

public class SettingsService(AppDbContext db)
{
    public async Task<RecruitmentPeriodDto> GetPeriodAsync()
    {
        var s = await db.RecruitmentSettings.FindAsync(1);
        return new(s?.InterviewsFrom, s?.InterviewsTo, s?.MinNoticeHours ?? 12);
    }

    // Both dates set → booking open for that period; both null → booking closed.
    public async Task<Result<RecruitmentPeriodDto>> SetPeriodAsync(RecruitmentPeriodDto req)
    {
        if (req.InterviewsFrom is null != req.InterviewsTo is null || req.InterviewsFrom > req.InterviewsTo)
            return Error.Validation("InterviewsTo", "Set both dates, with the end on or after the start (or clear both).");

        var s = await db.RecruitmentSettings.FindAsync(1);
        if (s is null) db.RecruitmentSettings.Add(s = new RecruitmentSettings());
        (s.InterviewsFrom, s.InterviewsTo, s.MinNoticeHours) = (req.InterviewsFrom, req.InterviewsTo, req.MinNoticeHours);
        await db.SaveChangesAsync();
        return req;
    }

    public async Task<RemindersDto> GetRemindersAsync() => new((await db.RecruitmentSettings.FindAsync(1))?.RemindersEnabled ?? false);

    public async Task<RemindersDto> SetRemindersAsync(RemindersDto req)
    {
        var s = await db.RecruitmentSettings.FindAsync(1);
        if (s is null) db.RecruitmentSettings.Add(s = new RecruitmentSettings());
        s.RemindersEnabled = req.Enabled;
        await db.SaveChangesAsync();
        return req;
    }
}
