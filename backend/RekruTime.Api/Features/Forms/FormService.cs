using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using RekruTime.Api.Common;
using RekruTime.Api.Data;

namespace RekruTime.Api.Features.Forms;

// Id may be left out for a new field – the server makes one.
public record FieldDto(string? Id, FieldType Type, string Label, bool Required, List<string>? Options, string? FollowUp = null);

public record FormRequest(
    [Required, StringLength(200, MinimumLength = 1)] string Title,
    [StringLength(5000)] string? Description,
    [Required, MaxLength(100)] List<FieldDto> Fields);

public record FormDto(int Id, string Title, string? Description, bool IsOpen, List<FieldDto> Fields, DateTime CreatedAt, int Responses);

public record SetOpenRequest(bool IsOpen);

public partial class FormService(AppDbContext db)
{
    public async Task<List<FormDto>> ListAsync() =>
        (await db.Forms.OrderByDescending(f => f.CreatedAt).Select(f => new { Form = f, Responses = db.FormResponses.Count(r => r.FormId == f.Id) }).ToListAsync())
        .Select(x => ToDto(x.Form, x.Responses)).ToList();

    public async Task<Result<FormDto>> GetAsync(int id)
    {
        var f = await db.Forms.FindAsync(id);
        return f is null ? Error.NotFound() : ToDto(f, await ResponsesAsync(id));
    }

    public async Task<Result<FormDto>> CreateAsync(FormRequest req)
    {
        var fields = Fields(req.Fields);
        if (fields.Error is { } e) return e;
        var f = new Form { Title = req.Title.Trim(), Description = Blank(req.Description), Fields = fields.Value! };
        db.Forms.Add(f);
        await db.SaveChangesAsync();
        return ToDto(f, 0);
    }

    // Answers are keyed by field id, so renaming/reordering keeps them; a removed field's answers stay in the responses.
    public async Task<Result<FormDto>> UpdateAsync(int id, FormRequest req)
    {
        var f = await db.Forms.FindAsync(id);
        if (f is null) return Error.NotFound();
        var fields = Fields(req.Fields);
        if (fields.Error is { } e) return e;
        if (f.IsOpen && !fields.Value!.Any(x => x.Type == FieldType.Sections)) return NoSections;
        (f.Title, f.Description, f.Fields) = (req.Title.Trim(), Blank(req.Description), fields.Value!);
        await db.SaveChangesAsync();
        return ToDto(f, await ResponsesAsync(id));
    }

    public async Task<Result> SetOpenAsync(int id, bool isOpen)
    {
        var f = await db.Forms.FindAsync(id);
        if (f is null) return Error.NotFound();
        if (isOpen && !f.Fields.Any(x => x.Type == FieldType.Sections)) return NoSections;
        f.IsOpen = isOpen;
        await db.SaveChangesAsync();
        return Result.Ok;
    }

    public async Task<Result> DeleteAsync(int id)
    {
        var f = await db.Forms.FindAsync(id);
        if (f is null) return Error.NotFound();
        if (await ResponsesAsync(id) > 0) return Error.Conflict("The form has responses and can't be deleted – close it instead.");
        db.Forms.Remove(f);
        await db.SaveChangesAsync();
        return Result.Ok;
    }

    // Without a sections question nobody who fills the form would show up in any section's recruitment.
    static readonly Error NoSections = Error.Validation("Fields", "An open form needs a sections question.");

    static Result<List<FormField>> Fields(List<FieldDto> input)
    {
        var fields = new List<FormField>();
        foreach (var (x, i) in input.Select((x, i) => (x, i)))
        {
            var key = $"Fields[{i}]";
            var id = string.IsNullOrWhiteSpace(x.Id) ? Guid.NewGuid().ToString("N")[..8] : x.Id.Trim();
            if (!FieldId().IsMatch(id)) return Error.Validation(key, "Field id: up to 40 letters, digits, '-' or '_'.");
            if (fields.Any(f => f.Id == id)) return Error.Validation(key, $"Field id \"{id}\" is used twice.");
            var label = x.Label?.Trim() ?? "";
            if (label.Length is 0 or > 500) return Error.Validation(key, "Every question needs a label (up to 500 characters).");
            if (x.Type == FieldType.Sections && fields.Any(f => f.Type == FieldType.Sections)) return Error.Validation(key, "Only one sections question per form.");

            var options = new List<string>();
            if (x.Type is FieldType.SingleChoice or FieldType.MultiChoice)
            {
                options = (x.Options ?? []).Select(o => o.Trim()).Where(o => o.Length > 0).Distinct().ToList();
                if (options.Count == 0) return Error.Validation(key, $"\"{label}\" needs at least one option.");
                if (options.Count > 50 || options.Any(o => o.Length > 200)) return Error.Validation(key, "Up to 50 options of up to 200 characters.");
            }
            var followUp = string.IsNullOrWhiteSpace(x.FollowUp) ? null : x.FollowUp.Trim();
            if (followUp?.Length > 500) return Error.Validation(key, "The follow-up question can have up to 500 characters.");
            fields.Add(new FormField { Id = id, Type = x.Type, Label = label, Required = x.Required, Options = options, FollowUp = followUp });
        }
        return fields;
    }

    [GeneratedRegex("^[A-Za-z0-9_-]{1,40}$")]
    private static partial Regex FieldId();

    Task<int> ResponsesAsync(int formId) => db.FormResponses.CountAsync(r => r.FormId == formId);

    static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    static FormDto ToDto(Form f, int responses) => new(f.Id, f.Title, f.Description, f.IsOpen,
        f.Fields.Select(x => new FieldDto(x.Id, x.Type, x.Label, x.Required, x.Options, x.FollowUp)).ToList(), f.CreatedAt, responses);
}
