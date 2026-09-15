using Hrdr.Core.Data;
using Hrdr.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace Hrdr.Core.Services;

public record WorkItemDto(
    int Id,
    int ProjectId,
    string? ProjectName,
    WorkItemType Type,
    string Title,
    string Description,
    int SortOrder,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public record CreateWorkItemRequest(
    int ProjectId,
    WorkItemType Type,
    string Title,
    string? Description = null);

public record UpdateWorkItemRequest(
    int? ProjectId = null,
    WorkItemType? Type = null,
    string? Title = null,
    string? Description = null);

public record ReorderWorkItemsRequest(IReadOnlyList<int> OrderedIds);

public class WorkItemService(HrdrDbContext db)
{
    public async Task<IReadOnlyList<WorkItemDto>> ListAsync(
        int? projectId = null,
        WorkItemType? type = null,
        CancellationToken ct = default)
    {
        var query = db.WorkItems.AsNoTracking().Include(w => w.Project).AsQueryable();

        if (projectId is not null)
            query = query.Where(w => w.ProjectId == projectId);

        if (type is not null)
            query = query.Where(w => w.Type == type);

        var items = await query
            .OrderBy(w => w.ProjectId)
            .ThenBy(w => w.SortOrder)
            .ThenBy(w => w.Id)
            .ToListAsync(ct);

        return items.Select(ToDto).ToList();
    }

    public async Task<WorkItemDto?> GetAsync(int id, CancellationToken ct = default)
    {
        var item = await db.WorkItems.AsNoTracking()
            .Include(w => w.Project)
            .FirstOrDefaultAsync(w => w.Id == id, ct);
        return item is null ? null : ToDto(item);
    }

    public async Task<WorkItemDto> CreateAsync(CreateWorkItemRequest request, CancellationToken ct = default)
    {
        var title = request.Title.Trim();
        if (string.IsNullOrWhiteSpace(title))
            throw new ArgumentException("Title is required.", nameof(request));

        var projectExists = await db.Projects.AnyAsync(p => p.Id == request.ProjectId, ct);
        if (!projectExists)
            throw new ArgumentException($"Project {request.ProjectId} not found.", nameof(request));

        var maxOrder = await db.WorkItems
            .Where(w => w.ProjectId == request.ProjectId)
            .Select(w => (int?)w.SortOrder)
            .MaxAsync(ct) ?? -1;

        var now = DateTimeOffset.UtcNow;
        var item = new WorkItem
        {
            ProjectId = request.ProjectId,
            Type = request.Type,
            Title = title,
            Description = request.Description?.Trim() ?? "",
            SortOrder = maxOrder + 1,
            CreatedAt = now,
            UpdatedAt = now
        };

        db.WorkItems.Add(item);
        await db.SaveChangesAsync(ct);

        await db.Entry(item).Reference(w => w.Project).LoadAsync(ct);
        return ToDto(item);
    }

    public async Task<WorkItemDto?> UpdateAsync(int id, UpdateWorkItemRequest request, CancellationToken ct = default)
    {
        var item = await db.WorkItems.Include(w => w.Project).FirstOrDefaultAsync(w => w.Id == id, ct);
        if (item is null) return null;

        if (request.ProjectId is int newProjectId && newProjectId != item.ProjectId)
        {
            var projectExists = await db.Projects.AnyAsync(p => p.Id == newProjectId, ct);
            if (!projectExists)
                throw new ArgumentException($"Project {newProjectId} not found.", nameof(request));

            var maxOrder = await db.WorkItems
                .Where(w => w.ProjectId == newProjectId)
                .Select(w => (int?)w.SortOrder)
                .MaxAsync(ct) ?? -1;

            item.ProjectId = newProjectId;
            item.SortOrder = maxOrder + 1;
        }

        if (request.Type is not null)
            item.Type = request.Type.Value;

        if (request.Title is not null)
        {
            var title = request.Title.Trim();
            if (string.IsNullOrWhiteSpace(title))
                throw new ArgumentException("Title is required.", nameof(request));
            item.Title = title;
        }

        if (request.Description is not null)
            item.Description = request.Description.Trim();

        item.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        await db.Entry(item).Reference(w => w.Project).LoadAsync(ct);
        return ToDto(item);
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken ct = default)
    {
        var item = await db.WorkItems.FirstOrDefaultAsync(w => w.Id == id, ct);
        if (item is null) return false;
        db.WorkItems.Remove(item);
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task ReorderAsync(int projectId, ReorderWorkItemsRequest request, CancellationToken ct = default)
    {
        var projectExists = await db.Projects.AnyAsync(p => p.Id == projectId, ct);
        if (!projectExists)
            throw new ArgumentException($"Project {projectId} not found.", nameof(projectId));

        var orderedIds = request.OrderedIds;
        if (orderedIds.Count == 0)
            throw new ArgumentException("OrderedIds must not be empty.", nameof(request));

        if (orderedIds.Distinct().Count() != orderedIds.Count)
            throw new ArgumentException("OrderedIds must be unique.", nameof(request));

        var items = await db.WorkItems.Where(w => w.ProjectId == projectId).ToListAsync(ct);
        if (items.Count != orderedIds.Count)
            throw new ArgumentException("OrderedIds must include every item in the project exactly once.", nameof(request));

        var byId = items.ToDictionary(i => i.Id);
        foreach (var id in orderedIds)
        {
            if (!byId.ContainsKey(id))
                throw new ArgumentException($"Item {id} is not in project {projectId}.", nameof(request));
        }

        var now = DateTimeOffset.UtcNow;
        for (var i = 0; i < orderedIds.Count; i++)
        {
            var item = byId[orderedIds[i]];
            item.SortOrder = i;
            item.UpdatedAt = now;
        }

        await db.SaveChangesAsync(ct);
    }

    private static WorkItemDto ToDto(WorkItem w) =>
        new(w.Id, w.ProjectId, w.Project?.Name, w.Type, w.Title, w.Description, w.SortOrder, w.CreatedAt, w.UpdatedAt);
}
