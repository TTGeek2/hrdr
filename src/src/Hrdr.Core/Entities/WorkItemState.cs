using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Hrdr.Core.Entities;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum WorkItemState
{
    Created = 0,
    Refined = 1,

    [Display(Name = "In development")]
    [JsonStringEnumMemberName("In development")]
    InDevelopment = 2,

    [Display(Name = "on pullrequest")]
    [JsonStringEnumMemberName("on pullrequest")]
    OnPullRequest = 3,

    [Display(Name = "done")]
    [JsonStringEnumMemberName("done")]
    Done = 4,

    [Display(Name = "released")]
    [JsonStringEnumMemberName("released")]
    Released = 5
}

public static class WorkItemStateHelpers
{
    /// <summary>Column order for the items overview kanban board.</summary>
    public static IReadOnlyList<WorkItemState> BoardOrder { get; } =
    [
        WorkItemState.Created,
        WorkItemState.Refined,
        WorkItemState.InDevelopment,
        WorkItemState.OnPullRequest,
        WorkItemState.Done,
        WorkItemState.Released
    ];

    public static string ToDisplayString(this WorkItemState state) => state switch
    {
        WorkItemState.Created => "Created",
        WorkItemState.Refined => "Refined",
        WorkItemState.InDevelopment => "In development",
        WorkItemState.OnPullRequest => "on pullrequest",
        WorkItemState.Done => "done",
        WorkItemState.Released => "released",
        _ => state.ToString()
    };

    public static bool TryParse(string? value, out WorkItemState state)
    {
        state = default;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var trimmed = value.Trim();
        foreach (var candidate in BoardOrder)
        {
            if (string.Equals(candidate.ToDisplayString(), trimmed, StringComparison.Ordinal)
                || string.Equals(candidate.ToString(), trimmed, StringComparison.OrdinalIgnoreCase))
            {
                state = candidate;
                return true;
            }
        }

        return Enum.TryParse(trimmed, ignoreCase: true, out state);
    }
}
