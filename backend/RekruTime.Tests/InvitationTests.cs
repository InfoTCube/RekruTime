using RekruTime.Api.Features.Email;

namespace RekruTime.Tests;

public class InvitationTests
{
    static readonly InvitationValues Jan = new("Jan Kowalski", "https://r.example/book/tok", ["AI", "Web"], new DateOnly(2026, 11, 12), "rekrutacja@kolo.pl");

    [Fact]
    public void Fills_every_placeholder_with_language_specific_date()
    {
        var t = new Template("{sections} – {name}", "{name}|{link}|{sections}|{deadline}|{contact}");

        var pl = Invitations.Render(t, Jan, "pl");
        var en = Invitations.Render(t, Jan, "en");

        Assert.Equal("AI, Web – Jan Kowalski", pl.Subject);
        Assert.Equal("Jan Kowalski|https://r.example/book/tok|AI, Web|12 listopada 2026|rekrutacja@kolo.pl", pl.Body);
        Assert.EndsWith("|12 November 2026|rekrutacja@kolo.pl", en.Body);
    }

    [Fact]
    public void Empty_value_removes_the_placeholder_and_the_space_before_it()
    {
        var anonymous = Jan with { Name = null, Contact = null };

        var r = Invitations.Render(new Template("x", "Cześć {name}! Pisz: {contact}"), anonymous, "pl");

        Assert.Equal("Cześć! Pisz:", r.Body);
    }

    [Theory]
    [InlineData("pl")]
    [InlineData("en")]
    public void Built_in_templates_use_only_known_placeholders_and_contain_the_link(string language)
    {
        var r = Invitations.Render(Invitations.Defaults[language], Jan, language);

        Assert.Contains("https://r.example/book/tok", r.Body);
        Assert.DoesNotContain("{", r.Subject + r.Body); // nothing left unfilled
    }
}
