using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace RekruTime.Api.Features.Email;

public record HtmlTexts(string Button, string LinkFallback, string Footer);

// Wraps an editable plain-text e-mail in an HTML layout: blank line = paragraph, a line that is only the
// booking link = big button, other URLs / e-mail addresses = links. Table layout + inline styles, because
// Gmail and Outlook ignore <style> blocks and most modern CSS.
public static partial class EmailHtml
{
    // ponytail: placeholder palette – the visual identity (#76) replaces these.
    const string Accent = "#2563eb", Text = "#1f2937", Muted = "#6b7280", Page = "#f3f4f6";
    const string Font = "font-family:-apple-system,'Segoe UI',Roboto,Helvetica,Arial,sans-serif;";

    public static string Build(string text, string link, HtmlTexts texts, string? header)
    {
        var content = new StringBuilder();
        var lines = new List<string>();
        void Paragraph()
        {
            if (lines.Count > 0)
                content.Append($"<p style=\"margin:0 0 16px;{Font}font-size:16px;line-height:24px;color:{Text}\">{string.Join("<br>", lines)}</p>");
            lines.Clear();
        }

        foreach (var raw in text.Replace("\r\n", "\n").Trim().Split('\n'))
        {
            var line = raw.Trim();
            if (line == "") Paragraph();
            else if (line == link) { Paragraph(); content.Append(Button(link, texts.Button)); }
            else lines.Add(Linkify(line));
        }
        Paragraph();

        var href = WebUtility.HtmlEncode(link);
        var headerRow = string.IsNullOrWhiteSpace(header) ? "" :
            $"<tr><td bgcolor=\"{Accent}\" style=\"background-color:{Accent};padding:20px 32px;{Font}font-size:18px;font-weight:bold;color:#ffffff\">{WebUtility.HtmlEncode(header)}</td></tr>";
        return $"""
            <!DOCTYPE html>
            <html><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"></head>
            <body style="margin:0;padding:0;background-color:{Page}">
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" bgcolor="{Page}" style="background-color:{Page}"><tr><td align="center" style="padding:24px 12px">
            <!--[if mso]><table role="presentation" width="560" cellpadding="0" cellspacing="0"><tr><td><![endif]-->
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" bgcolor="#ffffff" style="max-width:560px;background-color:#ffffff;border-radius:8px;overflow:hidden">
            {headerRow}
            <tr><td style="padding:32px">{content}</td></tr>
            <tr><td style="padding:16px 32px 24px;border-top:1px solid #e5e7eb;{Font}font-size:12px;line-height:18px;color:{Muted}">
            {WebUtility.HtmlEncode(texts.LinkFallback)}<br><a href="{href}" style="color:{Muted};word-break:break-all">{href}</a><br><br>{WebUtility.HtmlEncode(texts.Footer)}
            </td></tr>
            </table>
            <!--[if mso]></td></tr></table><![endif]-->
            </td></tr></table>
            </body></html>
            """;
    }

    static string Button(string link, string label) => $"""
        <table role="presentation" cellpadding="0" cellspacing="0" style="margin:8px 0 24px"><tr>
        <td bgcolor="{Accent}" style="background-color:{Accent};border-radius:6px"><a href="{WebUtility.HtmlEncode(link)}" style="display:inline-block;padding:14px 28px;{Font}font-size:16px;font-weight:bold;color:#ffffff;text-decoration:none">{WebUtility.HtmlEncode(label)}</a></td>
        </tr></table>
        """;

    // Encodes the line, then links URLs and e-mail addresses (trailing punctuation stays outside the link).
    static string Linkify(string line) => Links().Replace(WebUtility.HtmlEncode(line), m =>
    {
        var target = m.Value.TrimEnd('.', ',', ';', ':', '!', '?', ')');
        var href = m.Groups["url"].Success ? target : "mailto:" + target;
        return $"<a href=\"{href}\" style=\"color:{Accent}\">{target}</a>{m.Value[target.Length..]}";
    });

    [GeneratedRegex(@"(?<url>https?://[^\s<]+)|[\w.+-]+@[\w-]+(\.[\w-]+)+")]
    private static partial Regex Links();
}
