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
            e.Property(w => w.State).HasDefaultValue(WorkItemState.Created);
            e.HasIndex(w => new { w.ProjectId, w.SortOrder });
            e.HasOne(w => w.Project)
                .WithMany(p => p.WorkItems)
                .HasForeignKey(w => w.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }

    /// <summary>
    /// Creates the database when missing and applies additive SQLite column upgrades
    /// (EnsureCreated does not alter existing schemas).
    /// </summary>
    public async Task EnsureDatabaseAsync(CancellationToken ct = default)
    {
        await Database.EnsureCreatedAsync(ct);
        await EnsureWorkItemStateColumnAsync(ct);
    }

    private async Task EnsureWorkItemStateColumnAsync(CancellationToken ct)
    {
        var connection = Database.GetDbConnection();
        var shouldClose = connection.State != System.Data.ConnectionState.Open;
        if (shouldClose)
            await connection.OpenAsync(ct);

        try
        {
            var hasState = false;
            await using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = "PRAGMA table_info(\"WorkItems\")";
                await using var reader = await cmd.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct))
                {
                    if (string.Equals(reader.GetString(1), "State", StringComparison.OrdinalIgnoreCase))
                    {
                        hasState = true;
                        break;
                    }
                }
            }

            if (hasState)
                return;

            await using var alter = connection.CreateCommand();
            alter.CommandText = """ALTER TABLE "WorkItems" ADD COLUMN "State" INTEGER NOT NULL DEFAULT 0""";
            await alter.ExecuteNonQueryAsync(ct);
        }
        finally
        {
            if (shouldClose)
                await connection.CloseAsync();
        }
    }
}
