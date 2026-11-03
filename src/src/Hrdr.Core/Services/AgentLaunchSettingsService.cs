using Hrdr.Core.Data;
using Hrdr.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace Hrdr.Core.Services;

public record AgentLaunchSettings(string Message, string Kind, string PaneName);

public class AgentLaunchSettingsService(HrdrDbContext db)
{
    public const string MessageKey = "HerdrAgentMessage";
    public const string KindKey = "HerdrAgentKind";
    public const string PaneNameKey = "HerdrAgentPaneName";
    public const string DefaultMessage = "You were started from Hrdr. Carry out the task in this repository.";
    public const string DefaultKind = "cursor";
    public const string DefaultPaneName = "hrdr";
    public const int MaxMessageLength = 8000;
    public const int MaxPaneNameLength = 64;

    public static readonly IReadOnlyList<string> Kinds =
    [
        "cursor",
        "amp",
        "agy",
        "claude",
        "cline",
        "codex",
        "copilot",
        "devin",
        "droid",
        "gemini",
        "grok",
        "hermes",
        "kilo",
        "kimi",
        "kiro",
        "maki",
        "mastracode",
        "muse",
        "omp",
        "opencode",
        "pi",
        "qodercli",
        "qwen"
    ];

    public async Task<AgentLaunchSettings> GetAsync(CancellationToken ct = default)
    {
        var message = await db.AppSettings.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Key == MessageKey, ct);
        var kind = await db.AppSettings.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Key == KindKey, ct);
        var paneName = await db.AppSettings.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Key == PaneNameKey, ct);

        var kindValue = kind?.Value ?? DefaultKind;
        if (!IsKnownKind(kindValue))
            kindValue = DefaultKind;

        var paneNameValue = paneName?.Value?.Trim();
        if (string.IsNullOrEmpty(paneNameValue))
            paneNameValue = DefaultPaneName;

        return new AgentLaunchSettings(
            message is null ? DefaultMessage : message.Value,
            kindValue,
            paneNameValue);
    }

    public async Task SaveAsync(AgentLaunchSettings settings, CancellationToken ct = default)
    {
        var message = settings.Message?.Trim() ?? "";
        if (message.Length > MaxMessageLength)
            throw new ArgumentException($"Agent message must be at most {MaxMessageLength} characters.", nameof(settings));

        var kind = settings.Kind?.Trim() ?? "";
        if (!IsKnownKind(kind))
            throw new ArgumentException($"Unknown agent '{settings.Kind}'.", nameof(settings));

        var paneName = NormalizePaneName(settings.PaneName);

        await UpsertAsync(MessageKey, message, ct);
        await UpsertAsync(KindKey, kind, ct);
        await UpsertAsync(PaneNameKey, paneName, ct);
        await db.SaveChangesAsync(ct);
    }

    public static string NormalizePaneName(string? paneName)
    {
        var name = paneName?.Trim() ?? "";
        if (name.Length == 0)
            throw new ArgumentException("Set a pane name before starting an agent.", nameof(paneName));
        if (name.Length > MaxPaneNameLength)
            throw new ArgumentException($"Pane name must be at most {MaxPaneNameLength} characters.", nameof(paneName));
        if (name.Any(char.IsControl))
            throw new ArgumentException("Pane name cannot contain control characters.", nameof(paneName));
        return name;
    }

    public static bool IsKnownKind(string? kind) =>
        kind is not null && Kinds.Contains(kind, StringComparer.Ordinal);

    private async Task UpsertAsync(string key, string value, CancellationToken ct)
    {
        var setting = await db.AppSettings.FirstOrDefaultAsync(s => s.Key == key, ct);
        if (setting is null)
            db.AppSettings.Add(new AppSetting { Key = key, Value = value });
        else
            setting.Value = value;
    }
}
