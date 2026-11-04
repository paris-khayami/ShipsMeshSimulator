using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
using ShipsMeshSimulator.Ships;

namespace ShipsMeshSimulator.Mesh;

/// <summary>
/// Plays the part of the air between the radios: it starts and stops nodes as ships come and go,
/// and only delivers packets between nodes that are in range. It makes no routing decisions.
/// </summary>
public sealed class MeshNetwork : BackgroundService, IMeshRadio
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(1);

    private readonly ConcurrentDictionary<long, MeshNode> _nodes = new();

    // Ships the person has killed. They stay on the map, but their radios are off.
    private readonly ConcurrentDictionary<long, bool> _killed = new();

    private readonly IHubContext<ShipHub> _hub;
    private readonly ILogger<MeshNetwork> _logger;
    private readonly TimeSpan _hopDelay;

    // For each node, its neighbors and the signal to each. Sync replaces the whole table at once,
    // so other threads see either the old table or the new one, never a half-built one.
    private volatile Dictionary<long, Dictionary<long, double>> _neighbors = new();

    public MeshNetwork(
        IHubContext<ShipHub> hub,
        IOptions<MeshOptions> options,
        ILogger<MeshNetwork> logger)
    {
        _hub = hub;
        _logger = logger;
        _hopDelay = TimeSpan.FromMilliseconds(options.Value.HopDelayMilliseconds);
    }

    /// <summary>Updates the network to match the ships and links currently on the map.</summary>
    public void Sync(IReadOnlyList<Ship> ships, ShoreStation shore, IReadOnlyList<RadioLink> links)
    {
        var neighbors = new Dictionary<long, Dictionary<long, double>>();
        foreach (RadioLink link in links)
        {
            AddNeighbor(neighbors, link.NodeA, link.NodeB, link.SignalPercent);
            AddNeighbor(neighbors, link.NodeB, link.NodeA, link.SignalPercent);
        }
        _neighbors = neighbors;

        AddNodeIfNew(shore.Id, isShore: true);

        var currentIds = new HashSet<long> { shore.Id };
        foreach (Ship ship in ships)
        {
            currentIds.Add(ship.Mmsi);
            AddNodeIfNew(ship.Mmsi, isShore: false);
        }

        foreach (long id in _nodes.Keys)
        {
            if (!currentIds.Contains(id) && _nodes.TryRemove(id, out MeshNode? node))
            {
                node.Stop();
            }
        }
    }

    public bool IsKilled(long id) => _killed.ContainsKey(id);

    public IReadOnlyList<long> GetKilledIds() => _killed.Keys.ToList();

    /// <summary>Turns off a ship's radio. Nobody tells its neighbors: they have to notice.</summary>
    public void KillNode(long id)
    {
        if (id == ShoreStation.NodeId)
        {
            return;
        }

        _killed[id] = true;
        if (_nodes.TryRemove(id, out MeshNode? node))
        {
            node.Stop();
        }
    }

    /// <summary>Turns a ship's radio back on. It rejoins the network at the next update.</summary>
    public void RestoreNode(long id) => _killed.TryRemove(id, out _);

    public void RestoreAllNodes() => _killed.Clear();

    public IReadOnlyList<NodeRoute> GetRoutes() => _nodes.Values.Select(node => node.Route).ToList();

    /// <summary>Starts a message from a ship. Returns false if that ship isn't in the network.</summary>
    public bool SendTestMessage(long originId)
    {
        if (!_nodes.TryGetValue(originId, out MeshNode? node))
        {
            return false;
        }

        var message = new DataMessage(Guid.NewGuid(), originId, Array.Empty<long>());
        return node.Post(new DataReceived(message));
    }

    public void Announce(long senderId, int hopsToShore, long? nextHopId)
    {
        if (!_neighbors.TryGetValue(senderId, out Dictionary<long, double>? inRange))
        {
            return;
        }

        foreach ((long neighborId, double signal) in inRange)
        {
            if (_nodes.TryGetValue(neighborId, out MeshNode? neighbor))
            {
                neighbor.Post(new RouteAnnouncement(senderId, hopsToShore, nextHopId, signal));
            }
        }
    }

    public bool Transmit(long fromId, long toId, DataMessage message)
    {
        bool inRange = _nodes.ContainsKey(toId)
            && _neighbors.TryGetValue(fromId, out Dictionary<long, double>? neighbors)
            && neighbors.ContainsKey(toId);

        if (!inRange)
        {
            return false;
        }

        _ = PublishAsync("MessageHop", new MessageHop(message.MessageId, fromId, toId));
        _ = DeliverAfterDelayAsync(fromId, toId, message);
        return true;
    }

    public void ReportDelivered(DataMessage message)
    {
        _ = PublishAsync("MessageDelivered", new MessageDelivered(message.MessageId, message.Path));
    }

    public void ReportDropped(DataMessage message, long atId, string reason)
    {
        _ = PublishAsync("MessageDropped", new MessageDropped(message.MessageId, atId, reason));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TickInterval);

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                foreach (MeshNode node in _nodes.Values)
                {
                    node.Post(new TickEvent());
                }
            }
        }
        catch (OperationCanceledException)
        {
            // The app is shutting down, which is not an error.
        }

        foreach (MeshNode node in _nodes.Values)
        {
            node.Stop();
        }
    }

    private static void AddNeighbor(
        Dictionary<long, Dictionary<long, double>> table, long nodeId, long neighborId, double signal)
    {
        if (!table.TryGetValue(nodeId, out Dictionary<long, double>? neighbors))
        {
            neighbors = new Dictionary<long, double>();
            table[nodeId] = neighbors;
        }

        neighbors[neighborId] = signal;
    }

    private void AddNodeIfNew(long id, bool isShore)
    {
        if (_nodes.ContainsKey(id) || _killed.ContainsKey(id))
        {
            return;
        }

        var node = new MeshNode(id, isShore, this);
        if (_nodes.TryAdd(id, node))
        {
            node.Start();
        }
    }

    /// <summary>Waits before delivering, so each hop takes time and can be seen on the map.</summary>
    private async Task DeliverAfterDelayAsync(long fromId, long toId, DataMessage message)
    {
        await Task.Delay(_hopDelay);

        bool delivered = _nodes.TryGetValue(toId, out MeshNode? node)
            && node.Post(new DataReceived(message));

        if (delivered)
        {
            return;
        }

        // The next ship disappeared while the message was on its way, so the sender tries again.
        bool senderTold = _nodes.TryGetValue(fromId, out MeshNode? sender)
            && sender.Post(new DeliveryFailed(message, toId));

        if (!senderTold)
        {
            ReportDropped(message, fromId, "The ship carrying the message left the network.");
        }
    }

    private async Task PublishAsync(string eventName, object payload)
    {
        try
        {
            await _hub.Clients.All.SendAsync(eventName, payload);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Couldn't send {EventName} to the browser: {Reason}", eventName, ex.Message);
        }
    }
}
