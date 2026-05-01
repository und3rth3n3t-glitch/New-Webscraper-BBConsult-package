using BBWM.WebScraper.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BBWM.WebScraper.EntityConfiguration;

public class TaskEntityConfiguration : IEntityTypeConfiguration<TaskEntity>
{
    public void Configure(EntityTypeBuilder<TaskEntity> e)
    {
        e.ToTable("Tasks");
        e.HasKey(x => x.Id);
        e.Property(x => x.UserId).IsRequired().HasMaxLength(450);
        e.Property(x => x.Name).IsRequired().HasMaxLength(256);
        e.HasMany(x => x.Blocks)
            .WithOne(b => b.Task!)
            .HasForeignKey(b => b.TaskId)
            .OnDelete(DeleteBehavior.Cascade);
        e.HasIndex(x => x.UserId);
    }
}
