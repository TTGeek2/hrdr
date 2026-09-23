using Hrdr.Core.Data;
using Hrdr.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace Hrdr.Core.Services;

public record CommentDto(
    int Id,
    int WorkItemId,
    string Body,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public record CreateCommentRequest(int WorkItemId, string Body);

public class CommentService(HrdrDbContext db)
{
    public async Task<IReadOnlyList<CommentDto>> ListAsync(int workItemId, CancellationToken ct = default)
    {
        var comments = await db.Comments.AsNoTracking()
            .Where(c => c.WorkItemId == workItemId)
            .OrderBy(c => c.Id)
            .ToListAsync(ct);

        return comments.Select(ToDto).ToList();
    }

    public async Task<CommentDto?> GetAsync(int id, CancellationToken ct = default)
    {
        var comment = await db.Comments.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == id, ct);
        return comment is null ? null : ToDto(comment);
    }

    public async Task<CommentDto> CreateAsync(CreateCommentRequest request, CancellationToken ct = default)
    {
        var body = request.Body.Trim();
        if (string.IsNullOrWhiteSpace(body))
            throw new ArgumentException("Body is required.", nameof(request));

        var itemExists = await db.WorkItems.AnyAsync(w => w.Id == request.WorkItemId, ct);
        if (!itemExists)
            throw new ArgumentException($"Work item {request.WorkItemId} not found.", nameof(request));

        var now = DateTimeOffset.UtcNow;
        var comment = new Comment
        {
            WorkItemId = request.WorkItemId,
            Body = body,
            CreatedAt = now,
            UpdatedAt = now
        };

        db.Comments.Add(comment);
        await db.SaveChangesAsync(ct);
        return ToDto(comment);
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken ct = default)
    {
        var comment = await db.Comments.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (comment is null) return false;
        db.Comments.Remove(comment);
        await db.SaveChangesAsync(ct);
        return true;
    }

    private static CommentDto ToDto(Comment c) =>
        new(c.Id, c.WorkItemId, c.Body, c.CreatedAt, c.UpdatedAt);
}
