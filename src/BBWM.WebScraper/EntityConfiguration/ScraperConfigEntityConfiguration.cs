using System.Text.Json;
using BBWM.WebScraper.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace BBWM.WebScraper.EntityConfiguration;

public class ScraperConfigEntityConfiguration : IEntityTypeConfiguration<ScraperConfigEntity>
{
    private static readonly ValueConverter<JsonDocument, string> JsonConverter = new(
        v => v.RootElement.GetRawText(),
        v => JsonDocument.Parse(v, default(JsonDocumentOptions)));

    public void Configure(EntityTypeBuilder<ScraperConfigEntity> e)
    {
        e.ToTable("ScraperConfigs");
        e.HasKey(x => x.Id);
        e.Property(x => x.UserId).IsRequired().HasMaxLength(450);
        e.Property(x => x.Name).IsRequired().HasMaxLength(256);
        e.Property(x => x.Domain).IsRequired().HasMaxLength(256);
        e.Property(x => x.ConfigJson).HasConversion(JsonConverter).IsRequired();
        e.Property(x => x.SchemaVersion).HasDefaultValue(3);
        e.Property(x => x.OriginClientId).HasMaxLength(450);
        // D1.c — concurrency token; EF adds WHERE Version = X to UPDATE statements.
        e.Property(x => x.Version).IsConcurrencyToken().HasDefaultValue(1);
        e.HasIndex(x => x.UserId);
        e.HasIndex(x => new { x.UserId, x.Shared });
    }
}
