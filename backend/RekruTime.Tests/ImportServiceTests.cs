using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RekruTime.Api.Data;
using RekruTime.Api.Features.Booking;
using Testcontainers.PostgreSql;

namespace RekruTime.Tests;

// ImportService against a real Postgres (needs Docker): merging, validation, "already invited", idempotent re-import.
public class ImportServiceTests : IAsyncLifetime
{
    readonly PostgreSqlContainer _db = new PostgreSqlBuilder("postgres:17").Build();
    WebApplicationFactory<Program> _app = null!;

    public async Task InitializeAsync()
    {
        await _db.StartAsync();
        _app = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b
            .UseSetting("ConnectionStrings:Default", _db.GetConnectionString())
            .UseSetting("SeedAdmin:Email", "admin@test.local")
            .UseSetting("SeedAdmin:Password", "Admin123!"));
        _ = _app.Server; // migrations + seed
    }

    public async Task DisposeAsync()
    {
        await _app.DisposeAsync();
        await _db.DisposeAsync();
    }

    [Fact]
    public async Task Merges_rows_validates_and_skips_people_already_invited()
    {
        using var scope = _app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var ai = new Section { Name = "AI" };
        var web = new Section { Name = "Web" };
        db.Sections.AddRange(ai, web);
        // Kasia already has a working link.
        db.BookingLinks.Add(new BookingLink { Token = "existing", Recruit = new Recruit { FullName = "Kasia", Email = "kasia@x.pl" }, Sections = [ai] });
        await db.SaveChangesAsync();
        var import = scope.ServiceProvider.GetRequiredService<ImportService>();
        var adminId = await db.Users.Where(u => u.Email == "admin@test.local").Select(u => u.Id).SingleAsync();

        ImportRow[] rows =
        [
            new(2, "Zofia Nowak", " Zofia@X.pl", [ai.Id], null),
            new(3, "Zofia Nowak", "zofia@x.pl", [web.Id], null),        // same person, second section → merged
            new(4, "Lucas", "lucas@x.pl", [ai.Id], "EN"),
            new(5, "Bad", "nope", [ai.Id], null),
            new(6, "No section", "nosec@x.pl", [], null),
            new(7, "Kasia", "kasia@x.pl", [ai.Id], null),
        ];

        var preview = (await import.ImportAsync(new ImportRequest(rows, null, DryRun: true, SendEmails: false), adminId)).Value!;
        Assert.Equal(0, await db.BookingLinks.CountAsync(l => l.Token != "existing")); // dry run saves nothing
        Assert.Equal(
            [("zofia@x.pl", ImportStatus.New), ("lucas@x.pl", ImportStatus.New), ("nope", ImportStatus.Error), ("nosec@x.pl", ImportStatus.Error), ("kasia@x.pl", ImportStatus.AlreadyInvited)],
            preview.Rows.Select(r => (r.Email, r.Status)));
        var zofia = preview.Rows[0];
        Assert.Equal([2, 3], zofia.Lines);
        Assert.Equal(["AI", "Web"], zofia.Sections);
        Assert.Equal("en", preview.Rows[1].Language);

        var done = (await import.ImportAsync(new ImportRequest(rows, null, DryRun: false, SendEmails: false), adminId)).Value!;
        Assert.Equal(2, done.Created);
        var zofiaLink = await db.BookingLinks.Include(l => l.Sections).SingleAsync(l => l.Recruit!.Email == "zofia@x.pl");
        Assert.Equal(["AI", "Web"], zofiaLink.Sections.Select(s => s.Name).Order());

        var again = (await import.ImportAsync(new ImportRequest(rows, null, DryRun: false, SendEmails: false), adminId)).Value!;
        Assert.Equal(0, again.Created); // re-importing the same file creates nothing
    }
}
