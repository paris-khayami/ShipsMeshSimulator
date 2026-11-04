using Microsoft.AspNetCore.SignalR;
using ShipsMeshSimulator.Mesh;

namespace ShipsMeshSimulator.Ships;

/// <summary>
/// The live connection to the browser. ShipBroadcaster and MeshNetwork send updates through it,
/// and the browser calls the methods below.
/// </summary>
public sealed class ShipHub : Hub
{
    private readonly MeshNetwork _network;

    public ShipHub(MeshNetwork network)
    {
        _network = network;
    }

    /// <summary>Called when someone presses "Send message to shore".</summary>
    public bool SendTestMessage(long mmsi) => _network.SendTestMessage(mmsi);
}
