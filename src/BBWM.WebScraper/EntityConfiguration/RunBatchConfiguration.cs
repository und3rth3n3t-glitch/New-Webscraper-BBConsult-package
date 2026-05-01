using System.Text.Json;
using BBWM.WebScraper.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace BBWM.WebScraper.EntityConfiguration;

public class RunBatchConfiguration : IEntityTypeConfiguration<RunBatch>
{
    private static readonly ValueConverter<JsonDocument, string> JsonConverter = new(
        v => v.RootElement.GetRawText(),
        v => JsonDocument.Parse(v, default(JsonDocumentOptions)));

    public void Configure(EntityTypeBuilder<RunBatch> e)
    {
        e.ToTable("RunBatches");
        e.HasKey(x => x.Id);
        e.Property(x => x.UserId).IsRequired().HasMaxLength(450);
        e.Property(x => x.PopulateSnapshot).HasConversion(JsonConverter).IsRequired();
        e.HasOne(x => x.Task)
            .WithMany()
            .HasForeignKey(x => x.TaskId)
            .OnDelete(DeleteBehavior.Cascade);
        e.HasOne(x => x.Worker)
            .WithMany()
            .HasForeignKey(x => x.WorkerId)
            .OnDelete(DeleteBehavior.Restrict);
        e.HasIndex(x => new { x.UserId, x.CreatedAt });
    }
}
