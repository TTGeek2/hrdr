using Hrdr.Core.Data;
using Hrdr.Core.Entities;
using Hrdr.Core.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Hrdr.Tests;

public class CommentServiceTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection;
    private readonly HrdrDbContext _db;
    private readonly ProjectService _projects;
    private readonly WorkItemService _items;
    private readonly CommentService _comments;

    public CommentServiceTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<HrdrDbContext>()
            .UseSqlite(_connection)
            .Options;
        _db = new HrdrDbContext(options);
        _projects = new ProjectService(_db);
        _items = new WorkItemService(_db);
        _comments = new CommentService(_db);
    }

    public async Task InitializeAsync() => await _db.EnsureDatabaseAsync();

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task Create_and_list_orders_by_created_at()
    {
        var project = await _projects.CreateAsync(new CreateProjectRequest("Alpha"));
        var item = await _items.CreateAsync(new CreateWorkItemRequest(project.Id, WorkItemType.Feature, "A"));

        var first = await _comments.CreateAsync(new CreateCommentRequest(item.Id, "First"));
        var second = await _comments.CreateAsync(new CreateCommentRequest(item.Id, "Second"));

        var list = await _comments.ListAsync(item.Id);
        Assert.Equal(2, list.Count);
        Assert.Equal([first.Id, second.Id], list.Select(c => c.Id).ToArray());
        Assert.Equal("First", list[0].Body);
        Assert.Equal("Second", list[1].Body);
    }

    [Fact]
    public async Task Create_rejects_empty_body_and_missing_item()
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _comments.CreateAsync(new CreateCommentRequest(1, "  ")));

        await Assert.ThrowsAsync<ArgumentException>(() =>
            _comments.CreateAsync(new CreateCommentRequest(999, "Hello")));
    }

    [Fact]
    public async Task Delete_removes_comment()
    {
        var project = await _projects.CreateAsync(new CreateProjectRequest("Alpha"));
        var item = await _items.CreateAsync(new CreateWorkItemRequest(project.Id, WorkItemType.Feature, "A"));
        var comment = await _comments.CreateAsync(new CreateCommentRequest(item.Id, "Temp"));

        Assert.True(await _comments.DeleteAsync(comment.Id));
        Assert.Empty(await _comments.ListAsync(item.Id));
        Assert.False(await _comments.DeleteAsync(comment.Id));
    }

    [Fact]
    public async Task Deleting_work_item_cascades_comments()
    {
        var project = await _projects.CreateAsync(new CreateProjectRequest("Alpha"));
        var item = await _items.CreateAsync(new CreateWorkItemRequest(project.Id, WorkItemType.Feature, "A"));
        var comment = await _comments.CreateAsync(new CreateCommentRequest(item.Id, "Gone"));

        Assert.True(await _items.DeleteAsync(item.Id));
        Assert.Null(await _comments.GetAsync(comment.Id));
    }
}
