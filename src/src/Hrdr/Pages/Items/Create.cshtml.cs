using Hrdr.Core.Entities;
using Hrdr.Core.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace Hrdr.Pages.Items;

public class CreateModel(WorkItemService workItems, ProjectService projects) : PageModel
{
    [BindProperty]
    public InputModel Input { get; set; } = new();

    public SelectList ProjectOptions { get; private set; } = null!;
    public string? ErrorMessage { get; private set; }

    public class InputModel
    {
        public int ProjectId { get; set; }
        public WorkItemType Type { get; set; } = WorkItemType.Feature;
        public string Title { get; set; } = "";
        public string? Description { get; set; }
    }

    public async Task<IActionResult> OnGetAsync(int? projectId, CancellationToken ct)
    {
        var projectList = await projects.ListAsync(ct);
        if (projectList.Count == 0)
        {
            TempData["Error"] = "Create a project before adding work items.";
            return RedirectToPage("/Projects/Index");
        }

        if (projectId is int id && projectList.Any(p => p.Id == id))
            Input.ProjectId = id;
        else
            Input.ProjectId = projectList[0].Id;

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
            var created = await workItems.CreateAsync(
                new CreateWorkItemRequest(Input.ProjectId, Input.Type, Input.Title, Input.Description),
                ct);
            return RedirectToPage("Index", new { ProjectId = created.ProjectId });
        }
        catch (ArgumentException ex)
        {
            ErrorMessage = ex.Message;
            return Page();
        }
    }
}
