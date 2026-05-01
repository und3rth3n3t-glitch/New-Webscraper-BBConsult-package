using System.Text.Json;
using BBWM.WebScraper.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace BBWM.WebScraper.EntityConfiguration;

public class TaskEntityConfiguration : IEntityTypeConfiguration<TaskEntity>
{
    private static readonly ValueConverter<string[], string> SearchTermsConverter = new(
        v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
        v => JsonSerializer.Deserialize<string[]>(v, (JsonSerializerOptions?)null) ?? Array.Empty<string>());

    public void Configure(EntityTypeBuilder<TaskEntity> e)
    {
        e.ToTable("Tasks");
        e.HasKey(x => x.Id);
        e.Property(x => x.UserId).IsRequired().HasMaxLength(450);
        e.Property(x => x.Name).IsRequired().HasMaxLength(256);
        e.Property(x => x.SearchTerms).HasConversion(SearchTermsConverter).IsRequired();
        e.HasOne(x => x.ScraperConfig)
            .WithMany()
            .HasForeignKey(x => x.ScraperConfigId)
            .OnDelete(DeleteBehavior.Restrict);
        e.HasIndex(x => x.UserId);
    }
}
