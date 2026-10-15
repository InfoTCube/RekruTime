using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using RekruTime.Api.Common;
using RekruTime.Api.Data;

namespace RekruTime.Api.Features.Sections;

public record SectionRequest(
    [Required, StringLength(100, MinimumLength = 1)] string Name,
    [Range(5, 240)] int InterviewDurationMinutes = 30,
    [Range(0, 120)] int BufferMinutes = 0);

public record SectionDto(int Id, string Name, int InterviewDurationMinutes, int BufferMinutes);

public class SectionService(AppDbContext db)
{
    public Task<List<SectionDto>> ListAsync() =>
        db.Sections.OrderBy(s => s.Name).Select(s => new SectionDto(s.Id, s.Name, s.InterviewDurationMinutes, s.BufferMinutes)).ToListAsync();

    public async Task<Result<SectionDto>> CreateAsync(SectionRequest req)
    {
        var name = req.Name.Trim();
        if (await NameTakenAsync(name, null)) return NameConflict(name);
        var s = new Section { Name = name, InterviewDurationMinutes = req.InterviewDurationMinutes, BufferMinutes = req.BufferMinutes };
        db.Sections.Add(s);
        await db.SaveChangesAsync();
        return ToDto(s);
    }

    // Changing duration/buffer only affects future bookings; existing interviews keep their times.
    public async Task<Result<SectionDto>> UpdateAsync(int id, SectionRequest req)
    {
        var s = await db.Sections.FindAsync(id);
        if (s is null) return Error.NotFound();
        var name = req.Name.Trim();
        if (await NameTakenAsync(name, id)) return NameConflict(name);
        (s.Name, s.InterviewDurationMinutes, s.BufferMinutes) = (name, req.InterviewDurationMinutes, req.BufferMinutes);
        await db.SaveChangesAsync();
        return ToDto(s);
    }

    public async Task<Result> DeleteAsync(int id)
    {
        var s = await db.Sections.FindAsync(id);
        if (s is null) return Error.NotFound();
        // Recruitment data hangs off sections – refuse instead of cascading it away.
        var inUse = await db.Applications.AnyAsync(a => a.SectionId == id)
            || await db.Interviews.AnyAsync(i => i.Sections.Any(x => x.Id == id))
            || await db.BookingLinks.AnyAsync(l => l.Sections.Any(x => x.Id == id));
        if (inUse) return Error.Conflict("Section has booking links, applications or interviews and can't be deleted.");
        db.Sections.Remove(s); // user assignments go with it (cascade)
        await db.SaveChangesAsync();
        return Result.Ok;
    }

    static SectionDto ToDto(Section s) => new(s.Id, s.Name, s.InterviewDurationMinutes, s.BufferMinutes);

    Task<bool> NameTakenAsync(string name, int? exceptId) =>
        db.Sections.AnyAsync(s => s.Id != exceptId && s.Name.ToLower() == name.ToLower());

    static Error NameConflict(string name) => Error.Conflict($"Section \"{name}\" already exists.");
}
