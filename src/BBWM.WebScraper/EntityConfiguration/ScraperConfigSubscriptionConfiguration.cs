using BBWM.WebScraper.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BBWM.WebScraper.EntityConfiguration;

public class ScraperConfigSubscriptionConfiguration : IEntityTypeConfiguration<ScraperConfigSubscription>
{
    public void Configure(EntityTypeBuilder<ScraperConfigSubscription> e)
    {
        e.ToTable("ScraperConfigSubscriptions");
        e.HasKey(x => new { x.ScraperConfigId, x.WorkerId });
        e.HasOne(x => x.Config)
            .WithMany(c => c.Subscriptions)
            .HasForeignKey(x => x.ScraperConfigId)
            .OnDelete(DeleteBehavior.Cascade);
        e.HasOne(x => x.Worker)
            .WithMany()
            .HasForeignKey(x => x.WorkerId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
