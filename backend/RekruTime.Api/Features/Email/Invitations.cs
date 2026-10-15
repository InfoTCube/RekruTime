using System.Globalization;

namespace RekruTime.Api.Features.Email;

public record Template(string Subject, string Body);
public record RenderedEmail(string Subject, string Body, string Html);

public record InvitationValues(string? Name, string Link, IEnumerable<string> Sections, DateOnly? Deadline, string? Contact);

// Built-in invitation texts and rendering – pure, no DB (InvitationService loads/edits/queues).
public static class Invitations
{
    public const string Key = "invitation";

    // Adding a language = a default template here + a dictionary for the booking page (frontend/src/locales).
    public static readonly Dictionary<string, Template> Defaults = new()
    {
        ["pl"] = new(
            "Zaproszenie na rozmowę rekrutacyjną – {sections}",
            """
            Cześć {firstName}!

            Dziękujemy za zgłoszenie do naszego koła naukowego. Zapraszamy Cię na krótką rozmowę rekrutacyjną ({sections}).

            Wybierz dogodny termin tutaj:
            {link}

            Terminy są dostępne do {deadline}. Link jest osobisty – możesz zarezerwować jedną rozmowę.
            Jeśli żaden termin Ci nie pasuje, napisz do nas: {contact}

            Do zobaczenia!
            Zespół rekrutacyjny
            """),
        ["en"] = new(
            "Invitation to a recruitment interview – {sections}",
            """
            Hi {firstName}!

            Thank you for applying to our scientific circle. We'd like to invite you to a short recruitment interview ({sections}).

            Pick a time that suits you here:
            {link}

            Times are available until {deadline}. The link is personal – you can book one interview.
            If none of the times suit you, write to us: {contact}

            See you soon!
            The recruitment team
            """),
    };

    // Fixed parts of the HTML version around the editable text (EmailHtml).
    public static readonly Dictionary<string, HtmlTexts> Html = new()
    {
        ["pl"] = new("Wybierz termin", "Przycisk nie działa? Otwórz ten link:",
            "Dostajesz tę wiadomość, bo zgłosiłeś(-aś) się do rekrutacji. Link jest osobisty – nie przekazuj go dalej."),
        ["en"] = new("Choose a time", "Button not working? Open this link:",
            "You're receiving this because you applied to our recruitment. The link is personal – please don't forward it."),
    };

    public static readonly string[] Placeholders = ["{firstName}", "{name}", "{link}", "{sections}", "{deadline}", "{contact}"];

    // header = title bar of the HTML version (the sender name), null = none.
    public static RenderedEmail Render(Template t, InvitationValues v, string language, string? header = null)
    {
        var culture = CultureInfo.GetCultureInfo(language);
        var values = new Dictionary<string, string>
        {
            ["{firstName}"] = v.Name?.Trim().Split(' ')[0] ?? "",
            ["{name}"] = v.Name?.Trim() ?? "",
            ["{link}"] = v.Link,
            ["{sections}"] = string.Join(", ", v.Sections),
            ["{deadline}"] = v.Deadline?.ToString("d MMMM yyyy", culture) ?? "",
            ["{contact}"] = v.Contact ?? "",
        };
        var body = Fill(t.Body, values);
        return new(Fill(t.Subject, values), body, EmailHtml.Build(body, v.Link, Html.GetValueOrDefault(language, Html["pl"]), header));
    }

    // An empty value takes the space before it along ("Cześć {name}!" → "Cześć!" for anonymous links).
    static string Fill(string text, Dictionary<string, string> values)
    {
        foreach (var (key, value) in values)
            text = value == "" ? text.Replace(" " + key, "").Replace(key, "") : text.Replace(key, value);
        return text;
    }
}
