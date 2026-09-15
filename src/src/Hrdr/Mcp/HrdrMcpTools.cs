using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using Hrdr.Core.Entities;
using Hrdr.Core.Services;
using ModelContextProtocol.Server;

namespace Hrdr.Mcp;

[McpServerToolType]
public static class HrdrMcpTools
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    [McpServerTool(Name = "list_projects"), Description("List all projects.")]
    public static async Task<string> ListProjects(
        ProjectService projects,
        CancellationToken ct)
        => Json(await projects.ListAsync(ct));

    [McpServerTool(Name = "create_project"), Description("Create a project. Slug is optional and derived from name when omitted.")]
    public static async Task<string> CreateProject(
        ProjectService projects,
        [Description("Project name")] string name,
        [Description("Optional URL slug")] string? slug = null,
        CancellationToken ct = default)
    {
        try
        {
            return Json(await projects.CreateAsync(new CreateProjectRequest(name, slug), ct));
        }
        catch (ArgumentException ex)
        {
            return Error(ex.Message);
        }
    }

    [McpServerTool(Name = "update_project"), Description("Update a project's name and optionally its slug.")]
    public static async Task<string> UpdateProject(
        ProjectService projects,
        [Description("Project id")] int id,
        [Description("New project name")] string name,
        [Description("Optional URL slug; omit to leave unchanged, empty to re-derive from name")] string? slug = null,
        CancellationToken ct = default)
    {
        try
        {
            var updated = await projects.UpdateAsync(id, new UpdateProjectRequest(name, slug), ct);
            return updated is null ? Error($"Project {id} not found.") : Json(updated);
        }
        catch (ArgumentException ex)
        {
            return Error(ex.Message);
        }
    }

    [McpServerTool(Name = "delete_project"), Description("Delete a project by id.")]
    public static async Task<string> DeleteProject(
        ProjectService projects,
        [Description("Project id")] int id,
        CancellationToken ct = default)
        => await projects.DeleteAsync(id, ct)
            ? """{"ok":true}"""
            : Error($"Project {id} not found.");

    [McpServerTool(Name = "list_items"), Description("List work items. Optionally filter by projectId and type (Feature or Bug).")]
    public static async Task<string> ListItems(
        WorkItemService items,
        [Description("Optional project id filter")] int? projectId = null,
        [Description("Optional type filter: Feature or Bug")] WorkItemType? type = null,
        CancellationToken ct = default)
        => Json(await items.ListAsync(projectId, type, ct));

    [McpServerTool(Name = "get_item"), Description("Get a work item by id.")]
    public static async Task<string> GetItem(
        WorkItemService items,
        [Description("Work item id")] int id,
        CancellationToken ct = default)
    {
        var item = await items.GetAsync(id, ct);
        return item is null ? Error($"Item {id} not found.") : Json(item);
    }

    [McpServerTool(Name = "create_item"), Description("Create a work item in a project.")]
    public static async Task<string> CreateItem(
        WorkItemService items,
        [Description("Project id")] int projectId,
        [Description("Item type: Feature or Bug")] WorkItemType type,
        [Description("Item title")] string title,
        [Description("Optional description")] string? description = null,
        CancellationToken ct = default)
    {
        try
        {
            return Json(await items.CreateAsync(
                new CreateWorkItemRequest(projectId, type, title, description), ct));
        }
        catch (ArgumentException ex)
        {
            return Error(ex.Message);
        }
    }

    [McpServerTool(Name = "update_item"), Description("Update a work item. Only provided fields are changed.")]
    public static async Task<string> UpdateItem(
        WorkItemService items,
        [Description("Work item id")] int id,
        [Description("Optional new project id")] int? projectId = null,
        [Description("Optional type: Feature or Bug")] WorkItemType? type = null,
        [Description("Optional new title")] string? title = null,
        [Description("Optional new description")] string? description = null,
        CancellationToken ct = default)
    {
        try
        {
            var updated = await items.UpdateAsync(
                id,
                new UpdateWorkItemRequest(projectId, type, title, description),
                ct);
            return updated is null ? Error($"Item {id} not found.") : Json(updated);
        }
        catch (ArgumentException ex)
        {
            return Error(ex.Message);
        }
    }

    [McpServerTool(Name = "delete_item"), Description("Delete a work item by id.")]
    public static async Task<string> DeleteItem(
        WorkItemService items,
        [Description("Work item id")] int id,
        CancellationToken ct = default)
        => await items.DeleteAsync(id, ct)
            ? """{"ok":true}"""
            : Error($"Item {id} not found.");

    [McpServerTool(Name = "reorder_items"), Description("Reorder all work items in a project. orderedIds must list every item id exactly once.")]
    public static async Task<string> ReorderItems(
        WorkItemService items,
        [Description("Project id")] int projectId,
        [Description("Item ids in the desired order")] int[] orderedIds,
        CancellationToken ct = default)
    {
        try
        {
            await items.ReorderAsync(projectId, new ReorderWorkItemsRequest(orderedIds), ct);
            return """{"ok":true}""";
        }
        catch (ArgumentException ex)
        {
            return Error(ex.Message);
        }
    }

    private static string Json<T>(T value) =>
        JsonSerializer.Serialize(value, JsonOptions);

    private static string Error(string message) =>
        JsonSerializer.Serialize(new { error = message }, JsonOptions);
}
