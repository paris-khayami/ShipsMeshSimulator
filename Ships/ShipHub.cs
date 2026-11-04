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
    private readonly ShipFeed _feed;

    public ShipHub(MeshNetwork network, ShipFeed feed)
    {
        _network = network;
        _feed = feed;
    }

    /// <summary>Called when someone presses "Send message to shore".</summary>
    public bool SendTestMessage(long mmsi) => _network.SendTestMessage(mmsi);

    public void KillNode(long mmsi) => _network.KillNode(mmsi);

    public void RestoreNode(long mmsi) => _network.RestoreNode(mmsi);

    public void RestoreAllNodes() => _network.RestoreAllNodes();

    public bool UseLiveData() => _feed.SetMode(ShipDataMode.Live);

    public bool UseRecordedData() => _feed.SetMode(ShipDataMode.Replay);
}
