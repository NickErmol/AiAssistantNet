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
            t.Property(x => x.BoundaryRole).HasConversion<int>().HasDefaultValue(BoundaryRole.None);
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

        // Session mode columns
        s.Property(x => x.Mode).HasConversion<int>();
        s.Property(x => x.AudioSource).HasConversion<int>();

        s.Navigation(x => x.ConversationTurns).HasField("_turns").UsePropertyAccessMode(PropertyAccessMode.Field);

        s.OwnsMany(x => x.ConversationTurns, ct =>
        {
            ct.HasKey(x => x.Id);
            ct.Property(x => x.Id)
                .HasConversion(id => id.Value, v => new ConversationTurnId(v));
            ct.Property(x => x.SessionId)
                .HasConversion(id => id.Value, v => new SessionId(v));
            ct.Property(x => x.InitialQuestionId)
                .HasConversion(id => id.Value, v => new QuestionId(v));
            ct.Property(x => x.InitialQuestionText).HasMaxLength(2000);
            ct.Property(x => x.Status).HasConversion<int>();
            ct.Property(x => x.CreatedAt)
                .HasConversion(dto => dto.ToUnixTimeMilliseconds(),
                               ms  => DateTimeOffset.FromUnixTimeMilliseconds(ms));
            ct.Property(x => x.UpdatedAt)
                .HasConversion(dto => dto.ToUnixTimeMilliseconds(),
                               ms  => DateTimeOffset.FromUnixTimeMilliseconds(ms));

            // JSON bridge for private List<TranscriptItemId> fields
            ct.Property("ClarificationQuestionIdsJson")
                .HasColumnName("ClarificationQuestionIds")
                .HasColumnType("TEXT")
                .HasDefaultValue("[]");
            ct.Property("ClarificationResponseIdsJson")
                .HasColumnName("ClarificationResponseIds")
                .HasColumnType("TEXT")
                .HasDefaultValue("[]");
            ct.Property("QuestionFragmentsJson")
                .HasColumnName("QuestionFragments")
                .HasColumnType("TEXT")
                .HasDefaultValue("[]");
            ct.Property(x => x.LastUpdateReason).HasConversion<int>().HasDefaultValue(TurnUpdateReason.InitialQuestionComplete);

            ct.OwnsMany(x => x.AnswerVersions, av =>
            {
                av.HasKey(x => x.Id);
                av.Property(x => x.Id)
                    .HasConversion(id => id.Value, v => new AnswerVersionId(v));
                av.Property(x => x.VersionType).HasConversion<int>();
                av.Property(x => x.Text).HasMaxLength(32000);
                av.Property(x => x.SupersedesId)
                    .HasConversion(
                        id => id.HasValue ? (Guid?)id.Value.Value : null,
                        v  => v.HasValue ? new AnswerVersionId(v.Value) : (AnswerVersionId?)null);
                av.Property(x => x.CreatedAt)
                    .HasConversion(dto => dto.ToUnixTimeMilliseconds(),
                                   ms  => DateTimeOffset.FromUnixTimeMilliseconds(ms));
            });
            ct.Navigation(x => x.AnswerVersions).UsePropertyAccessMode(PropertyAccessMode.Field);
        });
    }
}
