using Microsoft.EntityFrameworkCore;
using RekruTime.Api.Data;

namespace RekruTime.Api.Features.Recruits;

public record PurgeRequest(string Confirm);

// GDPR: admins can erase a single recruit, or everything once the recruitment is over.
public static class RecruitEndpoints
{
    public const string PurgeConfirmation = "DELETE ALL";

    public static void MapRecruitEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/recruits").WithTags("Recruits").RequireAuthorization(Roles.Admin);

        // Applications, interviews and their booking links cascade.
        group.MapDelete("/{id:int}", async (int id, AppDbContext db) =>
            await db.Recruits.Where(r => r.Id == id).ExecuteDeleteAsync() == 0 ? Results.NotFound() : Results.NoContent());

        // End of recruitment: all recruits, applications, interviews and booking links (anonymous ones too).
        group.MapPost("/purge", async (PurgeRequest req, AppDbContext db) =>
        {
            if (req.Confirm != PurgeConfirmation)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["Confirm"] = [$"Type \"{PurgeConfirmation}\" to confirm."] });
            await using var tx = await db.Database.BeginTransactionAsync();
            var recruits = await db.Recruits.ExecuteDeleteAsync();
            await db.Interviews.ExecuteDeleteAsync();
            await db.BookingLinks.ExecuteDeleteAsync();
            await tx.CommitAsync();
            return Results.Ok(new { deletedRecruits = recruits });
        });
    }
}
