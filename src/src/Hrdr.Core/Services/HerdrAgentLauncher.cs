using System.Text.Json;

namespace Hrdr.Core.Services;

public record HerdrAgentStartResult(
    string AgentName,
    string Kind,
    string PaneId,
    string WorkspaceId,
    string PaneName,
    bool CreatedPane);

public class HerdrLaunchException(string message) : Exception(message);

public class HerdrAgentLauncher(IHerdrCommandRunner herdr)
{
    private static readonly TimeSpan QuickTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(70);
    private const string StartTimeoutMs = "60000";

    public async Task<HerdrAgentStartResult> StartAsync(
        AgentLaunchSettings settings,
        CancellationToken ct = default)
    {
        var message = settings.Message?.Trim() ?? "";
        if (message.Length == 0)
            throw new ArgumentException("Set an agent message before starting an agent.", nameof(settings));

        var kind = settings.Kind?.Trim() ?? "";
        if (!AgentLaunchSettingsService.IsKnownKind(kind))
            throw new ArgumentException($"Unknown agent '{settings.Kind}'.", nameof(settings));

        var paneName = AgentLaunchSettingsService.NormalizePaneName(settings.PaneName);
        var session = await ReadSessionAsync(ct);
        var existing = session.Panes.FirstOrDefault(pane =>
            string.Equals(pane.Label, paneName, StringComparison.Ordinal));

        if (existing is not null)
        {
            var agentName = await EnsureAgentAsync(existing, kind, ct);
            await PromptAsync(existing.Label ?? paneName, agentName, message, ct);
            return new HerdrAgentStartResult(
                agentName, kind, existing.PaneId, existing.WorkspaceId, paneName, CreatedPane: false);
        }

        var created = await CreateNamedPaneAsync(session.FocusedWorkspaceId, session.FocusedCwd, paneName, ct);
        string createdAgent;
        try
        {
            createdAgent = await EnsureAgentAsync(created, kind, ct);
        }
        catch (HerdrLaunchException)
        {
            if (!string.IsNullOrWhiteSpace(created.TabId))
                await TryCloseTabAsync(created.TabId, ct);
            throw;
        }

        await PromptAsync(paneName, createdAgent, message, ct);
        return new HerdrAgentStartResult(
            createdAgent, kind, created.PaneId, created.WorkspaceId, paneName, CreatedPane: true);
    }

    private async Task<string> EnsureAgentAsync(PaneRef pane, string kind, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(pane.AgentKind))
            return pane.AgentName ?? pane.PaneId;

