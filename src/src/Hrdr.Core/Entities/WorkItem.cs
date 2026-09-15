namespace Hrdr.Core.Entities;

public class WorkItem
{
    public int Id { get; set; }
    public int ProjectId { get; set; }
    public WorkItemType Type { get; set; }
    public required string Title { get; set; }
    public string Description { get; set; } = "";
    public int SortOrder { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public Project Project { get; set; } = null!;
}
