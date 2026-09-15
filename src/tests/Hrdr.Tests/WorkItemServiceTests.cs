using Hrdr.Core.Data;
using Hrdr.Core.Entities;
using Hrdr.Core.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Hrdr.Tests;

public class WorkItemServiceTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection;
    private readonly HrdrDbContext _db;
    private readonly ProjectService _projects;
    private readonly WorkItemService _items;

    public WorkItemServiceTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<HrdrDbContext>()
            .UseSqlite(_connection)
            .Options;
        _db = new HrdrDbContext(options);
        _db.Database.EnsureCreated();
        _projects = new ProjectService(_db);
        _items = new WorkItemService(_db);
    }

    public async Task InitializeAsync() => await Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task Create_assigns_sort_order_and_list_filters_by_project()
    {
        var p1 = await _projects.CreateAsync(new CreateProjectRequest("Alpha"));
        var p2 = await _projects.CreateAsync(new CreateProjectRequest("Beta"));

        var a = await _items.CreateAsync(new CreateWorkItemRequest(p1.Id, WorkItemType.Feature, "A"));
        var b = await _items.CreateAsync(new CreateWorkItemRequest(p1.Id, WorkItemType.Bug, "B"));
        await _items.CreateAsync(new CreateWorkItemRequest(p2.Id, WorkItemType.Feature, "C"));

        Assert.Equal(0, a.SortOrder);
        Assert.Equal(1, b.SortOrder);

        var filtered = await _items.ListAsync(projectId: p1.Id);
        Assert.Equal(2, filtered.Count);
        Assert.All(filtered, i => Assert.Equal(p1.Id, i.ProjectId));

        var bugs = await _items.ListAsync(projectId: p1.Id, type: WorkItemType.Bug);
        Assert.Single(bugs);
        Assert.Equal("B", bugs[0].Title);
    }

    [Fact]
    public async Task Reorder_rewrites_sort_order_for_project()
    {
        var p = await _projects.CreateAsync(new CreateProjectRequest("Alpha"));
        var a = await _items.CreateAsync(new CreateWorkItemRequest(p.Id, WorkItemType.Feature, "A"));
        var b = await _items.CreateAsync(new CreateWorkItemRequest(p.Id, WorkItemType.Feature, "B"));
        var c = await _items.CreateAsync(new CreateWorkItemRequest(p.Id, WorkItemType.Bug, "C"));

        await _items.ReorderAsync(p.Id, new ReorderWorkItemsRequest([c.Id, a.Id, b.Id]));

        var list = await _items.ListAsync(projectId: p.Id);
        Assert.Equal([c.Id, a.Id, b.Id], list.Select(i => i.Id).ToArray());
        Assert.Equal([0, 1, 2], list.Select(i => i.SortOrder).ToArray());
    }
}
