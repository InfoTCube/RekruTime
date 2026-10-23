using System.Globalization;

namespace RekruTime.Api.Features.Email;

public record BookingValues(string? Name, DateTime StartUtc, DateTime EndUtc, IEnumerable<string> Sections, string? Location, string? Contact, string? Link);
public record StaffValues(string Recruit, DateTime StartUtc, DateTime EndUtc, IEnumerable<string> Sections, IEnumerable<string> Team, string? Link);

public enum StaffNotice { Booked, Moved, Cancelled, Added, Removed }

// Booking e-mails – pure, no DB (BookingEmailService queues them). Recruit texts are editable templates;
// the interviewers' notices are fixed English text like the rest of the staff UI.
public static class BookingMails
{
    public const string Confirmation = "confirmation", Rescheduled = "rescheduled", Cancelled = "cancelled", Reminder = "reminder";

    public static readonly Dictionary<string, Template> ConfirmationDefaults = new()
    {
        ["pl"] = new(
            "Potwierdzenie rozmowy rekrutacyjnej – {date}",
            """
            Cześć {firstName}!

            Twoja rozmowa rekrutacyjna ({sections}) jest zarezerwowana:

            {date}, godz. {time}
            Miejsce: {location}

            W załączniku jest wydarzenie do dodania do kalendarza. Szczegóły rezerwacji są pod Twoim linkiem:
            {link}

            Jeśli nie możesz przyjść, napisz do nas: {contact}

            Do zobaczenia!
            Zespół rekrutacyjny
            """),
        ["en"] = new(
            "Your recruitment interview – {date}",
            """
            Hi {firstName}!

            Your recruitment interview ({sections}) is booked:

            {date}, {time}
            Place: {location}

            The attached event adds it to your calendar. Your booking details are under your link:
            {link}

            If you can't make it, write to us: {contact}

            See you soon!
            The recruitment team
            """),
    };

    public static readonly Dictionary<string, Template> RescheduledDefaults = new()
    {
        ["pl"] = new(
            "Nowy termin rozmowy rekrutacyjnej – {date}",
            """
            Cześć {firstName}!

            Zmieniliśmy termin Twojej rozmowy rekrutacyjnej ({sections}). Nowy termin:

            {date}, godz. {time}
            Miejsce: {location}

            W załączniku jest zaktualizowane wydarzenie do kalendarza. Jeśli nowy termin Ci nie pasuje, napisz do nas: {contact}

            Do zobaczenia!
            Zespół rekrutacyjny
            """),
        ["en"] = new(
            "New time for your recruitment interview – {date}",
            """
            Hi {firstName}!

            We've moved your recruitment interview ({sections}). The new time:

            {date}, {time}
            Place: {location}

            The attached event updates your calendar. If the new time doesn't suit you, write to us: {contact}

            See you soon!
            The recruitment team
            """),
    };

    public static readonly Dictionary<string, Template> CancelledDefaults = new()
    {
        ["pl"] = new(
            "Rozmowa rekrutacyjna odwołana",
            """
            Cześć {firstName}!

            Twoja rozmowa rekrutacyjna ({sections}) zaplanowana na {date}, godz. {time} została odwołana.

            Możesz wybrać nowy termin pod swoim linkiem:
            {link}

            Jeśli masz pytania, napisz do nas: {contact}

            Zespół rekrutacyjny
            """),
        ["en"] = new(
            "Your recruitment interview was cancelled",
            """
            Hi {firstName}!

            Your recruitment interview ({sections}) on {date}, {time} has been cancelled.

            You can pick a new time under your link:
            {link}

            If you have any questions, write to us: {contact}

            The recruitment team
            """),
    };

    public static readonly Dictionary<string, Template> ReminderDefaults = new()
    {
        ["pl"] = new(
            "Przypomnienie: rozmowa rekrutacyjna – {date}",
            """
            Cześć {firstName}!

            Przypominamy o Twojej rozmowie rekrutacyjnej ({sections}):

            {date}, godz. {time}
            Miejsce: {location}

            Szczegóły są pod Twoim linkiem:
            {link}

            Jeśli nie możesz przyjść, daj nam znać jak najszybciej: {contact}

            Do zobaczenia!
            Zespół rekrutacyjny
            """),
        ["en"] = new(
            "Reminder: your recruitment interview – {date}",
            """
            Hi {firstName}!

            A quick reminder about your recruitment interview ({sections}):

            {date}, {time}
            Place: {location}

            The details are under your link:
            {link}

            If you can't make it, please let us know as soon as possible: {contact}

            See you soon!
            The recruitment team
            """),
    };

