using AIHelperNET.Domain.Ids;
using AIHelperNET.Domain.Sessions;
using AIHelperNET.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace AIHelperNET.Infrastructure.Persistence;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Session> Sessions => Set<Session>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var s = modelBuilder.Entity<Session>();

        s.HasKey(x => x.Id);
        s.Property(x => x.Id)
            .HasConversion(id => id.Value, v => new SessionId(v));

        // SQLite stores DateTimeOffset as long (Unix ms) for native numeric ordering.
        s.Property(x => x.StartedAt)
            .HasConversion(
                dto => dto.ToUnixTimeMilliseconds(),
                ms  => DateTimeOffset.FromUnixTimeMilliseconds(ms));
        s.Property(x => x.EndedAt)
            .HasConversion(
                dto => dto.HasValue ? (long?)dto.Value.ToUnixTimeMilliseconds() : null,
                ms  => ms.HasValue ? DateTimeOffset.FromUnixTimeMilliseconds(ms.Value) : null);
        s.Property(x => x.State);

        s.OwnsOne(x => x.AnswerSettings, a =>
        {
            a.Property(p => p.OutputLanguage).HasMaxLength(64);
        });
        s.OwnsOne(x => x.CodeProfile);

        s.Navigation(x => x.Transcript).UsePropertyAccessMode(PropertyAccessMode.Field);
        s.Navigation(x => x.Questions).UsePropertyAccessMode(PropertyAccessMode.Field);
        s.Navigation(x => x.Answers).UsePropertyAccessMode(PropertyAccessMode.Field);

        s.OwnsMany(x => x.Transcript, t =>
        {
            t.HasKey(x => x.Id);
            t.Property(x => x.Id).HasConversion(id => id.Value, v => new TranscriptItemId(v));
            t.Property(x => x.Text).HasMaxLength(4000);
            t.Property(x => x.Timestamp)
                .HasConversion(
                    dto => dto.ToUnixTimeMilliseconds(),
                    ms  => DateTimeOffset.FromUnixTimeMilliseconds(ms));
        });

        s.OwnsMany(x => x.Questions, q =>
        {
            q.HasKey(x => x.Id);
            q.Property(x => x.Id).HasConversion(id => id.Value, v => new QuestionId(v));
            q.Property(x => x.Text).HasMaxLength(2000);
            q.Property(x => x.DetectedAt)
                .HasConversion(
                    dto => dto.ToUnixTimeMilliseconds(),
                    ms  => DateTimeOffset.FromUnixTimeMilliseconds(ms));
        });

        s.OwnsMany(x => x.Answers, a =>
        {
            a.HasKey(x => x.Id);
            a.Property(x => x.Id).HasConversion(id => id.Value, v => new AnswerId(v));
            a.Property(x => x.QuestionId).HasConversion(id => id.Value, v => new QuestionId(v));
            a.Property(x => x.StartedAt)
                .HasConversion(
                    dto => dto.ToUnixTimeMilliseconds(),
                    ms  => DateTimeOffset.FromUnixTimeMilliseconds(ms));
            a.Property(x => x.CompletedAt)
                .HasConversion(
                    dto => dto.HasValue ? (long?)dto.Value.ToUnixTimeMilliseconds() : null,
                    ms  => ms.HasValue ? DateTimeOffset.FromUnixTimeMilliseconds(ms.Value) : null);
            a.Property(x => x.Content)
                .HasField("_persistedContent")
                .UsePropertyAccessMode(PropertyAccessMode.Field)
                .HasColumnName("Content")
                .HasMaxLength(32000);
        });
    }
}
