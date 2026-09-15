using Hrdr.Core.Entities;
using Hrdr.Core.Services;

namespace Hrdr.Api;

public static class ApiEndpoints
{
    public static IEndpointRouteBuilder MapHrdrApi(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api");

        var projects = api.MapGroup("/projects");
        projects.MapGet("/", async (ProjectService svc, CancellationToken ct) =>
            Results.Ok(await svc.ListAsync(ct)));

        projects.MapGet("/{id:int}", async (int id, ProjectService svc, CancellationToken ct) =>
        {
            var project = await svc.GetAsync(id, ct);
            return project is null ? Results.NotFound() : Results.Ok(project);
        });

        projects.MapPost("/", async (CreateProjectRequest body, ProjectService svc, CancellationToken ct) =>
        {
            try
            {
                var created = await svc.CreateAsync(body, ct);
                return Results.Created($"/api/projects/{created.Id}", created);
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        projects.MapPut("/{id:int}", async (int id, UpdateProjectRequest body, ProjectService svc, CancellationToken ct) =>
        {
            try
            {
                var updated = await svc.UpdateAsync(id, body, ct);
                return updated is null ? Results.NotFound() : Results.Ok(updated);
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        projects.MapDelete("/{id:int}", async (int id, ProjectService svc, CancellationToken ct) =>
            await svc.DeleteAsync(id, ct) ? Results.NoContent() : Results.NotFound());

        projects.MapPut("/{projectId:int}/items/reorder", async (
            int projectId,
            ReorderWorkItemsRequest body,
            WorkItemService svc,
            CancellationToken ct) =>
        {
            try
            {
                await svc.ReorderAsync(projectId, body, ct);
                return Results.NoContent();
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        var items = api.MapGroup("/items");
        items.MapGet("/", async (
            int? projectId,
            WorkItemType? type,
            WorkItemService svc,
            CancellationToken ct) =>
            Results.Ok(await svc.ListAsync(projectId, type, ct)));

        items.MapGet("/{id:int}", async (int id, WorkItemService svc, CancellationToken ct) =>
        {
            var item = await svc.GetAsync(id, ct);
            return item is null ? Results.NotFound() : Results.Ok(item);
        });

        items.MapPost("/", async (CreateWorkItemRequest body, WorkItemService svc, CancellationToken ct) =>
        {
            try
            {
                var created = await svc.CreateAsync(body, ct);
                return Results.Created($"/api/items/{created.Id}", created);
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        items.MapPut("/{id:int}", async (int id, UpdateWorkItemRequest body, WorkItemService svc, CancellationToken ct) =>
        {
            try
            {
                var updated = await svc.UpdateAsync(id, body, ct);
                return updated is null ? Results.NotFound() : Results.Ok(updated);
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        items.MapDelete("/{id:int}", async (int id, WorkItemService svc, CancellationToken ct) =>
            await svc.DeleteAsync(id, ct) ? Results.NoContent() : Results.NotFound());

        return app;
    }
}
