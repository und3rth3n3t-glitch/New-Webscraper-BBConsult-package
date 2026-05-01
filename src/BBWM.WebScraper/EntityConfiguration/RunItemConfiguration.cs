using System.Text.Json;
using BBWM.WebScraper.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace BBWM.WebScraper.EntityConfiguration;

public class RunItemConfiguration : IEntityTypeConfiguration<RunItem>
{
    private static readonly ValueConverter<JsonDocument?, string?> NullableJsonConverter = new(
        v => v == null ? null : v.RootElement.GetRawText(),
        v => v == null ? null : JsonDocument.Parse(v, default(JsonDocumentOptions)));

    public void Configure(EntityTypeBuilder<RunItem> e)
    {
        e.ToTable("RunItems");
        e.HasKey(x => x.Id);
        e.Property(x => x.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
        e.Property(x => x.PauseReason).HasMaxLength(64);
        e.Property(x => x.CurrentTerm).HasMaxLength(512);
        e.Property(x => x.CurrentStep).HasMaxLength(256);
        e.Property(x => x.Phase).HasMaxLength(32);
        e.Property(x => x.IterationLabel).HasMaxLength(512);
        e.Property(x => x.ResultJsonb).HasConversion(NullableJsonConverter);
        e.Property(x => x.IterationAssignments).HasConversion(NullableJsonConverter);
        e.HasOne(x => x.Task)
            .WithMany()
            .HasForeignKey(x => x.TaskId)
            .OnDelete(DeleteBehavior.Cascade);
        e.HasOne(x => x.Worker)
            .WithMany()
            .HasForeignKey(x => x.WorkerId)
            .OnDelete(DeleteBehavior.Restrict);
        e.HasOne(x => x.Batch)
            .WithMany(b => b.Items)
            .HasForeignKey(x => x.BatchId)
            .OnDelete(DeleteBehavior.SetNull);
        e.HasIndex(x => new { x.TaskId, x.RequestedAt });
        e.HasIndex(x => x.Status);
        e.HasIndex(x => x.ScraperConfigId);
        e.HasIndex(x => x.BatchId);
    }
}
