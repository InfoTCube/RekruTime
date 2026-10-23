using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace RekruTime.Api.Features.Email;

public record HtmlTexts(string Recruitment, string Button, string LinkFallback, string Footer);

// Wraps an editable plain-text e-mail in an HTML layout: blank line = paragraph, a line that is only the
// booking link = big button (link null = no button, e.g. decisions), other URLs / e-mail addresses = links. Table layout + inline styles, because
// Gmail and Outlook ignore <style> blocks and most modern CSS.
public static partial class EmailHtml
{
    // Brand colours – copies of frontend/src/theme.ts (cortex-9 header, signal-7 button/links: white text stays readable).
    const string Header = "#26235c", Accent = "#0c8178", Text = "#22253a", Muted = "#6b7280", Page = "#f6f7fb";
    const string Font = "font-family:-apple-system,'Segoe UI',Roboto,Helvetica,Arial,sans-serif;";

    public static string Build(string text, string? link, HtmlTexts texts, string header)
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
            else if (link is not null && line == link) { Paragraph(); content.Append(Button(link, texts.Button)); }
            else lines.Add(Linkify(line));
        }
        Paragraph();

        var fallback = link is null ? "" : $"{WebUtility.HtmlEncode(texts.LinkFallback)}<br><a href=\"{WebUtility.HtmlEncode(link)}\" style=\"color:{Muted};word-break:break-all\">{WebUtility.HtmlEncode(link)}</a><br><br>";
        var headerRow =
            $"<tr><td bgcolor=\"{Header}\" style=\"background-color:{Header};padding:20px 32px;{Font}font-size:18px;font-weight:bold;color:#ffffff\">{WebUtility.HtmlEncode(header)}</td></tr>";
        return $"""
            <!DOCTYPE html>
            <html><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"></head>
            <body style="margin:0;padding:0;background-color:{Page}">
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" bgcolor="{Page}" style="background-color:{Page}"><tr><td align="center" style="padding:24px 12px">
            <!--[if mso]><table role="presentation" width="560" cellpadding="0" cellspacing="0"><tr><td><![endif]-->
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" bgcolor="#ffffff" style="max-width:560px;background-color:#ffffff;border-radius:8px;overflow:hidden">
            {headerRow}
            <tr><td height="4" bgcolor="#26c6b8" style="height:4px;font-size:0;line-height:0;background-color:#26c6b8">&nbsp;</td></tr>
            <tr><td style="padding:32px">{content}</td></tr>
            <tr><td style="padding:16px 32px 24px;border-top:1px solid #e5e7eb;{Font}font-size:12px;line-height:18px;color:{Muted}">
            {fallback}{WebUtility.HtmlEncode(texts.Footer)}
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
