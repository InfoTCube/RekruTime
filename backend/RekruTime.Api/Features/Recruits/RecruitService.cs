using Microsoft.EntityFrameworkCore;
using RekruTime.Api.Common;
using RekruTime.Api.Data;

namespace RekruTime.Api.Features.Recruits;

public record PurgeRequest(string Confirm);
public record PurgeResult(int DeletedRecruits);

// GDPR: erase a single recruit, or everything once the recruitment is over.
public class RecruitService(AppDbContext db)
{
    public const string PurgeConfirmation = "DELETE ALL";

    // Applications, interviews, booking links and their e-mails cascade.
    public async Task<Result> DeleteAsync(int id) =>
        await db.Recruits.Where(r => r.Id == id).ExecuteDeleteAsync() == 0 ? Error.NotFound() : Result.Ok;

    // All recruits, applications, interviews and booking links (anonymous ones too). Users, sections, availability stay.
    public async Task<Result<PurgeResult>> PurgeAsync(string confirm)
    {
        if (confirm != PurgeConfirmation) return Error.Validation("Confirm", $"Type \"{PurgeConfirmation}\" to confirm.");
        await using var tx = await db.Database.BeginTransactionAsync();
        var recruits = await db.Recruits.ExecuteDeleteAsync();
        await db.Interviews.ExecuteDeleteAsync();
        await db.BookingLinks.ExecuteDeleteAsync();
        await tx.CommitAsync();
        return new PurgeResult(recruits);
    }
}
