using System.Globalization;
using System.Text;

namespace RekruTime.Api.Features.Email;

// A one-event iCalendar file (RFC 5545) attached to booking e-mails. METHOD:PUBLISH = "add to calendar" without
// RSVP buttons; the same UID with a higher SEQUENCE updates the event, METHOD:CANCEL removes it.
public static class Ics
{
    public static string Event(string uid, int sequence, DateTime startUtc, DateTime endUtc, string summary, string? location, string? description,
        bool cancelled = false)
    {
        var sb = new StringBuilder();
        void Line(string s) => sb.Append(Fold(s)).Append("\r\n");
        Line("BEGIN:VCALENDAR");
        Line("VERSION:2.0");
        Line("PRODID:-//RekruTime//EN");
        Line(cancelled ? "METHOD:CANCEL" : "METHOD:PUBLISH");
        Line("BEGIN:VEVENT");
        Line($"UID:{uid}");
        Line($"SEQUENCE:{sequence}");
        Line($"DTSTAMP:{Utc(DateTime.UtcNow)}");
        Line($"DTSTART:{Utc(startUtc)}");
        Line($"DTEND:{Utc(endUtc)}");
        Line($"SUMMARY:{Escape(summary)}");
        if (!string.IsNullOrWhiteSpace(location)) Line($"LOCATION:{Escape(location)}");
        if (!string.IsNullOrWhiteSpace(description)) Line($"DESCRIPTION:{Escape(description)}");
        if (cancelled) Line("STATUS:CANCELLED");
        Line("END:VEVENT");
        Line("END:VCALENDAR");
        return sb.ToString();
    }

    // Grows with every change, so calendars apply the latest version (seconds since 2025 fit an int for decades).
    public static int Sequence() => (int)(DateTime.UtcNow - new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;

    static string Utc(DateTime d) => DateTime.SpecifyKind(d, DateTimeKind.Utc).ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);

    public static string Escape(string s) =>
        s.Replace("\\", "\\\\").Replace(";", "\\;").Replace(",", "\\,").Replace("\r\n", "\\n").Replace("\n", "\\n");

    // Lines over 75 bytes continue on the next line after a space; never split inside a UTF-8 character.
    static string Fold(string line)
    {
        var sb = new StringBuilder();
        var bytes = 0;
        foreach (var rune in line.EnumerateRunes())
        {
            var size = rune.Utf8SequenceLength;
            if (bytes + size > 75)
            {
                sb.Append("\r\n ");
                bytes = 1;
            }
            sb.Append(rune.ToString());
            bytes += size;
        }
        return sb.ToString();
    }
}
