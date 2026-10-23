using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RekruTime.Api.Data;

namespace RekruTime.Api.Features.Email;

// Day-before reminders to recruits (when an admin turned them on). An interview gets one reminder; booking or
// moving it inside the last day counts as reminded – the confirmation / new-time e-mail just told them.
public class ReminderJob(AppDbContext db, BookingEmailService emails, IOptions<EmailOptions> email)
{
    public static readonly TimeSpan Before = TimeSpan.FromHours(24);

    // Call whenever an interview gets a (new) time.
    public static void Reset(Interview i, DateTime now) => i.ReminderSentAt = i.Start - now <= Before ? now : null;

    public async Task<int> RunAsync(DateTime now)
    {
        if (!email.Value.IsConfigured || (await db.RecruitmentSettings.FindAsync(1))?.RemindersEnabled != true) return 0;
        var due = await db.Interviews
            .Where(i => i.Status == InterviewStatus.Scheduled && i.ReminderSentAt == null && i.Start > now && i.Start <= now + Before)
            .ToListAsync();
        foreach (var i in due)
        {
            await emails.QueueAsync(i.Id, InterviewChange.Reminder);
            i.ReminderSentAt = now;
            await db.SaveChangesAsync(); // per interview, so a failure never re-sends the ones before it
        }
        return due.Count;
    }
}

public class ReminderService(IServiceScopeFactory scopes, ILogger<ReminderService> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<ReminderJob>().RunAsync(DateTime.UtcNow);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                log.LogError(e, "Interview reminders failed");
            }
            await Task.Delay(TimeSpan.FromMinutes(5), ct);
        }
    }
}