    public static readonly Dictionary<string, HtmlTexts> Html = new()
    {
        ["pl"] = new("Rekrutacja", "Otwórz rezerwację", "Przycisk nie działa? Otwórz ten link:",
            "Dostajesz tę wiadomość, bo zarezerwowałeś(-aś) rozmowę rekrutacyjną. Link jest osobisty – nie przekazuj go dalej."),
        ["en"] = new("Recruitment", "Open your booking", "Button not working? Open this link:",
            "You're receiving this because you booked a recruitment interview. The link is personal – please don't forward it."),
    };

    static readonly Dictionary<string, string> NoLocation = new() { ["pl"] = "podamy wkrótce", ["en"] = "we'll let you know" };

    // {date} and {time} in the club's time zone, in the recruit's language.
    public static RenderedEmail Render(Template t, BookingValues v, string language, TimeZoneInfo zone, string? clubName = null)
    {
        var culture = CultureInfo.GetCultureInfo(language);
        var (start, end) = (TimeZoneInfo.ConvertTimeFromUtc(v.StartUtc, zone), TimeZoneInfo.ConvertTimeFromUtc(v.EndUtc, zone));
        var values = new Dictionary<string, string>
        {
            ["{firstName}"] = v.Name?.Trim().Split(' ')[0] ?? "",
            ["{name}"] = v.Name?.Trim() ?? "",
            ["{date}"] = start.ToString("dddd, d MMMM yyyy", culture),
            ["{time}"] = $"{start:HH:mm}–{end:HH:mm}",
            ["{sections}"] = string.Join(", ", v.Sections),
            ["{location}"] = string.IsNullOrWhiteSpace(v.Location) ? NoLocation.GetValueOrDefault(language, NoLocation["pl"]) : v.Location.Trim(),
            ["{contact}"] = v.Contact ?? "",
            ["{link}"] = v.Link ?? "",
        };
        var body = EmailTemplates.Fill(t.Body, values);
        var html = Html.GetValueOrDefault(language, Html["pl"]);
        var header = string.IsNullOrWhiteSpace(clubName) ? html.Recruitment : $"{html.Recruitment} – {clubName.Trim()}";
        return new(EmailTemplates.Fill(t.Subject, values), body, EmailHtml.Build(body, v.Link, html, header));
    }

    static readonly HtmlTexts StaffHtml = new("RekruTime", "Open the interview", "Button not working? Open this link:",
        "You're receiving this because you're on the interview team.");

    public static RenderedEmail RenderStaff(StaffNotice notice, StaffValues v, TimeZoneInfo zone)
    {
        var culture = CultureInfo.GetCultureInfo("en-GB");
        var (start, end) = (TimeZoneInfo.ConvertTimeFromUtc(v.StartUtc, zone), TimeZoneInfo.ConvertTimeFromUtc(v.EndUtc, zone));
        var when = $"{start.ToString("dddd d MMMM", culture)}, {start:HH:mm}–{end:HH:mm}";
        var sections = string.Join(", ", v.Sections);
        var (subject, intro) = notice switch
        {
            StaffNotice.Booked => ($"New interview: {v.Recruit} – {when}", $"{v.Recruit} booked an interview ({sections})."),
            StaffNotice.Moved => ($"Interview moved: {v.Recruit} – {when}", $"The interview with {v.Recruit} ({sections}) was moved to a new time."),
            StaffNotice.Cancelled => ($"Interview cancelled: {v.Recruit} – {when}", $"The interview with {v.Recruit} ({sections}) was cancelled."),
            StaffNotice.Added => ($"You're on an interview: {v.Recruit} – {when}", $"You were added to the interview with {v.Recruit} ({sections})."),
            _ => ($"You're off an interview: {v.Recruit} – {when}", $"You were removed from the interview with {v.Recruit} ({sections})."),
        };
        var lines = new List<string> { intro, "", when, $"Team: {string.Join(", ", v.Team)}" };
        var link = notice is StaffNotice.Cancelled or StaffNotice.Removed ? null : v.Link; // nothing to open any more
        if (link is not null) lines.AddRange(["", "Details and your note:", link]);
        var body = string.Join("\n", lines);
        return new(subject, body, EmailHtml.Build(body, link, StaffHtml, "RekruTime"));
    }
}
