using Microsoft.AspNetCore.SignalR;

namespace Hrdr.Realtime;

/// <summary>Receive-only hub. The server pushes <c>boardChanged</c> after saved mutations.</summary>
public class HrdrHub : Hub;
