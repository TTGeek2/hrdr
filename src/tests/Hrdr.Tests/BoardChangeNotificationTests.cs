using Hrdr.Core.Data;
using Hrdr.Core.Entities;
using Hrdr.Core.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Hrdr.Tests;

public class BoardChangeNotificationTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection;
    private readonly HrdrDbContext _db;
    private readonly RecordingBoardChangeNotifier _changes;
    private readonly ProjectService _projects;
    private readonly WorkItemService _items;
    private readonly CommentService _comments;
    private readonly BoardColumnService _board;

    public BoardChangeNotificationTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<HrdrDbContext>()
            .UseSqlite(_connection)
            .Options;
        _db = new HrdrDbContext(options);
        _changes = new RecordingBoardChangeNotifier();
        _projects = new ProjectService(_db, _changes);
        _items = new WorkItemService(_db, _changes);
        _comments = new CommentService(_db, _changes);
        _board = new BoardColumnService(_db, _changes);
    }

    public async Task InitializeAsync() => await _db.EnsureDatabaseAsync();

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task Item_changes_notify_only_after_they_are_saved()
    {
        var first = await _projects.CreateAsync(new CreateProjectRequest("Alpha"));
        var second = await _projects.CreateAsync(new CreateProjectRequest("Beta"));
        _changes.Changes.Clear();

        var item = await _items.CreateAsync(new CreateWorkItemRequest(first.Id, WorkItemType.Feature, "A", "notes"));
        var created = Assert.Single(_changes.Changes);
        Assert.Equal(BoardChangeKind.WorkItemCreated, created.Kind);
        Assert.Equal(item.Id, created.WorkItemId);
        Assert.Equal(first.Id, created.ProjectId);
        Assert.Equal("A", created.Item!.Title);
        Assert.Equal("notes", created.Item.Description);
        Assert.Null(created.OriginClientId);

        _changes.Changes.Clear();
        var movedState = await _items.UpdateAsync(item.Id, new UpdateWorkItemRequest(State: WorkItemState.Done));
        var stateChange = Assert.Single(_changes.Changes);
        Assert.Equal(BoardChangeKind.WorkItemUpdated, stateChange.Kind);
        Assert.Equal(WorkItemState.Done, stateChange.Item!.State);
        Assert.Equal(item.Id, stateChange.WorkItemId);
        Assert.Null(stateChange.PreviousProjectId);
        Assert.NotNull(movedState);

        _changes.Changes.Clear();
        var moved = await _items.UpdateAsync(item.Id, new UpdateWorkItemRequest(
            ProjectId: second.Id,
            Type: WorkItemType.Bug,
            Title: "B",
            Description: "more",
            State: WorkItemState.InDevelopment));
        var update = Assert.Single(_changes.Changes);
        Assert.Equal(BoardChangeKind.WorkItemUpdated, update.Kind);
        Assert.Equal(first.Id, update.PreviousProjectId);
        Assert.Equal(second.Id, update.ProjectId);
        Assert.Equal(WorkItemType.Bug, update.Item!.Type);
        Assert.Equal("B", update.Item.Title);
        Assert.Equal(WorkItemState.InDevelopment, update.Item.State);
        Assert.NotNull(moved);

        var other = await _items.CreateAsync(new CreateWorkItemRequest(second.Id, WorkItemType.Feature, "C"));
        _changes.Changes.Clear();
        await _items.ReorderAsync(second.Id, new ReorderWorkItemsRequest([other.Id, moved!.Id]));
        var reorder = Assert.Single(_changes.Changes);
        Assert.Equal(BoardChangeKind.WorkItemsReordered, reorder.Kind);
        Assert.Equal(second.Id, reorder.ProjectId);
        Assert.Equal([other.Id, moved.Id], reorder.OrderedIds);

        _changes.Changes.Clear();
        Assert.True(await _items.DeleteAsync(moved.Id));
        var deleted = Assert.Single(_changes.Changes);
        Assert.Equal(BoardChangeKind.WorkItemDeleted, deleted.Kind);
        Assert.Equal(moved.Id, deleted.WorkItemId);
        Assert.Equal(second.Id, deleted.ProjectId);
        Assert.Null(deleted.Item);
    }

    [Fact]
    public async Task Project_changes_notify_only_after_they_are_saved()
    {
        _changes.Changes.Clear();
        var project = await _projects.CreateAsync(new CreateProjectRequest("Alpha", "alpha"));
        var created = Assert.Single(_changes.Changes);
        Assert.Equal(BoardChangeKind.ProjectCreated, created.Kind);
        Assert.Equal(project.Id, created.ProjectId);
        Assert.Equal("Alpha", created.Project!.Name);
        Assert.Equal(project.Slug, created.Project.Slug);

        _changes.Changes.Clear();
        var updated = await _projects.UpdateAsync(project.Id, new UpdateProjectRequest("Alpha 2", "alpha-2"));
        var change = Assert.Single(_changes.Changes);
        Assert.Equal(BoardChangeKind.ProjectUpdated, change.Kind);
        Assert.Equal("Alpha 2", change.Project!.Name);
        Assert.Equal(updated!.Slug, change.Project.Slug);

        _changes.Changes.Clear();
        Assert.True(await _projects.DeleteAsync(project.Id));
        var deleted = Assert.Single(_changes.Changes);
        Assert.Equal(BoardChangeKind.ProjectDeleted, deleted.Kind);
        Assert.Equal(project.Id, deleted.ProjectId);
        Assert.Null(deleted.Project);
    }

    [Fact]
    public async Task Comment_changes_notify_only_after_they_are_saved()
    {
        var project = await _projects.CreateAsync(new CreateProjectRequest("Alpha"));
        var item = await _items.CreateAsync(new CreateWorkItemRequest(project.Id, WorkItemType.Feature, "A"));
        _changes.Changes.Clear();

        var comment = await _comments.CreateAsync(new CreateCommentRequest(item.Id, "Hello"));
        var created = Assert.Single(_changes.Changes);
        Assert.Equal(BoardChangeKind.CommentCreated, created.Kind);
        Assert.Equal(comment.Id, created.CommentId);
        Assert.Equal(item.Id, created.WorkItemId);
        Assert.Equal("Hello", created.Comment!.Body);

        _changes.Changes.Clear();
        Assert.True(await _comments.DeleteAsync(comment.Id));
        var deleted = Assert.Single(_changes.Changes);
        Assert.Equal(BoardChangeKind.CommentDeleted, deleted.Kind);
        Assert.Equal(comment.Id, deleted.CommentId);
        Assert.Equal(item.Id, deleted.WorkItemId);
        Assert.Null(deleted.Comment);
    }

    [Fact]
    public async Task Column_reorder_notifies_with_display_names()
    {
        var desired = new[]
        {
            WorkItemState.Released,
            WorkItemState.Done,
            WorkItemState.OnPullRequest,
            WorkItemState.InDevelopment,
            WorkItemState.Refined,
            WorkItemState.Created
        };

        _changes.Changes.Clear();
        await _board.ReorderAsync(new ReorderBoardColumnsRequest(desired.Select(s => s.ToDisplayString()).ToArray()));

        var change = Assert.Single(_changes.Changes);
        Assert.Equal(BoardChangeKind.BoardColumnsReordered, change.Kind);
        Assert.Equal(desired.Select(s => s.ToDisplayString()).ToArray(), change.OrderedStates);
    }

    [Fact]
    public async Task Failed_mutations_do_not_notify()
    {
        var project = await _projects.CreateAsync(new CreateProjectRequest("Alpha"));
        var item = await _items.CreateAsync(new CreateWorkItemRequest(project.Id, WorkItemType.Feature, "A"));
        var comment = await _comments.CreateAsync(new CreateCommentRequest(item.Id, "Note"));
        var before = _changes.Changes.Count;

        await Assert.ThrowsAsync<ArgumentException>(() =>
            _items.CreateAsync(new CreateWorkItemRequest(project.Id, WorkItemType.Feature, "  ")));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _items.CreateAsync(new CreateWorkItemRequest(999, WorkItemType.Feature, "Missing project")));
        Assert.Null(await _items.UpdateAsync(999, new UpdateWorkItemRequest(Title: "Missing")));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _items.UpdateAsync(item.Id, new UpdateWorkItemRequest(Title: "  ")));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _items.UpdateAsync(item.Id, new UpdateWorkItemRequest(ProjectId: 999)));
        Assert.False(await _items.DeleteAsync(999));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _items.ReorderAsync(project.Id, new ReorderWorkItemsRequest([item.Id + 1])));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _items.ReorderAsync(999, new ReorderWorkItemsRequest([item.Id])));

        await Assert.ThrowsAsync<ArgumentException>(() =>
            _projects.CreateAsync(new CreateProjectRequest("  ")));
        Assert.Null(await _projects.UpdateAsync(999, new UpdateProjectRequest("Missing")));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _projects.UpdateAsync(project.Id, new UpdateProjectRequest("  ")));
        Assert.False(await _projects.DeleteAsync(999));

        await Assert.ThrowsAsync<ArgumentException>(() =>
            _comments.CreateAsync(new CreateCommentRequest(item.Id, "  ")));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _comments.CreateAsync(new CreateCommentRequest(999, "Hello")));
        Assert.False(await _comments.DeleteAsync(999));

        await Assert.ThrowsAsync<ArgumentException>(() =>
            _board.ReorderAsync(new ReorderBoardColumnsRequest(["Created", "done"])));

        Assert.Equal(before, _changes.Changes.Count);
        Assert.Equal("A", (await _items.GetAsync(item.Id))!.Title);
        Assert.Equal(comment.Id, (await _comments.GetAsync(comment.Id))!.Id);
    }

    private sealed class RecordingBoardChangeNotifier : IBoardChangeNotifier
    {
        public List<BoardChange> Changes { get; } = [];

        public Task NotifyAsync(BoardChange change, CancellationToken cancellationToken = default)
        {
            Changes.Add(change);
            return Task.CompletedTask;
        }
    }
}
