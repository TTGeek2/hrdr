using Hrdr.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace Hrdr.Core.Data;

public class HrdrDbContext(DbContextOptions<HrdrDbContext> options) : DbContext(options)
{
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<WorkItem> WorkItems => Set<WorkItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Project>(e =>
        {
            e.HasIndex(p => p.Name).IsUnique();
            e.HasIndex(p => p.Slug).IsUnique().HasFilter("Slug IS NOT NULL");
            e.Property(p => p.Name).HasMaxLength(200).IsRequired();
            e.Property(p => p.Slug).HasMaxLength(100);
        });

        modelBuilder.Entity<WorkItem>(e =>
        {
            e.Property(w => w.Title).HasMaxLength(300).IsRequired();
            e.Property(w => w.Description).HasMaxLength(8000);
            e.HasIndex(w => new { w.ProjectId, w.SortOrder });
            e.HasOne(w => w.Project)
                .WithMany(p => p.WorkItems)
                .HasForeignKey(w => w.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
