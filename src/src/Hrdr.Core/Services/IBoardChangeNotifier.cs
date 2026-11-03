using System.Text.Json.Serialization;

namespace Hrdr.Core.Services;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum BoardChangeKind
{
    WorkItemCreated,
    WorkItemUpdated,
    WorkItemDeleted,
    WorkItemsReordered,
    ProjectCreated,
    ProjectUpdated,
    ProjectDeleted,
    CommentCreated,
    CommentDeleted,
    BoardColumnsReordered
}

/// <summary>
/// A saved board mutation. Services leave <see cref="OriginClientId"/> empty;
/// the web notifier fills it when the caller sent a client id.
/// </summary>
public sealed record BoardChange(
    BoardChangeKind Kind,
    string? OriginClientId = null,
    int? WorkItemId = null,
    int? ProjectId = null,
    int? PreviousProjectId = null,
    int? CommentId = null,
    WorkItemDto? Item = null,
    ProjectDto? Project = null,
    CommentDto? Comment = null,
    IReadOnlyList<int>? OrderedIds = null,
    IReadOnlyList<string>? OrderedStates = null);

public interface IBoardChangeNotifier
{
    Task NotifyAsync(BoardChange change, CancellationToken cancellationToken = default);
}

public sealed class NullBoardChangeNotifier : IBoardChangeNotifier
{
    public static NullBoardChangeNotifier Instance { get; } = new();

    public Task NotifyAsync(BoardChange change, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}
