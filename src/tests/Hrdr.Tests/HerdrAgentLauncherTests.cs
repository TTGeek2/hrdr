using Hrdr.Core.Services;

namespace Hrdr.Tests;

public class HerdrAgentLauncherTests
{
    private static AgentLaunchSettings Settings(string message = "Implement the card", string paneName = "hrdr") =>
        new(message, "cursor", paneName);

    [Fact]
    public async Task Start_prompts_an_existing_named_pane_without_opening_another()
    {
        var herdr = new ScriptedHerdr();
        herdr.Enqueue(0, """{"result":{"workspaces":[{"workspace_id":"w9","focused":false},{"workspace_id":"w4","focused":true}]}}""");
        herdr.Enqueue(0, """{"result":{"panes":[{"pane_id":"w4:p1","cwd":"/repo","focused":true,"label":"editor"}]}}""");
        herdr.Enqueue(0, """{"result":{"panes":[{"pane_id":"w9:p2","label":"hrdr","agent":"cursor","name":"worker","cwd":"/other"}]}}""");
        herdr.Enqueue(0, """{"result":{"type":"agent_prompted"}}""");

        var launcher = new HerdrAgentLauncher(herdr);
        var started = await launcher.StartAsync(Settings());

        Assert.False(started.CreatedPane);
        Assert.Equal("worker", started.AgentName);
        Assert.Equal("w9:p2", started.PaneId);
        Assert.Equal(
            [
                "workspace list",
                "pane list --workspace w4",
                "pane list --workspace w9",
                "agent prompt worker Implement the card"
            ],
            herdr.Calls.Select(Join));
    }

    [Fact]
    public async Task Start_starts_an_agent_in_an_existing_shell_pane()
    {
        var herdr = new ScriptedHerdr();
        herdr.Enqueue(0, """{"result":{"workspaces":[{"workspace_id":"w4","focused":true}]}}""");
        herdr.Enqueue(0, """{"result":{"panes":[{"pane_id":"w4:p4","label":"hrdr","cwd":"/repo","focused":false}]}}""");
        herdr.Enqueue(0, """{"result":{"agents":[]}}""");
        herdr.Enqueue(0, """{"result":{"type":"agent_started"}}""");
        herdr.Enqueue(0, """{"result":{"type":"agent_prompted"}}""");

        var launcher = new HerdrAgentLauncher(herdr);
        var started = await launcher.StartAsync(Settings());

        Assert.False(started.CreatedPane);
        Assert.Equal("hrdr", started.AgentName);
        Assert.Equal(
            [
                "workspace list",
                "pane list --workspace w4",
                "agent list",
                "agent start hrdr --kind cursor --pane w4:p4 --timeout 60000",
                "agent prompt hrdr Implement the card"
            ],
            herdr.Calls.Select(Join));
    }

    [Fact]
    public async Task Start_creates_a_named_pane_when_none_matches()
    {
        var herdr = new ScriptedHerdr();
        herdr.Enqueue(0, """{"result":{"workspaces":[{"workspace_id":"w9","focused":false},{"workspace_id":"w4","focused":true}]}}""");
        herdr.Enqueue(0, """{"result":{"panes":[{"pane_id":"w4:p1","cwd":"/repo","focused":true}]}}""");
        herdr.Enqueue(0, """{"result":{"panes":[{"pane_id":"w9:p1","label":"other"}]}}""");
        herdr.Enqueue(0, """{"result":{"tab":{"tab_id":"w4:t8"},"root_pane":{"pane_id":"w4:p8"}}}""");
        herdr.Enqueue(0, """{"result":{"type":"ok"}}""");
        herdr.Enqueue(0, """{"result":{"agents":[{"name":"hrdr"}]}}""");
        herdr.Enqueue(0, """{"result":{"type":"agent_started"}}""");
        herdr.Enqueue(0, """{"result":{"type":"agent_prompted"}}""");

        var launcher = new HerdrAgentLauncher(herdr);
        var started = await launcher.StartAsync(Settings("Go", "HRDR Agent"));

        Assert.True(started.CreatedPane);
        Assert.Equal("hrdr-agent", started.AgentName);
        Assert.Equal("w4:p8", started.PaneId);
        Assert.Equal("HRDR Agent", started.PaneName);
        Assert.DoesNotContain(herdr.Calls, args => args is ["pane", "split", ..]);
        Assert.Equal(
            [
                "workspace list",
                "pane list --workspace w4",
                "pane list --workspace w9",
                "tab create --workspace w4 --label HRDR Agent --no-focus --cwd /repo",
                "pane rename w4:p8 HRDR Agent",
                "agent list",
                "agent start hrdr-agent --kind cursor --pane w4:p8 --timeout 60000",
                "agent prompt hrdr-agent Go"
            ],
            herdr.Calls.Select(Join));
    }

