using Microsoft.AspNetCore.SignalR;

namespace ShipsMeshSimulator.Ships;

/// <summary>
/// The address browsers connect to for live updates. It's empty because, for now, the server
/// only sends data. ShipBroadcaster does the sending.
/// </summary>
public sealed class ShipHub : Hub
{
}
