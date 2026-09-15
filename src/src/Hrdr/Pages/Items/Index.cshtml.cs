using Hrdr.Core.Entities;
using Hrdr.Core.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace Hrdr.Pages.Items;

public class IndexModel(WorkItemService workItems, ProjectService projects) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public int? ProjectId { get; set; }

    [BindProperty(SupportsGet = true)]
    public WorkItemType? Type { get; set; }

    public IReadOnlyList<WorkItemDto> Items { get; private set; } = [];
    public SelectList ProjectOptions { get; private set; } = null!;
    public bool CanReorder => ProjectId is > 0;

    public async Task OnGetAsync(CancellationToken ct)
    {
        await LoadAsync(ct);
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id, CancellationToken ct)
    {
        await workItems.DeleteAsync(id, ct);
        return RedirectToPage(new { ProjectId, Type });
    }

    private async Task LoadAsync(CancellationToken ct)
    {
        var projectList = await projects.ListAsync(ct);
        ProjectOptions = new SelectList(projectList, nameof(ProjectDto.Id), nameof(ProjectDto.Name), ProjectId);
        Items = await workItems.ListAsync(ProjectId, Type, ct);
    }
}
