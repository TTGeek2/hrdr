using Hrdr.Core.Entities;
using Hrdr.Core.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace Hrdr.Pages.Items;

public class EditModel(WorkItemService workItems, ProjectService projects) : PageModel
{
    [BindProperty]
    public int Id { get; set; }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public SelectList ProjectOptions { get; private set; } = null!;
    public string? ErrorMessage { get; private set; }

    public class InputModel
    {
        public int ProjectId { get; set; }
        public WorkItemType Type { get; set; }
        public WorkItemState State { get; set; } = WorkItemState.Created;
        public string Title { get; set; } = "";
        public string? Description { get; set; }
    }

    public async Task<IActionResult> OnGetAsync(int id, CancellationToken ct)
    {
        var item = await workItems.GetAsync(id, ct);
        if (item is null) return NotFound();

        Id = item.Id;
        Input = new InputModel
        {
            ProjectId = item.ProjectId,
            Type = item.Type,
            State = item.State,
            Title = item.Title,
            Description = item.Description
        };

        var projectList = await projects.ListAsync(ct);
        ProjectOptions = new SelectList(projectList, nameof(ProjectDto.Id), nameof(ProjectDto.Name), Input.ProjectId);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        var projectList = await projects.ListAsync(ct);
        ProjectOptions = new SelectList(projectList, nameof(ProjectDto.Id), nameof(ProjectDto.Name), Input.ProjectId);

        if (string.IsNullOrWhiteSpace(Input.Title))
        {
            ErrorMessage = "Title is required.";
            return Page();
        }

        try
        {
            var updated = await workItems.UpdateAsync(
                Id,
                new UpdateWorkItemRequest(Input.ProjectId, Input.Type, Input.Title, Input.Description, Input.State),
                ct);
            if (updated is null) return NotFound();
            return RedirectToPage("Index", new { ProjectId = updated.ProjectId });
        }
        catch (ArgumentException ex)
        {
            ErrorMessage = ex.Message;
            return Page();
        }
    }
}
