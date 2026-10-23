namespace RekruTime.Api.Features.Email;

public record DecisionValues(string? Name, IEnumerable<string> Sections, string? Contact);

// Built-in accepted/rejected texts and rendering – pure, no DB (DecisionService queues them).
public static class Decisions
{
    public const string Accepted = "accepted", Rejected = "rejected";

    public static readonly Dictionary<string, Template> AcceptedDefaults = new()
    {
        ["pl"] = new(
            "Wynik rekrutacji – witamy w kole!",
            """
            Cześć {firstName}!

            Mamy dobrą wiadomość: przyjęliśmy Cię do naszego koła naukowego ({sections}). Gratulacje!

            Wkrótce odezwiemy się z informacjami o pierwszym spotkaniu. Jeśli masz pytania, napisz do nas: {contact}

            Do zobaczenia!
            Zespół rekrutacyjny
            """),
        ["en"] = new(
            "Recruitment result – welcome aboard!",
            """
            Hi {firstName}!

            Good news: you've been accepted to our scientific circle ({sections}). Congratulations!

            We'll get in touch soon with details about the first meeting. If you have any questions, write to us: {contact}

            See you soon!
            The recruitment team
            """),
    };

    public static readonly Dictionary<string, Template> RejectedDefaults = new()
    {
        ["pl"] = new(
            "Wynik rekrutacji",
            """
            Cześć {firstName}!

            Dziękujemy za udział w rekrutacji i za rozmowę z nami. Tym razem nie możemy zaproponować Ci miejsca w kole ({sections}).

            To nie zamyka drogi – zachęcamy do ponownego zgłoszenia w kolejnej rekrutacji. Jeśli chcesz porozmawiać o wyniku, napisz do nas: {contact}

            Powodzenia!
            Zespół rekrutacyjny
            """),
        ["en"] = new(
            "Recruitment result",
            """
            Hi {firstName}!

            Thank you for taking part in our recruitment and for talking with us. Unfortunately, we can't offer you a place this time ({sections}).

            This doesn't close the door – you're welcome to apply again in the next recruitment. If you'd like to talk about the result, write to us: {contact}

            Good luck!
            The recruitment team
            """),
    };

    // Fixed parts of the HTML version (no button – a decision has no link).
    public static readonly Dictionary<string, HtmlTexts> Html = new()
    {
        ["pl"] = new("Rekrutacja", "", "", "Dostajesz tę wiadomość, bo brałeś(-aś) udział w naszej rekrutacji."),
        ["en"] = new("Recruitment", "", "", "You're receiving this because you took part in our recruitment."),
    };

    public static RenderedEmail Render(Template t, DecisionValues v, string language, string? clubName = null)
    {
        var values = new Dictionary<string, string>
        {
            ["{firstName}"] = v.Name?.Trim().Split(' ')[0] ?? "",
            ["{name}"] = v.Name?.Trim() ?? "",
            ["{sections}"] = string.Join(", ", v.Sections),
            ["{contact}"] = v.Contact ?? "",
        };
        var body = EmailTemplates.Fill(t.Body, values);
        var html = Html.GetValueOrDefault(language, Html["pl"]);
        var header = string.IsNullOrWhiteSpace(clubName) ? html.Recruitment : $"{html.Recruitment} – {clubName.Trim()}";
        return new(EmailTemplates.Fill(t.Subject, values), body, EmailHtml.Build(body, null, html, header));
    }
}
