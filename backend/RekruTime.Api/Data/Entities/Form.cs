namespace RekruTime.Api.Data;

// Sections: the options are the club's sections; each chosen one becomes an Application.
public enum FieldType { Text, Textarea, Email, Phone, SingleChoice, MultiChoice, Sections }

// A recruitment form. Name, e-mail and GDPR consent are fixed on every form; Fields are the extra questions.
public class Form
{
    public int Id { get; set; }
    public required string Title { get; set; }
    public string? Description { get; set; }
    public bool IsOpen { get; set; }
    public List<FormField> Fields { get; set; } = []; // jsonb column "Schema"
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class FormField
{
    public required string Id { get; set; } // stable key of the answers – survives renames and reordering
    public FieldType Type { get; set; }
    public required string Label { get; set; }
    public bool Required { get; set; }
    public List<string> Options { get; set; } = []; // single/multi choice only
}

// One per recruit per form; submitting again replaces it.
public class FormResponse
{
    public int Id { get; set; }
    public int FormId { get; set; }
    public int RecruitId { get; set; }
    public List<FormAnswer> Answers { get; set; } = []; // jsonb
    public DateTime SubmittedAt { get; set; } = DateTime.UtcNow;
}

// Values: one entry for text fields, the chosen options for choices, section ids for Sections.
public class FormAnswer
{
    public required string FieldId { get; set; }
    public List<string> Values { get; set; } = [];
}
