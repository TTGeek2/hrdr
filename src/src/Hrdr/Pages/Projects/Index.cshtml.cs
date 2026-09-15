using Hrdr.Core.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Hrdr.Pages.Projects;

public class IndexModel(ProjectService projects) : PageModel
{
    public IReadOnlyList<ProjectDto> Projects { get; private set; } = [];
    public string? ErrorMessage { get; private set; }

    [BindProperty]
    public string NewName { get; set; } = "";

    [BindProperty]
    public string RenameName { get; set; } = "";

    public async Task OnGetAsync(CancellationToken ct)
    {
        if (TempData["Error"] is string err)
            ErrorMessage = err;
        Projects = await projects.ListAsync(ct);
    }

    public async Task<IActionResult> OnPostCreateAsync(CancellationToken ct)
    {
        try
        {
            await projects.CreateAsync(new CreateProjectRequest(NewName), ct);
            return RedirectToPage();
        }
        catch (ArgumentException ex)
        {
            ErrorMessage = ex.Message;
            Projects = await projects.ListAsync(ct);
            return Page();
        }
    }

    public async Task<IActionResult> OnPostRenameAsync(int id, CancellationToken ct)
    {
        try
        {
            var updated = await projects.UpdateAsync(id, new UpdateProjectRequest(RenameName), ct);
            if (updated is null) return NotFound();
            return RedirectToPage();
        }
        catch (ArgumentException ex)
        {
            ErrorMessage = ex.Message;
            Projects = await projects.ListAsync(ct);
            return Page();
        }
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id, CancellationToken ct)
    {
        await projects.DeleteAsync(id, ct);
        return RedirectToPage();
    }
}
