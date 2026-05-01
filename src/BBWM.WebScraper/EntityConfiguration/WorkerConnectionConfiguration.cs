using BBWM.WebScraper.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BBWM.WebScraper.EntityConfiguration;

public class WorkerConnectionConfiguration : IEntityTypeConfiguration<WorkerConnection>
{
    public void Configure(EntityTypeBuilder<WorkerConnection> e)
    {
        e.ToTable("WorkerConnections");
        e.HasKey(x => x.Id);
        e.Property(x => x.UserId).IsRequired().HasMaxLength(450);
        e.Property(x => x.Name).IsRequired().HasMaxLength(256);
        e.Property(x => x.CurrentConnection).HasMaxLength(256);
        e.Property(x => x.ExtensionVersion).HasMaxLength(64);
        e.HasIndex(x => new { x.UserId, x.Name }).IsUnique(); // dedup key
    }
}
