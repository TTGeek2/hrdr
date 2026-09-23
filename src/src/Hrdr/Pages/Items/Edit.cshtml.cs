using Hrdr.Core.Entities;
using Hrdr.Core.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace Hrdr.Pages.Items;

public class EditModel(WorkItemService workItems, ProjectService projects, CommentService comments) : PageModel
{
    [BindProperty]
    public int Id { get; set; }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    [BindProperty]
    public string? NewCommentBody { get; set; }

    public IReadOnlyList<CommentDto> Comments { get; private set; } = [];
    public SelectList ProjectOptions { get; private set; } = null!;
    public string? ErrorMessage { get; private set; }
    public string? CommentErrorMessage { get; private set; }

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

        await LoadPageAsync(item, ct);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        await LoadSupportingDataAsync(ct);

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

    public async Task<IActionResult> OnPostAddCommentAsync(CancellationToken ct)
    {
        var item = await workItems.GetAsync(Id, ct);
        if (item is null) return NotFound();

        try
        {
            await comments.CreateAsync(new CreateCommentRequest(Id, NewCommentBody ?? ""), ct);
            NewCommentBody = null;
            await LoadPageAsync(item, ct);
            return Page();
        }
        catch (ArgumentException ex)
        {
            CommentErrorMessage = ex.Message;
            await LoadPageAsync(item, ct);
            return Page();
        }
    }

    public async Task<IActionResult> OnPostDeleteCommentAsync(int commentId, CancellationToken ct)
    {
        var item = await workItems.GetAsync(Id, ct);
        if (item is null) return NotFound();

        await comments.DeleteAsync(commentId, ct);
        await LoadPageAsync(item, ct);
        return Page();
    }

    private async Task LoadPageAsync(WorkItemDto item, CancellationToken ct)
    {
        Id = item.Id;
        Input = new InputModel
        {
            ProjectId = item.ProjectId,
            Type = item.Type,
            State = item.State,
            Title = item.Title,
            Description = item.Description
        };
        await LoadSupportingDataAsync(ct);
    }

    private async Task LoadSupportingDataAsync(CancellationToken ct)
    {
        var projectList = await projects.ListAsync(ct);
        ProjectOptions = new SelectList(projectList, nameof(ProjectDto.Id), nameof(ProjectDto.Name), Input.ProjectId);
        Comments = await comments.ListAsync(Id, ct);
    }
}
