namespace Hrdr.Core.Entities;

public class Project
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public string? Slug { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public ICollection<WorkItem> WorkItems { get; set; } = new List<WorkItem>();
}
