using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace RekruTime.Api.Data;

// All DateTime values are UTC (timestamptz).
public class AppDbContext(DbContextOptions<AppDbContext> options) : IdentityDbContext<AppUser>(options)
{
    public DbSet<Section> Sections => Set<Section>();
    public DbSet<UserSection> UserSections => Set<UserSection>();
    public DbSet<Availability> Availabilities => Set<Availability>();
    public DbSet<BookingLink> BookingLinks => Set<BookingLink>();
    public DbSet<Recruit> Recruits => Set<Recruit>();
    public DbSet<Application> Applications => Set<Application>();
    public DbSet<ApplicationVote> ApplicationVotes => Set<ApplicationVote>();
    public DbSet<Interview> Interviews => Set<Interview>();
    public DbSet<InterviewNote> InterviewNotes => Set<InterviewNote>();
    public DbSet<RecruitmentSettings> RecruitmentSettings => Set<RecruitmentSettings>();
    public DbSet<EmailMessage> EmailMessages => Set<EmailMessage>();
    public DbSet<EmailTemplate> EmailTemplates => Set<EmailTemplate>();

    protected override void ConfigureConventions(ModelConfigurationBuilder b)
    {
        // Enums as text: readable in SQL, and the overlap constraint filters on 'Cancelled'.
        b.Properties<ApplicationStatus>().HaveConversion<string>();
        b.Properties<VoteDecision>().HaveConversion<string>();
        b.Properties<InterviewStatus>().HaveConversion<string>();
        b.Properties<EmailStatus>().HaveConversion<string>();
    }

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);

        b.Entity<Section>().HasIndex(s => s.Name).IsUnique();
        b.Entity<Section>().ToTable(t =>
        {
            t.HasCheckConstraint("CK_Sections_Duration", "\"InterviewDurationMinutes\" > 0");
            t.HasCheckConstraint("CK_Sections_Buffer", "\"BufferMinutes\" >= 0");
        });

        b.Entity<UserSection>().HasKey(us => new { us.UserId, us.SectionId });
        b.Entity<UserSection>().HasOne(us => us.User).WithMany(u => u.Sections).HasForeignKey(us => us.UserId);

        b.Entity<Availability>().HasOne<AppUser>().WithMany().HasForeignKey(a => a.UserId);
        b.Entity<Availability>().HasIndex(a => new { a.UserId, a.Start });
        b.Entity<Availability>().ToTable(t => t.HasCheckConstraint("CK_Availabilities_Range", "\"End\" > \"Start\""));

        b.Entity<BookingLink>().HasIndex(l => l.Token).IsUnique();
        b.Entity<BookingLink>().Property(l => l.Token).HasMaxLength(32);
        b.Entity<BookingLink>().HasOne<AppUser>().WithMany().HasForeignKey(l => l.CreatedById).OnDelete(DeleteBehavior.SetNull);
        // Deleting a recruit (GDPR) also deletes their links – the label holds their name.
        b.Entity<BookingLink>().HasOne(l => l.Recruit).WithMany().HasForeignKey(l => l.RecruitId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<BookingLink>().HasMany(l => l.Sections).WithMany().UsingEntity("BookingLinkSections");

        b.Entity<RecruitmentSettings>().Property(s => s.Id).ValueGeneratedNever();
        b.Entity<RecruitmentSettings>().ToTable(t => t.HasCheckConstraint("CK_RecruitmentSettings_Single", "\"Id\" = 1"));

        b.Entity<EmailMessage>().HasOne<BookingLink>().WithMany().HasForeignKey(m => m.BookingLinkId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<EmailMessage>().HasIndex(m => new { m.Status, m.NextAttemptAt });
        b.Entity<EmailTemplate>().HasKey(t => new { t.Key, t.Language });
        b.Entity<BookingLink>().Property(l => l.Language).HasMaxLength(5);

        b.Entity<Recruit>().HasIndex(r => r.Email).IsUnique();
        b.Entity<Recruit>().Property(r => r.DiscordNickname).HasMaxLength(50);

        b.Entity<Application>().HasIndex(a => new { a.RecruitId, a.SectionId }).IsUnique();
        b.Entity<Application>().HasOne<Recruit>().WithMany(r => r.Applications).HasForeignKey(a => a.RecruitId);
        b.Entity<Application>().HasOne(a => a.Section).WithMany().HasForeignKey(a => a.SectionId).OnDelete(DeleteBehavior.Restrict);

        // Votes go with the application (GDPR delete) and with the voter's account.
        b.Entity<ApplicationVote>().HasOne<Application>().WithMany().HasForeignKey(v => v.ApplicationId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<ApplicationVote>().HasOne(v => v.User).WithMany().HasForeignKey(v => v.UserId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<ApplicationVote>().HasIndex(v => new { v.ApplicationId, v.UserId }).IsUnique();
        b.Entity<ApplicationVote>().Property(v => v.Comment).HasMaxLength(2000);

        b.Entity<Interview>().HasOne<BookingLink>().WithMany().HasForeignKey(i => i.BookingLinkId).OnDelete(DeleteBehavior.SetNull);
        b.Entity<Interview>().HasMany(i => i.Sections).WithMany().UsingEntity("InterviewSections");
        b.Entity<Interview>().HasMany(i => i.Interviewers).WithMany().UsingEntity("InterviewInterviewers");
        b.Entity<Interview>().HasIndex(i => i.Start);
        // One booking per link (a cancelled interview frees the link for rebooking).
        b.Entity<Interview>().HasIndex(i => i.BookingLinkId).IsUnique().HasFilter("\"Status\" <> 'Cancelled'")
            .HasDatabaseName("IX_Interviews_OneBookingPerLink");
        b.Entity<Interview>().ToTable(t =>
        {
            t.HasCheckConstraint("CK_Interviews_Range", "\"End\" > \"Start\" AND \"BlockedUntil\" >= \"End\"");
        });
        // Deleting the recruit (GDPR) deletes their interviews and so the notes.
        b.Entity<InterviewNote>().HasOne<Interview>().WithMany().HasForeignKey(n => n.InterviewId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<InterviewNote>().HasOne(n => n.Author).WithMany().HasForeignKey(n => n.AuthorId).OnDelete(DeleteBehavior.SetNull);
        b.Entity<InterviewNote>().HasIndex(n => new { n.InterviewId, n.AuthorId }).IsUnique();
        // The room-overlap exclusion constraint is raw SQL in the migration (EF can't model EXCLUDE).
    }
}
