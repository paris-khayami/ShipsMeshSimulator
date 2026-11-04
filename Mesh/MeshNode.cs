using System.Threading.Channels;

namespace ShipsMeshSimulator.Mesh;

/// <summary>
/// One independent node: a ship or the shore station. It has its own inbox and knows only
/// what its neighbors tell it, so no central program decides the route.
///
/// How a node finds a route to shore:
/// 1. The shore station announces "I'm 0 hops from shore" every tick.
/// 2. A node that hears this knows it's 1 hop away, and announces that to its neighbors.
/// 3. Each node forwards messages to the neighbor that's closest to shore.
/// 4. If a neighbor goes quiet, the node forgets it and uses another neighbor.
/// </summary>
public sealed class MeshNode
{
    // Routes longer than this are ignored, which also stops routes growing forever in a loop.
    private const int MaxHops = 12;

    // Forget a neighbor after this many ticks of silence.
    private const int NeighborTimeoutTicks = 3;

    private readonly IMeshRadio _radio;
    private readonly bool _isShore;
    private readonly Channel<NodeEvent> _inbox = Channel.CreateUnbounded<NodeEvent>();

    // Only this node's own loop touches these, one event at a time, so they need no locks.
    private readonly Dictionary<long, NeighborInfo> _neighbors = new();
    private long _currentTick;

    // The browser update reads this from another thread. Volatile makes the latest value visible.
    private volatile NodeRoute _route;

    public MeshNode(long id, bool isShore, IMeshRadio radio)
    {
        Id = id;
        _isShore = isShore;
        _radio = radio;
        _route = isShore ? new NodeRoute(id, null, 0) : new NodeRoute(id, null, null);
    }

    public long Id { get; }

    public NodeRoute Route => _route;

    /// <summary>Starts reading the inbox on a background thread.</summary>
    public void Start() => _ = Task.Run(() => RunAsync());

    /// <summary>Stops the node. Anything already in the inbox is still handled.</summary>
    public void Stop() => _inbox.Writer.TryComplete();

    /// <summary>Puts an event in the inbox. Returns false if the node has stopped.</summary>
    public bool Post(NodeEvent nodeEvent) => _inbox.Writer.TryWrite(nodeEvent);

    private async Task RunAsync()
    {
        await foreach (NodeEvent nodeEvent in _inbox.Reader.ReadAllAsync())
        {
            switch (nodeEvent)
            {
                case TickEvent:
                    HandleTick();
                    break;
                case RouteAnnouncement announcement:
                    HandleAnnouncement(announcement);
                    break;
                case DataReceived received:
                    HandleMessage(received.Message);
                    break;
            }
        }
    }

    private void HandleTick()
    {
        _currentTick++;
        RemoveSilentNeighbors();
        ChooseRoute();

        // A node with no route stays quiet, so its neighbors stop relying on it.
        if (_route.HopsToShore is int hops)
        {
            _radio.Announce(Id, hops, _route.NextHopId);
        }
    }

    private void HandleAnnouncement(RouteAnnouncement announcement)
    {
        _neighbors[announcement.SenderId] = new NeighborInfo(
            announcement.HopsToShore,
            announcement.SenderNextHopId,
            announcement.SignalPercent,
            _currentTick);

        ChooseRoute();
    }

    private void HandleMessage(DataMessage message)
    {
        var path = new List<long>(message.Path) { Id };
        DataMessage updated = message with { Path = path };

        if (_isShore)
        {
            _radio.ReportDelivered(updated);
            return;
        }

        if (path.Count > MaxHops)
        {
            _radio.ReportDropped(updated, Id, "It passed through too many ships.");
            return;
        }

        // Try the best neighbor. If it's out of range now, forget it and try the next best.
        ChooseRoute();
        while (_route.NextHopId is long nextHop)
        {
            if (_radio.Transmit(Id, nextHop, updated))
            {
                return;
            }

            _neighbors.Remove(nextHop);
            ChooseRoute();
        }

        _radio.ReportDropped(updated, Id, "This ship has no route to shore.");
    }

    private void RemoveSilentNeighbors()
    {
        List<long> silent = _neighbors
            .Where(entry => _currentTick - entry.Value.HeardAtTick > NeighborTimeoutTicks)
            .Select(entry => entry.Key)
            .ToList();

        foreach (long neighborId in silent)
        {
            _neighbors.Remove(neighborId);
        }
    }

    private void ChooseRoute()
    {
        if (_isShore)
        {
            return;
        }

        // Fewest hops wins. If two neighbors tie, use the one with the stronger signal.
        KeyValuePair<long, NeighborInfo>? best = _neighbors
            // Ignore a neighbor that's routing through this node, or the two would form a loop.
            .Where(entry => entry.Value.NextHopId != Id && entry.Value.HopsToShore < MaxHops)
            .OrderBy(entry => entry.Value.HopsToShore)
            .ThenByDescending(entry => entry.Value.SignalPercent)
            .ThenBy(entry => entry.Key)
            .Cast<KeyValuePair<long, NeighborInfo>?>()
            .FirstOrDefault();

        _route = best is { } chosen
            ? new NodeRoute(Id, chosen.Key, chosen.Value.HopsToShore + 1)
            : new NodeRoute(Id, null, null);
    }

    private sealed record NeighborInfo(
        int HopsToShore,
        long? NextHopId,
        double SignalPercent,
        long HeardAtTick);
}
