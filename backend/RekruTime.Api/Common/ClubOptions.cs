namespace RekruTime.Api.Common;

// "Club" config section (env: Club__PublicUrl, …).
public class ClubOptions
{
    public string? Name { get; set; }                         // e.g. "Koło Naukowe XYZ" – booking page, e-mail header
    public string TimeZone { get; set; } = "Europe/Warsaw"; // recruitment dates are in this zone
    public string? PublicUrl { get; set; }                    // e.g. https://rekrutacja.kolo.pl – for links in e-mails
    public string? ContactEmail { get; set; }                 // shown to recruits ({contact}, booking page)
    public string? InterviewLocation { get; set; }

    TimeZoneInfo? _zone;
    public TimeZoneInfo Zone => _zone ??= TimeZoneInfo.FindSystemTimeZoneById(TimeZone);

    public bool HasPublicUrl => !string.IsNullOrWhiteSpace(PublicUrl);
    public string BookingUrl(string token) => $"{PublicUrl?.TrimEnd('/')}/book/{token}";
}
