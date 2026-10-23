using System.Net.Http.Json;
using System.Text.Json;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.EntityFrameworkCore;
using RekruTime.Api.Data;

namespace RekruTime.Tests;

// Real background sender → real SMTP server (Mailpit, which exposes received mails over HTTP). Needs Docker.
public class EmailOutboxTests : IAsyncLifetime
{
    readonly IContainer _mailpit = new ContainerBuilder("axllent/mailpit:v1.27")
        .WithPortBinding(1025, true).WithPortBinding(8025, true)
        .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(r => r.ForPort(8025).ForPath("/api/v1/messages")))
        .Build();

    public Task InitializeAsync() => _mailpit.StartAsync();

    public async Task DisposeAsync() => await _mailpit.DisposeAsync();

    async Task<TestApp> App(int smtpPort)
    {
        var app = new TestApp(new Dictionary<string, string?>
        {
            ["Email:Host"] = _mailpit.Hostname,
            ["Email:Port"] = smtpPort.ToString(),
            ["Email:Security"] = "None",
            ["Email:FromAddress"] = "rekrutacja@test.local",
            ["Email:DelayBetweenMessagesMs"] = "0",
        });
        await app.StartAsync(); // migrations, seed, background sender
        return app;
    }

    [Fact]
    public async Task Queued_mail_is_delivered_and_test_endpoint_sends_directly()
    {
        await using var app = await App(_mailpit.GetMappedPublicPort(1025));
        var admin = await app.AdminAsync();

        var id = await Queue(app, "jan@test.local", "Zaproszenie na rozmowę");
        var sent = await WaitFor(app, id, m => m.Status == EmailStatus.Sent);
        Assert.NotNull(sent.SentAt);

        var test = await admin.PostAsync("/api/email/test", null);
        Assert.True(test.IsSuccessStatusCode, await test.Content.ReadAsStringAsync());

        var received = await Mailpit();
        Assert.Contains(("jan@test.local", "Zaproszenie na rozmowę"), received.Select(m => (m.To, m.Subject))); // UTF-8 subject survives
        Assert.Contains((TestApp.AdminEmail, "RekruTime: test e-mail"), received.Select(m => (m.To, m.Subject)));

        // HTML + plain text alternative in one message
        var jan = await MailpitMessage(received.Single(m => m.To == "jan@test.local").Id);
        Assert.Equal("Treść: zażółć gęślą jaźń", jan.GetProperty("Text").GetString()!.Trim());
        Assert.Contains("<b>zażółć</b>", jan.GetProperty("HTML").GetString());
    }

    [Fact]
    public async Task Unreachable_smtp_keeps_mail_pending_with_error_and_backoff()
    {
        await using var app = await App(smtpPort: 1); // nothing listens there
        var id = await Queue(app, "jan@test.local", "Hello");

        var failed = await WaitFor(app, id, m => m.Attempts == 1);
        Assert.Equal(EmailStatus.Pending, failed.Status); // retried later, not lost
        Assert.False(string.IsNullOrEmpty(failed.LastError));
        Assert.True(failed.NextAttemptAt > DateTime.UtcNow);

        var test = await (await app.AdminAsync()).PostAsync("/api/email/test", null);
        Assert.Equal(502, (int)test.StatusCode); // setup problem reported to the admin
    }

    static Task<int> Queue(TestApp app, string to, string subject) => app.DbAsync(async db =>
    {
        var m = new EmailMessage { To = to, Subject = subject, Body = "Treść: zażółć gęślą jaźń", HtmlBody = "<p>Treść: <b>zażółć</b> gęślą jaźń</p>" };
        db.EmailMessages.Add(m);
        await db.SaveChangesAsync();
        return m.Id;
    });

    static async Task<EmailMessage> WaitFor(TestApp app, int id, Func<EmailMessage, bool> done)
    {
        for (var i = 0; i < 60; i++)
        {
            var m = await app.DbAsync(db => db.EmailMessages.AsNoTracking().SingleAsync(x => x.Id == id));
            if (done(m)) return m;
            await Task.Delay(500);
        }
        throw new TimeoutException($"E-mail {id} didn't reach the expected state in 30 s.");
    }

    string MailpitApi => $"http://{_mailpit.Hostname}:{_mailpit.GetMappedPublicPort(8025)}/api/v1";

    async Task<List<(string Id, string To, string Subject)>> Mailpit()
    {
        using var http = new HttpClient();
        var json = await http.GetFromJsonAsync<JsonElement>($"{MailpitApi}/messages");
        return json.GetProperty("messages").EnumerateArray()
            .Select(m => (m.GetProperty("ID").GetString()!, m.GetProperty("To")[0].GetProperty("Address").GetString()!, m.GetProperty("Subject").GetString()!)).ToList();
    }

    async Task<JsonElement> MailpitMessage(string id)
    {
        using var http = new HttpClient();
        return await http.GetFromJsonAsync<JsonElement>($"{MailpitApi}/message/{id}");
    }
}
