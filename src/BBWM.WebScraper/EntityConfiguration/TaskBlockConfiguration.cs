using System.Text.Json;
using BBWM.WebScraper.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace BBWM.WebScraper.EntityConfiguration;

public class TaskBlockConfiguration : IEntityTypeConfiguration<TaskBlock>
{
    private static readonly ValueConverter<JsonDocument, string> JsonConverter = new(
        v => v.RootElement.GetRawText(),
        v => JsonDocument.Parse(v, default(JsonDocumentOptions)));

    public void Configure(EntityTypeBuilder<TaskBlock> e)
    {
        e.ToTable("TaskBlocks");
        e.HasKey(x => x.Id);
        e.Property(x => x.BlockType).HasConversion<int>();
        e.Property(x => x.ConfigJsonb).HasConversion(JsonConverter).IsRequired();
        e.HasOne(x => x.ParentBlock)
            .WithMany(x => x.Children)
            .HasForeignKey(x => x.ParentBlockId)
            .OnDelete(DeleteBehavior.Restrict);
        e.HasIndex(x => new { x.TaskId, x.ParentBlockId, x.OrderIndex });
    }
}
