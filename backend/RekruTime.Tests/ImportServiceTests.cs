using Microsoft.EntityFrameworkCore;
using RekruTime.Api.Data;
using RekruTime.Api.Features.Booking;

namespace RekruTime.Tests;

// ImportService against a real Postgres: merging, validation, "already invited", idempotent re-import.
public class ImportServiceTests : IntegrationTest
{
    [Fact]
    public async Task Merges_rows_validates_and_skips_people_already_invited()
    {
        var (ai, web, adminId) = await App.DbAsync(async db =>
        {
            Section a = new() { Name = "AI" }, w = new() { Name = "Web" };
            db.Sections.AddRange(a, w);
            // Kasia already has a working link.
            db.BookingLinks.Add(new BookingLink { Token = "existing", Recruit = new Recruit { FullName = "Kasia", Email = "kasia@x.pl" }, Sections = [a] });
            await db.SaveChangesAsync();
            return (a.Id, w.Id, await db.Users.Where(u => u.Email == TestApp.AdminEmail).Select(u => u.Id).SingleAsync());
        });

        ImportRow[] rows =
        [
            new(2, "Zofia Nowak", " Zofia@X.pl", [ai], null),
            new(3, "Zofia Nowak", "zofia@x.pl", [web], null),        // same person, second section → merged
            new(4, "Lucas", "lucas@x.pl", [ai], "EN"),
            new(5, "Bad", "nope", [ai], null),
            new(6, "No section", "nosec@x.pl", [], null),
            new(7, "Kasia", "kasia@x.pl", [ai], null),
        ];
        Task<ImportResult> Import(bool dryRun) =>
            App.UseAsync<ImportService, ImportResult>(async s => (await s.ImportAsync(new ImportRequest(rows, null, dryRun, SendEmails: false), adminId)).Value!);

        var preview = await Import(dryRun: true);
        Assert.Equal(0, await App.DbAsync(db => db.BookingLinks.CountAsync(l => l.Token != "existing"))); // dry run saves nothing
        Assert.Equal(
            [("zofia@x.pl", ImportStatus.New), ("lucas@x.pl", ImportStatus.New), ("nope", ImportStatus.Error), ("nosec@x.pl", ImportStatus.Error), ("kasia@x.pl", ImportStatus.AlreadyInvited)],
            preview.Rows.Select(r => (r.Email, r.Status)));
        var zofia = preview.Rows[0];
        Assert.Equal([2, 3], zofia.Lines);
        Assert.Equal(["AI", "Web"], zofia.Sections);
        Assert.Equal("en", preview.Rows[1].Language);

        Assert.Equal(2, (await Import(dryRun: false)).Created);
        var zofiaSections = await App.DbAsync(db => db.BookingLinks.Where(l => l.Recruit!.Email == "zofia@x.pl").SelectMany(l => l.Sections).Select(s => s.Name).OrderBy(n => n).ToListAsync());
        Assert.Equal(["AI", "Web"], zofiaSections);

        Assert.Equal(0, (await Import(dryRun: false)).Created); // re-importing the same file creates nothing
    }
}
