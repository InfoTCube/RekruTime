namespace RekruTime.Api.Features.Email;

// Account invite / new-password link for club members – fixed English text like the rest of the staff UI.
public static class AccountMails
{
    public const int LinkValidDays = 3; // Identity's token lifespan (Program.cs)

    public static RenderedEmail Render(bool invite, string name, string link, string? clubName)
    {
        var club = string.IsNullOrWhiteSpace(clubName) ? null : clubName.Trim();
        var (subject, intro, button) = invite
            ? ($"Your RekruTime account{(club is null ? "" : $" – {club}")}",
                $"An admin created an account for you in the recruitment panel{(club is null ? "" : $" of {club}")}. Set your password to log in:",
                "Set your password")
            : ("Set a new RekruTime password", "An admin sent you a link to set a new password for the recruitment panel:", "Set a new password");
        var body = $"""
            Hi {name.Trim().Split(' ')[0]}!

            {intro}

            {link}

            The link works once and expires in {LinkValidDays} days. You log in with this e-mail address.
            """;
        var html = new HtmlTexts("RekruTime", button, "Button not working? Open this link:",
            "You're receiving this because an admin added your e-mail to the recruitment panel. Didn't expect it? Just ignore this e-mail.");
        return new(subject, body, EmailHtml.Build(body, link, html, club is null ? "RekruTime" : $"RekruTime – {club}"));
    }
}
