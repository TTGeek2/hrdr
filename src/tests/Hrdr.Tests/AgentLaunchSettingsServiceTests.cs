using Hrdr.Core.Data;
using Hrdr.Core.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Hrdr.Tests;

public class AgentLaunchSettingsServiceTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection;
    private readonly HrdrDbContext _db;
    private readonly AgentLaunchSettingsService _settings;

    public AgentLaunchSettingsServiceTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<HrdrDbContext>()
            .UseSqlite(_connection)
            .Options;
        _db = new HrdrDbContext(options);
        _settings = new AgentLaunchSettingsService(_db);
    }

    public async Task InitializeAsync() => await _db.EnsureDatabaseAsync();

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task Get_returns_defaults_when_unset()
    {
        var settings = await _settings.GetAsync();
        Assert.Equal(AgentLaunchSettingsService.DefaultMessage, settings.Message);
        Assert.Equal(AgentLaunchSettingsService.DefaultKind, settings.Kind);
        Assert.Equal(AgentLaunchSettingsService.DefaultPaneName, settings.PaneName);
    }

    [Fact]
    public async Task Save_persists_message_kind_and_pane_name()
    {
        const string message = "Implement the refined card.\nKeep the public API stable.";
        await _settings.SaveAsync(new AgentLaunchSettings(message, "codex", "implement"));

        var loaded = await _settings.GetAsync();
        Assert.Equal(message, loaded.Message);
        Assert.Equal("codex", loaded.Kind);
        Assert.Equal("implement", loaded.PaneName);
    }

    [Fact]
    public async Task Save_rejects_an_unknown_kind()
    {
        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            _settings.SaveAsync(new AgentLaunchSettings("hello", "notepad", "hrdr")));
        Assert.Contains("Unknown agent", ex.Message);
    }

    [Fact]
    public async Task Save_rejects_an_empty_pane_name()
    {
        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            _settings.SaveAsync(new AgentLaunchSettings("hello", "cursor", "  ")));
        Assert.Contains("pane name", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Get_falls_back_when_stored_kind_is_unknown()
    {
        _db.AppSettings.Add(new Hrdr.Core.Entities.AppSetting
        {
            Key = AgentLaunchSettingsService.KindKey,
            Value = "notepad"
        });
        await _db.SaveChangesAsync();

        var settings = await _settings.GetAsync();
        Assert.Equal(AgentLaunchSettingsService.DefaultKind, settings.Kind);
    }
}
