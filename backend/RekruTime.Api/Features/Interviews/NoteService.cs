using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using RekruTime.Api.Common;
using RekruTime.Api.Data;

namespace RekruTime.Api.Features.Interviews;

public record NoteRequest([StringLength(20000)] string? Text);
public record NoteDto(string? AuthorId, string AuthorName, string Text, DateTime UpdatedAt);
public record NotesDto(bool CanWrite, List<NoteDto> Notes);

// Interviewers write their own note; interviewers, leaders of the interview's sections and admins read all notes.
public class NoteService(AppDbContext db)
{
    public async Task<Result<NotesDto>> ListAsync(int interviewId, string userId, bool isAdmin)
    {
        var access = await AccessAsync(interviewId, userId);
        if (access is null) return Error.NotFound();
        if (!access.IsInterviewer && !access.IsLeader && !isAdmin) return Error.Forbidden("Only the interviewers, section leaders and admins can read these notes.");

        var notes = await db.InterviewNotes.Where(n => n.InterviewId == interviewId).OrderBy(n => n.UpdatedAt)
            .Select(n => new NoteDto(n.AuthorId, n.Author != null ? n.Author.DisplayName : "(deleted user)", n.Text, n.UpdatedAt))
            .ToListAsync();
        return new NotesDto(access.IsInterviewer, notes);
    }

    // Upsert of the caller's note; blank text deletes it.
    public async Task<Result<NoteDto?>> SaveAsync(int interviewId, string userId, NoteRequest req)
    {
        var access = await AccessAsync(interviewId, userId);
        if (access is null) return Error.NotFound();
        if (!access.IsInterviewer) return Error.Forbidden("Only the interviewers can write notes on this interview.");

        var note = await db.InterviewNotes.Include(n => n.Author).FirstOrDefaultAsync(n => n.InterviewId == interviewId && n.AuthorId == userId);
        var text = req.Text?.Trim() ?? "";
        if (text.Length == 0)
        {
            if (note is not null) db.InterviewNotes.Remove(note);
            await db.SaveChangesAsync();
            return (NoteDto?)null;
        }

        if (note is null) db.InterviewNotes.Add(note = new InterviewNote { InterviewId = interviewId, AuthorId = userId });
        note.Text = text;
        note.UpdatedAt = DateTime.UtcNow;
        try
        {
            await db.SaveChangesAsync();
        }
        catch (Exception e) when (DbErrors.IsUniqueViolation(e))
        {
            return Error.Conflict("Your note was just saved from another tab. Reload and try again.");
        }
        var author = note.Author?.DisplayName ?? await db.Users.Where(u => u.Id == userId).Select(u => u.DisplayName).SingleAsync();
        return new NoteDto(userId, author, note.Text, note.UpdatedAt);
    }

    record Access(bool IsInterviewer, bool IsLeader);

    Task<Access?> AccessAsync(int interviewId, string userId) =>
        db.Interviews.Where(i => i.Id == interviewId).Select(i => new Access(
            i.Interviewers.Any(u => u.Id == userId),
            db.UserSections.Any(us => us.UserId == userId && us.IsLeader && i.Sections.Any(s => s.Id == us.SectionId))))
        .SingleOrDefaultAsync();
}
