using Hrdr.Core.Services;
using Microsoft.AspNetCore.SignalR;

namespace Hrdr.Realtime;

public class SignalRBoardChangeNotifier(
    IHubContext<HrdrHub> hub,
    IHttpContextAccessor httpContextAccessor,
    ILogger<SignalRBoardChangeNotifier> logger) : IBoardChangeNotifier
{
    public const string ClientHeaderName = "X-Hrdr-Client";
    public const string EventName = "boardChanged";

    public async Task NotifyAsync(BoardChange change, CancellationToken cancellationToken = default)
    {
        var origin = httpContextAccessor.HttpContext?.Request.Headers[ClientHeaderName].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(origin))
        {
            var trimmed = origin.Trim();
            if (trimmed.Length <= 100)
                change = change with { OriginClientId = trimmed };
        }

        try
        {
            await hub.Clients.All.SendAsync(EventName, change, cancellationToken);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to publish {ChangeKind} to connected clients.", change.Kind);
        }
    }
}
