namespace RekruTime.Api.Data;

// An admin's edited version of a built-in template (Features/Email/Invitations.cs). No row = built-in default.
public class EmailTemplate
{
    public required string Key { get; set; }      // e.g. "invitation"
    public required string Language { get; set; } // "pl", "en"
    public required string Subject { get; set; }
    public required string Body { get; set; }
}
