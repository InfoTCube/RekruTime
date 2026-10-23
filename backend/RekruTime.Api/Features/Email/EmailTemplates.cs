namespace RekruTime.Api.Features.Email;

// Every e-mail admins can edit: key → built-in default per language (an EmailTemplate row overrides it).
public static class EmailTemplates
{
    public static readonly Dictionary<string, Dictionary<string, Template>> Defaults = new()
    {
        [Invitations.Key] = Invitations.Defaults,
        [Decisions.Accepted] = Decisions.AcceptedDefaults,
        [Decisions.Rejected] = Decisions.RejectedDefaults,
    };

    // Without it the e-mail is useless (the invitation must contain the booking link).
    public static string? RequiredPlaceholder(string key) => key == Invitations.Key ? "{link}" : null;

    // An empty value takes the space before it along ("Cześć {name}!" → "Cześć!" for anonymous links).
    public static string Fill(string text, Dictionary<string, string> values)
    {
        foreach (var (key, value) in values)
            text = value == "" ? text.Replace(" " + key, "").Replace(key, "") : text.Replace(key, value);
        return text;
    }
}
