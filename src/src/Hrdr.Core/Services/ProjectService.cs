using Hrdr.Core.Data;
using Hrdr.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace Hrdr.Core.Services;

public record ProjectDto(int Id, string Name, string? Slug, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

public record CreateProjectRequest(string Name, string? Slug = null);

public record UpdateProjectRequest(string Name, string? Slug = null);

public class ProjectService(HrdrDbContext db)
{
    public async Task<IReadOnlyList<ProjectDto>> ListAsync(CancellationToken ct = default)
    {
        return await db.Projects
            .AsNoTracking()
            .OrderBy(p => p.Name)
            .Select(p => new ProjectDto(p.Id, p.Name, p.Slug, p.CreatedAt, p.UpdatedAt))
            .ToListAsync(ct);
    }

    public async Task<ProjectDto?> GetAsync(int id, CancellationToken ct = default)
    {
        var p = await db.Projects.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        return p is null ? null : ToDto(p);
    }

    public async Task<ProjectDto?> GetBySlugAsync(string slug, CancellationToken ct = default)
    {
        var p = await db.Projects.AsNoTracking().FirstOrDefaultAsync(x => x.Slug == slug, ct);
        return p is null ? null : ToDto(p);
    }

    public async Task<ProjectDto> CreateAsync(CreateProjectRequest request, CancellationToken ct = default)
    {
        var name = request.Name.Trim();
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Name is required.", nameof(request));

        var slug = string.IsNullOrWhiteSpace(request.Slug)
            ? await EnsureUniqueSlugAsync(SlugHelper.FromName(name), ct)
            : await EnsureUniqueSlugAsync(SlugHelper.FromName(request.Slug), ct);

        var now = DateTimeOffset.UtcNow;
        var project = new Project
        {
            Name = name,
            Slug = slug,
            CreatedAt = now,
            UpdatedAt = now
        };

        db.Projects.Add(project);
        await db.SaveChangesAsync(ct);
        return ToDto(project);
    }

    public async Task<ProjectDto?> UpdateAsync(int id, UpdateProjectRequest request, CancellationToken ct = default)
    {
        var project = await db.Projects.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (project is null) return null;

        var name = request.Name.Trim();
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Name is required.", nameof(request));

        project.Name = name;
        if (request.Slug is not null)
        {
            var desired = string.IsNullOrWhiteSpace(request.Slug)
                ? SlugHelper.FromName(name)
                : SlugHelper.FromName(request.Slug);
            if (!string.Equals(project.Slug, desired, StringComparison.Ordinal))
                project.Slug = await EnsureUniqueSlugAsync(desired, ct, excludeId: id);
        }

        project.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return ToDto(project);
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken ct = default)
    {
        var project = await db.Projects.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (project is null) return false;
        db.Projects.Remove(project);
        await db.SaveChangesAsync(ct);
        return true;
    }

    private async Task<string> EnsureUniqueSlugAsync(string baseSlug, CancellationToken ct, int? excludeId = null)
    {
        var slug = baseSlug;
        var i = 2;
        while (await db.Projects.AnyAsync(p => p.Slug == slug && (excludeId == null || p.Id != excludeId), ct))
        {
            slug = $"{baseSlug}-{i}";
            i++;
        }
        return slug;
    }

    private static ProjectDto ToDto(Project p) =>
        new(p.Id, p.Name, p.Slug, p.CreatedAt, p.UpdatedAt);
}