        var agentName = await NextAgentNameAsync(pane.Label ?? "hrdr", ct);
        await RunOkAsync(
            ["agent", "start", agentName, "--kind", kind, "--pane", pane.PaneId, "--timeout", StartTimeoutMs],
            StartTimeout,
            ct);
        return agentName;
    }

    private async Task PromptAsync(string paneName, string target, string message, CancellationToken ct)
    {
        try
        {
            await RunOkAsync(["agent", "prompt", target, message], QuickTimeout, ct);
        }
        catch (HerdrLaunchException ex)
        {
            throw new HerdrLaunchException(
                $"The message was not delivered to pane {paneName}: {ex.Message}");
        }
    }

    private async Task<SessionSnapshot> ReadSessionAsync(CancellationToken ct)
    {
        var listed = await RunOkAsync(["workspace", "list"], QuickTimeout, ct);
        var workspaces = new List<WorkspaceRef>();
        foreach (var workspace in RequireProperty(ResultOf(listed.Stdout), "workspaces").EnumerateArray())
        {
            var id = StringOrNull(workspace, "workspace_id");
            if (string.IsNullOrWhiteSpace(id))
                continue;
            var focused = workspace.TryGetProperty("focused", out var isFocused)
                && isFocused.ValueKind == JsonValueKind.True;
            workspaces.Add(new WorkspaceRef(id, focused));
        }

        if (workspaces.Count == 0)
            throw new HerdrLaunchException("The default Herdr session has no workspace.");

        var focusedWorkspaceId = workspaces.FirstOrDefault(workspace => workspace.Focused)?.Id ?? workspaces[0].Id;
        var panes = new List<PaneRef>();
        string? focusedCwd = null;
        foreach (var workspace in workspaces.OrderByDescending(workspace => workspace.Focused))
        {
            var paneList = await RunOkAsync(["pane", "list", "--workspace", workspace.Id], QuickTimeout, ct);
            foreach (var pane in RequireProperty(ResultOf(paneList.Stdout), "panes").EnumerateArray())
            {
                var paneId = StringOrNull(pane, "pane_id");
                if (string.IsNullOrWhiteSpace(paneId))
                    continue;
                var cwd = StringOrNull(pane, "cwd");
                var isFocused = pane.TryGetProperty("focused", out var focusedValue)
                    && focusedValue.ValueKind == JsonValueKind.True;
                if (workspace.Id == focusedWorkspaceId && isFocused)
                    focusedCwd = cwd;
                panes.Add(new PaneRef(
                    paneId,
                    StringOrNull(pane, "tab_id"),
                    workspace.Id,
                    cwd,
                    StringOrNull(pane, "agent"),
                    StringOrNull(pane, "name"),
                    StringOrNull(pane, "label")));
            }
        }

        return new SessionSnapshot(focusedWorkspaceId, focusedCwd, panes);
    }

    private async Task<PaneRef> CreateNamedPaneAsync(
        string workspaceId,
        string? cwd,
        string paneName,
        CancellationToken ct)
    {
        var args = new List<string>
        {
            "tab", "create", "--workspace", workspaceId, "--label", paneName, "--no-focus"
        };
        if (!string.IsNullOrWhiteSpace(cwd))
        {
            args.Add("--cwd");
            args.Add(cwd);
        }

        var created = await RunOkAsync(args, QuickTimeout, ct);
        var result = ResultOf(created.Stdout);
        var tabId = StringOrNull(RequireProperty(result, "tab"), "tab_id");
        var paneId = StringOrNull(RequireProperty(result, "root_pane"), "pane_id");
        if (string.IsNullOrWhiteSpace(tabId) || string.IsNullOrWhiteSpace(paneId))
        {
            if (!string.IsNullOrWhiteSpace(tabId))
                await TryCloseTabAsync(tabId, ct);
            throw new HerdrLaunchException("herdr did not return the new pane.");
        }

        try
        {
            await RunOkAsync(["pane", "rename", paneId, paneName], QuickTimeout, ct);
        }
        catch (HerdrLaunchException)
        {
            await TryCloseTabAsync(tabId, ct);
            throw;
        }

        return new PaneRef(paneId, tabId, workspaceId, cwd, AgentKind: null, AgentName: null, Label: paneName);
    }

    private async Task<string> NextAgentNameAsync(string paneName, CancellationToken ct)
    {
        var result = await RunOkAsync(["agent", "list"], QuickTimeout, ct);
        var taken = new HashSet<string>(StringComparer.Ordinal);
        foreach (var agent in RequireProperty(ResultOf(result.Stdout), "agents").EnumerateArray())
        {
            var name = StringOrNull(agent, "name");
            if (!string.IsNullOrWhiteSpace(name))
                taken.Add(name);
        }

        var baseName = AgentNameBase(paneName);
        for (var i = 1; i < 100; i++)
        {
            var candidate = i == 1 ? baseName : $"{baseName}-{i}";
            if (candidate.Length > 32)
                candidate = candidate[..32].TrimEnd('-');
            if (!taken.Contains(candidate))
                return candidate;
        }

        throw new HerdrLaunchException("Too many Hrdr agents are already running.");
    }

    private static string AgentNameBase(string paneName)
    {
        var chars = new List<char>(paneName.Length);
        foreach (var c in paneName.Trim().ToLowerInvariant())
        {
            if (c is >= 'a' and <= 'z' or >= '0' and <= '9' or '_' or '-')
                chars.Add(c);
            else if (char.IsWhiteSpace(c) || c is '.' or '/')
                chars.Add('-');
        }

        var baseName = new string(chars.ToArray()).Trim('-');
        while (baseName.Contains("--", StringComparison.Ordinal))
            baseName = baseName.Replace("--", "-", StringComparison.Ordinal);
        if (baseName.Length == 0 || !char.IsAsciiLetter(baseName[0]))
            baseName = "a" + baseName;
        if (baseName.Length > 28)
            baseName = baseName[..28].TrimEnd('-');
        return baseName;
    }

    private async Task<HerdrCommandResult> RunOkAsync(
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        CancellationToken ct)
    {
        var result = await herdr.RunAsync(arguments, timeout, ct);
        if (result.ExitCode != 0)
            throw new HerdrLaunchException(DescribeFailure(result));
        return result;
    }

    private async Task TryCloseTabAsync(string tabId, CancellationToken ct)
    {
        try
        {
            await herdr.RunAsync(["tab", "close", tabId], QuickTimeout, ct);
        }
        catch (HerdrLaunchException)
        {
        }
    }

    private static JsonElement RequireProperty(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value))
            throw new HerdrLaunchException("herdr returned an unexpected response.");
        return value;
    }

    private static string? StringOrNull(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
            return null;
        return value.GetString();
    }

    private static JsonElement ResultOf(string stdout)
    {
        try
        {
            using var doc = JsonDocument.Parse(stdout);
            if (!doc.RootElement.TryGetProperty("result", out var result))
                throw new HerdrLaunchException("herdr returned an unexpected response.");
            return result.Clone();
        }
        catch (JsonException)
        {
            throw new HerdrLaunchException("herdr returned an unexpected response.");
        }
    }

    private static string DescribeFailure(HerdrCommandResult result)
    {
        var raw = string.IsNullOrWhiteSpace(result.Stderr) ? result.Stdout : result.Stderr;
        if (string.IsNullOrWhiteSpace(raw))
            return $"herdr exited with status {result.ExitCode}.";

        try
        {
            using var doc = JsonDocument.Parse(raw);
            if (doc.RootElement.TryGetProperty("error", out var error)
                && error.TryGetProperty("message", out var message)
                && message.ValueKind == JsonValueKind.String
                && message.GetString() is { Length: > 0 } text)
                return text;
        }
        catch (JsonException)
        {
        }

        var trimmed = raw.Trim();
        return trimmed.Length > 400 ? trimmed[..400] : trimmed;
    }

    private sealed record WorkspaceRef(string Id, bool Focused);

    private sealed record PaneRef(
        string PaneId,
        string? TabId,
        string WorkspaceId,
        string? Cwd,
        string? AgentKind,
        string? AgentName,
        string? Label);

    private sealed record SessionSnapshot(string FocusedWorkspaceId, string? FocusedCwd, IReadOnlyList<PaneRef> Panes);
}
