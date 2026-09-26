using Hrdr.Core.Data;
using Hrdr.Core.Entities;
using Hrdr.Core.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Hrdr.Tests;

public class BoardColumnServiceTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection;
    private readonly HrdrDbContext _db;
    private readonly BoardColumnService _board;

    public BoardColumnServiceTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<HrdrDbContext>()
            .UseSqlite(_connection)
            .Options;
        _db = new HrdrDbContext(options);
        _board = new BoardColumnService(_db);
    }

    public async Task InitializeAsync() => await _db.EnsureDatabaseAsync();

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task GetOrder_defaults_to_BoardOrder()
    {
        var order = await _board.GetOrderAsync();
        Assert.Equal(WorkItemStateHelpers.BoardOrder, order);
    }

    [Fact]
    public async Task Reorder_persists_and_is_returned_on_get()
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

        var saved = await _board.ReorderAsync(new ReorderBoardColumnsRequest(
            desired.Select(s => s.ToDisplayString()).ToArray()));
        Assert.Equal(desired, saved);

        var loaded = await _board.GetOrderAsync();
        Assert.Equal(desired, loaded);
    }

    [Fact]
    public async Task Reorder_accepts_enum_names()
    {
        var desired = new[]
        {
            WorkItemState.Done,
            WorkItemState.Created,
            WorkItemState.Refined,
            WorkItemState.InDevelopment,
            WorkItemState.OnPullRequest,
            WorkItemState.Released
        };

        await _board.ReorderAsync(new ReorderBoardColumnsRequest(
            desired.Select(s => s.ToString()).ToArray()));

        Assert.Equal(desired, await _board.GetOrderAsync());
    }

    [Fact]
    public async Task Reorder_rejects_incomplete_list()
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _board.ReorderAsync(new ReorderBoardColumnsRequest(["Created", "done"])));
    }
}