    [Fact]
    public async Task Start_closes_a_new_tab_when_the_agent_fails_to_launch()
    {
        var herdr = new ScriptedHerdr();
        herdr.Enqueue(0, """{"result":{"workspaces":[{"workspace_id":"w4","focused":true}]}}""");
        herdr.Enqueue(0, """{"result":{"panes":[{"pane_id":"w4:p1","cwd":"/repo","focused":true}]}}""");
        herdr.Enqueue(0, """{"result":{"tab":{"tab_id":"w4:t8"},"root_pane":{"pane_id":"w4:p8"}}}""");
        herdr.Enqueue(0, """{"result":{"type":"ok"}}""");
        herdr.Enqueue(0, """{"result":{"agents":[]}}""");
        herdr.Enqueue(1, "", """{"error":{"message":"agent was not ready"}}""");
        herdr.Enqueue(0, """{"result":{"type":"ok"}}""");

        var launcher = new HerdrAgentLauncher(herdr);
        var ex = await Assert.ThrowsAsync<HerdrLaunchException>(() => launcher.StartAsync(Settings()));

        Assert.Equal("agent was not ready", ex.Message);
        Assert.Equal("tab close w4:t8", Join(herdr.Calls[^1]));
    }

    [Fact]
    public async Task Start_leaves_an_existing_pane_open_when_launch_fails()
    {
        var herdr = new ScriptedHerdr();
        herdr.Enqueue(0, """{"result":{"workspaces":[{"workspace_id":"w4","focused":true}]}}""");
        herdr.Enqueue(0, """{"result":{"panes":[{"pane_id":"w4:p4","label":"hrdr","focused":false}]}}""");
        herdr.Enqueue(0, """{"result":{"agents":[]}}""");
        herdr.Enqueue(1, "", """{"error":{"message":"pane is busy"}}""");

        var launcher = new HerdrAgentLauncher(herdr);
        var ex = await Assert.ThrowsAsync<HerdrLaunchException>(() => launcher.StartAsync(Settings()));

        Assert.Equal("pane is busy", ex.Message);
        Assert.DoesNotContain(herdr.Calls, args => args is ["tab", "close", ..] or ["pane", "close", ..]);
    }

    [Fact]
    public async Task Start_leaves_the_pane_when_the_prompt_fails()
    {
        var herdr = new ScriptedHerdr();
        herdr.Enqueue(0, """{"result":{"workspaces":[{"workspace_id":"w4","focused":true}]}}""");
        herdr.Enqueue(0, """{"result":{"panes":[{"pane_id":"w4:p4","label":"hrdr","agent":"cursor","name":"hrdr"}]}}""");
        herdr.Enqueue(1, "", """{"error":{"message":"agent is blocked"}}""");

        var launcher = new HerdrAgentLauncher(herdr);
        var ex = await Assert.ThrowsAsync<HerdrLaunchException>(() => launcher.StartAsync(Settings()));

        Assert.Contains("agent is blocked", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(herdr.Calls, args => args is ["tab", "close", ..] or ["pane", "close", ..]);
    }

    [Fact]
    public async Task Start_does_not_call_herdr_without_a_message()
    {
        var herdr = new ScriptedHerdr();
        var launcher = new HerdrAgentLauncher(herdr);

        await Assert.ThrowsAsync<ArgumentException>(() => launcher.StartAsync(Settings("  ")));

        Assert.Empty(herdr.Calls);
    }

    private static string Join(IReadOnlyList<string> args) => string.Join(' ', args);

    private sealed class ScriptedHerdr : IHerdrCommandRunner
    {
        private readonly Queue<HerdrCommandResult> _results = new();
        public List<IReadOnlyList<string>> Calls { get; } = [];

        public void Enqueue(int exitCode, string stdout, string stderr = "") =>
            _results.Enqueue(new HerdrCommandResult(exitCode, stdout, stderr));

        public Task<HerdrCommandResult> RunAsync(
            IReadOnlyList<string> arguments,
            TimeSpan timeout,
            CancellationToken ct)
        {
            Calls.Add(arguments.ToArray());
            if (_results.Count == 0)
                throw new InvalidOperationException("No scripted herdr result for: " + string.Join(' ', arguments));
            return Task.FromResult(_results.Dequeue());
        }
    }
}
