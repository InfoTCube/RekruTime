using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using RekruTime.Api.Data;

namespace RekruTime.Api.Features.Sections;

public record SectionRequest(
    [Required, StringLength(100, MinimumLength = 1)] string Name,
    [Range(5, 240)] int InterviewDurationMinutes = 30,
    [Range(0, 120)] int BufferMinutes = 0);

public record SectionDto(int Id, string Name, int InterviewDurationMinutes, int BufferMinutes);

public static class SectionEndpoints
{
    public static void MapSectionEndpoints(this IEndpointRouteBuilder app)
    {
        var sections = app.MapGroup("/api/sections").WithTags("Sections").RequireAuthorization();

        sections.MapGet("/", (AppDbContext db) =>
            db.Sections.OrderBy(s => s.Name).Select(s => new SectionDto(s.Id, s.Name, s.InterviewDurationMinutes, s.BufferMinutes)).ToListAsync());

        sections.MapPost("/", async (SectionRequest req, AppDbContext db) =>
        {
            var name = req.Name.Trim();
            if (await NameTaken(db, name, null)) return NameConflict(name);
            var s = new Section { Name = name, InterviewDurationMinutes = req.InterviewDurationMinutes, BufferMinutes = req.BufferMinutes };
            db.Sections.Add(s);
            await db.SaveChangesAsync();
            return Results.Created($"/api/sections/{s.Id}", ToDto(s));
        }).RequireAuthorization(Roles.Admin);

        sections.MapPut("/{id:int}", async (int id, SectionRequest req, AppDbContext db) =>
        {
            var s = await db.Sections.FindAsync(id);
            if (s is null) return Results.NotFound();
            var name = req.Name.Trim();
            if (await NameTaken(db, name, id)) return NameConflict(name);
            // Changing duration/buffer only affects future bookings; existing interviews keep their times.
            (s.Name, s.InterviewDurationMinutes, s.BufferMinutes) = (name, req.InterviewDurationMinutes, req.BufferMinutes);
            await db.SaveChangesAsync();
            return Results.Ok(ToDto(s));
        }).RequireAuthorization(Roles.Admin);

        sections.MapDelete("/{id:int}", async (int id, AppDbContext db) =>
        {
            var s = await db.Sections.FindAsync(id);
            if (s is null) return Results.NotFound();
            // Recruitment data hangs off sections – refuse instead of cascading it away.
            var inUse = await db.Applications.AnyAsync(a => a.SectionId == id)
                || await db.Interviews.AnyAsync(i => i.Sections.Any(x => x.Id == id))
                || await db.BookingLinks.AnyAsync(l => l.Sections.Any(x => x.Id == id));
            if (inUse) return Results.Problem("Section has booking links, applications or interviews and can't be deleted.", statusCode: 409);
            db.Sections.Remove(s); // user assignments go with it (cascade)
            await db.SaveChangesAsync();
            return Results.NoContent();
        }).RequireAuthorization(Roles.Admin);
    }

    static SectionDto ToDto(Section s) => new(s.Id, s.Name, s.InterviewDurationMinutes, s.BufferMinutes);

    static Task<bool> NameTaken(AppDbContext db, string name, int? exceptId) =>
        db.Sections.AnyAsync(s => s.Id != exceptId && s.Name.ToLower() == name.ToLower());

    static IResult NameConflict(string name) => Results.Problem($"Section \"{name}\" already exists.", statusCode: 409);
}
