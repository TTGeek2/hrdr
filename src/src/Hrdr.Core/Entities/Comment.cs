namespace Hrdr.Core.Entities;

public class Comment
{
    public int Id { get; set; }
    public int WorkItemId { get; set; }
    public string Body { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public WorkItem WorkItem { get; set; } = null!;
}
