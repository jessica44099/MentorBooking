
using MentorBooking.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace MentorBooking.Api.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(
        DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    // Existing tables
    public DbSet<User> Users => Set<User>();

    public DbSet<AvailabilitySlot> AvailabilitySlots =>
        Set<AvailabilitySlot>();

    public DbSet<Session> Sessions => Set<Session>();

    public DbSet<SessionRecord> SessionRecords =>
        Set<SessionRecord>();

    public DbSet<SessionRecordAudit> SessionRecordAudits =>
        Set<SessionRecordAudit>();

    public DbSet<Review> Reviews => Set<Review>();

    public DbSet<SessionStatusHistory> SessionStatusHistory =>
        Set<SessionStatusHistory>();

    // New tables
    public DbSet<AuthToken> AuthTokens =>
        Set<AuthToken>();

    public DbSet<Notification> Notifications =>
        Set<Notification>();

    public DbSet<EmailLog> EmailLogs =>
        Set<EmailLog>();

    public DbSet<ShiftHistory> ShiftHistories =>
        Set<ShiftHistory>();

    protected override void OnModelCreating(
        ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // USERS
        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(x => x.Id);

            entity.Property(x => x.Name)
                .IsRequired()
                .HasMaxLength(150);

            entity.Property(x => x.Email)
                .IsRequired()
                .HasMaxLength(320);

            entity.HasIndex(x => x.Email)
                .IsUnique();

            entity.Property(x => x.PasswordHash)
                .IsRequired();

            entity.Property(x => x.Role)
                .HasConversion<string>()
                .HasMaxLength(20);

            entity.Property(x => x.Bio)
                .HasMaxLength(2000);

            entity.HasIndex(x => x.Role);
        });

        // AVAILABILITY SLOTS
        modelBuilder.Entity<AvailabilitySlot>(entity =>
        {
            entity.HasKey(x => x.Id);

            entity.Property(x => x.Type)
                .HasConversion<string>()
                .HasMaxLength(20);

            entity.Property(x => x.Note)
                .HasMaxLength(500);

            entity.HasOne<User>()
                .WithMany()
                .HasForeignKey(x => x.MentorId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(x => new
            {
                x.MentorId,
                x.StartUtc
            });
        });

        // SESSIONS
        modelBuilder.Entity<Session>(entity =>
        {
            entity.HasKey(x => x.Id);

            entity.Property(x => x.Topic)
                .IsRequired()
                .HasMaxLength(200);

            entity.Property(x => x.Description)
                .IsRequired()
                .HasMaxLength(2000);

            entity.Property(x => x.MeetingInfo)
                .HasMaxLength(1500);

            entity.Property(x => x.RejectReason)
                .HasMaxLength(500);

            entity.Property(x => x.Mode)
                .HasConversion<string>()
                .HasMaxLength(20);

            entity.Property(x => x.Status)
                .HasConversion<string>()
                .HasMaxLength(30);

            entity.Property(x => x.RowVersion)
                .IsConcurrencyToken();

            entity.HasOne<User>()
                .WithMany()
                .HasForeignKey(x => x.MentorId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne<User>()
                .WithMany()
                .HasForeignKey(x => x.StudentId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne<AvailabilitySlot>()
                .WithMany()
                .HasForeignKey(x => x.SlotId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(x => new
            {
                x.MentorId,
                x.ScheduledStartUtc
            });

            entity.HasIndex(x => new
            {
                x.StudentId,
                x.ScheduledStartUtc
            });

            entity.HasIndex(x => x.Status);
        });

        // SESSION RECORDS
        modelBuilder.Entity<SessionRecord>(entity =>
        {
            entity.HasKey(x => x.SessionId);

            entity.HasOne<Session>()
                .WithOne()
                .HasForeignKey<SessionRecord>(
                    x => x.SessionId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.Property(x => x.Summary)
                .IsRequired()
                .HasMaxLength(500);

            entity.Property(x => x.Resolution)
                .HasConversion<string>()
                .HasMaxLength(30);

            entity.Property(x => x.PrivateMentorNote)
                .HasMaxLength(2000);

            entity.ToTable(table =>
                table.HasCheckConstraint(
                    "CK_SessionRecords_DoubtsCleared",
                    "\"DoubtsCleared\" BETWEEN 0 AND 20"));
        });

        // SESSION RECORD AUDITS
        modelBuilder.Entity<SessionRecordAudit>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.OldSummary)
    .HasMaxLength(500);

entity.Property(x => x.NewSummary)
    .HasMaxLength(500);

entity.Property(x => x.OldResolution)
    .HasMaxLength(30);

entity.Property(x => x.NewResolution)
    .HasMaxLength(30);

entity.Property(x => x.OldPrivateMentorNote)
    .HasMaxLength(2000);

entity.Property(x => x.NewPrivateMentorNote)
    .HasMaxLength(2000);

entity.Property(x => x.ChangeReason)
    .HasMaxLength(500);

            entity.HasOne<SessionRecord>()
                .WithMany()
                .HasForeignKey(x => x.SessionId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne<User>()
                .WithMany()
                .HasForeignKey(x => x.ChangedByMentorId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(x => new
            {
                x.SessionId,
                x.ChangedAtUtc
            });
        });

        // REVIEWS
        modelBuilder.Entity<Review>(entity =>
        {
            entity.HasKey(x => x.SessionId);

            entity.HasOne<Session>()
                .WithOne()
                .HasForeignKey<Review>(
                    x => x.SessionId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.Property(x => x.Comment)
                .HasMaxLength(1000);

            entity.ToTable(table =>
                table.HasCheckConstraint(
                    "CK_Reviews_Rating",
                    "\"Rating\" BETWEEN 1 AND 5"));
        });

        // SESSION STATUS HISTORY
        modelBuilder.Entity<SessionStatusHistory>(entity =>
        {
            entity.HasKey(x => x.Id);

            entity.Property(x => x.FromStatus)
                .HasConversion<string>()
                .HasMaxLength(30);

            entity.Property(x => x.ToStatus)
                .HasConversion<string>()
                .HasMaxLength(30);

            entity.Property(x => x.Reason)
                .HasMaxLength(500);

            entity.HasOne<Session>()
                .WithMany()
                .HasForeignKey(x => x.SessionId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne<User>()
                .WithMany()
                .HasForeignKey(x => x.ChangedByUserId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(x => new
            {
                x.SessionId,
                x.ChangedAtUtc
            });
        });

        // AUTHENTICATION TOKENS
        modelBuilder.Entity<AuthToken>(entity =>
        {
            entity.HasKey(x => x.Id);

            entity.Property(x => x.Type)
                .HasConversion<string>()
                .HasMaxLength(30);

            entity.Property(x => x.TokenHash)
                .IsRequired()
                .HasMaxLength(128);

            entity.HasOne<User>()
                .WithMany()
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(x => x.TokenHash)
                .IsUnique();

            entity.HasIndex(x => new
            {
                x.UserId,
                x.Type,
                x.CreatedAtUtc
            });
        });

        // NOTIFICATIONS
        modelBuilder.Entity<Notification>(entity =>
        {
            entity.HasKey(x => x.Id);

            entity.Property(x => x.Title)
                .IsRequired()
                .HasMaxLength(200);

            entity.Property(x => x.Message)
                .IsRequired()
                .HasMaxLength(2000);

            entity.HasOne<User>()
                .WithMany()
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne<Session>()
                .WithMany()
                .HasForeignKey(x => x.SessionId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(x => new
            {
                x.UserId,
                x.ReadAtUtc,
                x.CreatedAtUtc
            });
        });

        // EMAIL LOGS
        modelBuilder.Entity<EmailLog>(entity =>
        {
            entity.HasKey(x => x.Id);

            entity.Property(x => x.ToAddress)
                .IsRequired()
                .HasMaxLength(320);

            entity.Property(x => x.Subject)
                .IsRequired()
                .HasMaxLength(250);

            entity.Property(x => x.HtmlBody)
                .IsRequired();

            entity.Property(x => x.TextBody)
                .IsRequired();

            entity.Property(x => x.Status)
                .HasConversion<string>()
                .HasMaxLength(20);

            entity.Property(x => x.LastError)
                .HasMaxLength(4000);

            entity.HasIndex(x => new
            {
                x.Status,
                x.NextAttemptAtUtc
            });
        });

        // SESSION SHIFT HISTORY
        modelBuilder.Entity<ShiftHistory>(entity =>
        {
            entity.HasKey(x => x.Id);

            entity.Property(x => x.Reason)
                .IsRequired()
                .HasMaxLength(500);

            entity.HasOne<Session>()
                .WithMany()
                .HasForeignKey(x => x.SessionId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne<Session>()
                .WithMany()
                .HasForeignKey(x => x.CausedBySessionId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(x => new
            {
                x.SessionId,
                x.CreatedAtUtc
            });
        });
    }
}
