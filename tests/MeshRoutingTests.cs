using ShipsMeshSimulator.Mesh;
using Xunit;

namespace ShipsMeshSimulator.Tests;

/// <summary>
/// Runs real MeshNode objects on a made-up network, without any ship data or browser.
/// S is the shore station. The network is set up with links by hand in each test.
/// </summary>
public class MeshRoutingTests
{
    private const long S = ShoreStation.NodeId;
    private const long A = 1;
    private const long B = 2;
    private const long C = 3;
    private const long D = 4;

    [Fact]
    public async Task A_node_learns_a_route_through_its_neighbor()
    {
        using var mesh = new TestMesh(S, A, B);
        mesh.Link(S, A);
        mesh.Link(A, B);

        await mesh.RunUntilAsync(() => mesh.Hops(B) == 2);

        Assert.Equal(A, mesh.NextHop(B));
    }

    [Fact]
    public async Task A_node_chooses_the_route_with_fewer_hops()
    {
        using var mesh = new TestMesh(S, A, B, C);
        mesh.Link(S, A);
        mesh.Link(A, C);
        mesh.Link(S, B);
        mesh.Link(B, C);
        mesh.Link(C, D);
        mesh.Add(D);
        mesh.Link(A, D);

        await mesh.RunUntilAsync(() => mesh.Hops(D) == 2 && mesh.Hops(C) == 2);

        Assert.Equal(A, mesh.NextHop(D));
    }

    [Fact]
    public async Task A_tie_goes_to_the_stronger_signal()
    {
        using var mesh = new TestMesh(S, A, B, C);
        mesh.Link(S, A);
        mesh.Link(S, B);
        mesh.Link(A, C, signal: 20);
        mesh.Link(B, C, signal: 80);

        await mesh.RunUntilAsync(() => mesh.NextHop(C) == B);

        Assert.Equal(2, mesh.Hops(C));
    }

    [Fact]
    public async Task Routes_change_after_a_node_is_killed()
    {
        using var mesh = new TestMesh(S, A, B, C);
        mesh.Link(S, A);
        mesh.Link(S, B);
        mesh.Link(A, C, signal: 90);
        mesh.Link(B, C, signal: 10);
        await mesh.RunUntilAsync(() => mesh.NextHop(C) == A);

        mesh.Kill(A);

        await mesh.RunUntilAsync(() => mesh.NextHop(C) == B);
    }

    [Fact]
    public async Task A_node_becomes_disconnected_when_its_only_route_is_killed()
    {
        using var mesh = new TestMesh(S, A, B);
        mesh.Link(S, A);
        mesh.Link(A, B);
        await mesh.RunUntilAsync(() => mesh.Hops(B) == 2);

        mesh.Kill(A);

        await mesh.RunUntilAsync(() => mesh.Hops(B) is null);
    }

    [Fact]
    public async Task Disconnected_nodes_that_can_still_hear_each_other_lose_their_routes()
    {
        using var mesh = new TestMesh(S, A, B, C);
        mesh.Link(S, A);
        mesh.Link(A, B);
        mesh.Link(B, C);
        mesh.Link(C, A);
        await mesh.RunUntilAsync(() => mesh.Hops(C) == 2);

        mesh.Unlink(S, A);

        // The three ships are in a loop, so this proves they don't keep routing around it forever.
        await mesh.RunUntilAsync(() => mesh.Hops(A) is null && mesh.Hops(B) is null && mesh.Hops(C) is null);
    }

    [Fact]
    public async Task A_message_reaches_shore_one_hop_at_a_time()
    {
        using var mesh = new TestMesh(S, A, B);
        mesh.Link(S, A);
        mesh.Link(A, B);
        await mesh.RunUntilAsync(() => mesh.Hops(B) == 2);

        mesh.SendMessage(from: B);

        await mesh.RunUntilAsync(() => mesh.Delivered.Count == 1);
        Assert.Equal(new[] { B, A, S }, mesh.Delivered.Single().Path);
    }

    [Fact]
    public async Task A_message_from_a_disconnected_ship_is_dropped()
    {
        using var mesh = new TestMesh(S, A);

        mesh.SendMessage(from: A);

        await mesh.RunUntilAsync(() => mesh.Dropped.Count == 1);
        Assert.Empty(mesh.Delivered);
    }

    [Fact]
    public async Task A_message_tries_another_neighbor_if_the_first_is_out_of_range()
    {
        using var mesh = new TestMesh(S, A, B, C);
        mesh.Link(S, A);
        mesh.Link(S, B);
        mesh.Link(A, C, signal: 90);
        mesh.Link(B, C, signal: 10);
        await mesh.RunUntilAsync(() => mesh.NextHop(C) == A);
        await mesh.SettleAsync();

        // C still thinks A is its best route, because it hasn't noticed the link has gone.
        mesh.Unlink(A, C);
        mesh.SendMessage(from: C);

        await mesh.WaitForAsync(() => mesh.Delivered.Count == 1);
        Assert.Equal(new[] { C, B, S }, mesh.Delivered.Single().Path);
    }

    [Fact]
    public async Task A_sender_retries_when_a_delivery_fails_on_the_way()
    {
        using var mesh = new TestMesh(S, A, B, C);
        mesh.Link(S, A);
        mesh.Link(S, B);
        mesh.Link(A, C, signal: 90);
        mesh.Link(B, C, signal: 10);
        await mesh.RunUntilAsync(() => mesh.NextHop(C) == A);
        await mesh.SettleAsync();

        // C has already added itself to the path before the failed hop.
        var message = new DataMessage(Guid.NewGuid(), C, new[] { C });
        mesh.Node(C).Post(new DeliveryFailed(message, A));

        await mesh.WaitForAsync(() => mesh.Delivered.Count == 1);
        Assert.Equal(new[] { C, B, S }, mesh.Delivered.Single().Path);
    }

    [Fact]
    public async Task Routes_longer_than_the_limit_are_ignored()
    {
        // A straight line of 13 ships leading away from shore.
        long[] ships = Enumerable.Range(1, 13).Select(i => (long)i).ToArray();
        using var mesh = new TestMesh(ships.Prepend(S).ToArray());
        mesh.Link(S, ships[0]);
        for (int i = 1; i < ships.Length; i++)
        {
            mesh.Link(ships[i - 1], ships[i]);
        }

        await mesh.RunUntilAsync(() => mesh.Hops(ships[11]) == 12);

        // Give the 13th ship time to hear from the 12th, then check that it rejected the route.
        await mesh.SettleAsync();
        Assert.Null(mesh.Hops(ships[12]));
    }

    /// <summary>A small stand-in for MeshNetwork, with links set by hand and no delay.</summary>
    private sealed class TestMesh : IMeshRadio, IDisposable
    {
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

        private readonly Dictionary<long, MeshNode> _nodes = new();
        private readonly Dictionary<(long, long), double> _links = new();
        private readonly List<DataMessage> _delivered = new();
        private readonly List<DataMessage> _dropped = new();

        public TestMesh(params long[] ids)
        {
            foreach (long id in ids)
            {
                Add(id);
            }
        }

        public IReadOnlyList<DataMessage> Delivered
        {
            get { lock (_delivered) { return _delivered.ToList(); } }
        }

        public IReadOnlyList<DataMessage> Dropped
        {
            get { lock (_dropped) { return _dropped.ToList(); } }
        }

        public void Add(long id)
        {
            var node = new MeshNode(id, isShore: id == S, this);
            lock (_nodes)
            {
                _nodes[id] = node;
            }
            node.Start();
        }

        public MeshNode Node(long id)
        {
            lock (_nodes)
            {
                return _nodes[id];
            }
        }

        public int? Hops(long id) => Node(id).Route.HopsToShore;

        public long? NextHop(long id) => Node(id).Route.NextHopId;

        public void Link(long a, long b, double signal = 50)
        {
            lock (_links)
            {
                _links[(a, b)] = signal;
                _links[(b, a)] = signal;
            }
        }

        public void Unlink(long a, long b)
        {
            lock (_links)
            {
                _links.Remove((a, b));
                _links.Remove((b, a));
            }
        }

        public void Kill(long id)
        {
            MeshNode node;
            lock (_nodes)
            {
                node = _nodes[id];
                _nodes.Remove(id);
            }
            node.Stop();
        }

        public void SendMessage(long from)
        {
            var message = new DataMessage(Guid.NewGuid(), from, Array.Empty<long>());
            Node(from).Post(new DataReceived(message));
        }

        public void Tick()
        {
            foreach (MeshNode node in AllNodes())
            {
                node.Post(new TickEvent());
            }
        }

        /// <summary>Keeps the network's clock running until the condition is true.</summary>
        public async Task RunUntilAsync(Func<bool> condition)
        {
            DateTime giveUpAt = DateTime.UtcNow + Timeout;
            while (!condition())
            {
                Assert.True(DateTime.UtcNow < giveUpAt, "The network didn't reach the expected state in time.");
                Tick();
                await Task.Delay(20);
            }
        }

        /// <summary>Runs a few more ticks, so every node has heard from its neighbors.</summary>
        public async Task SettleAsync()
        {
            for (int i = 0; i < 5; i++)
            {
                Tick();
                await Task.Delay(20);
            }
        }

        /// <summary>Waits without ticking, so nodes learn nothing new while waiting.</summary>
        public async Task WaitForAsync(Func<bool> condition)
        {
            DateTime giveUpAt = DateTime.UtcNow + Timeout;
            while (!condition())
            {
                Assert.True(DateTime.UtcNow < giveUpAt, "The expected event didn't happen in time.");
                await Task.Delay(10);
            }
        }

        public void Announce(long senderId, int hopsToShore, long? nextHopId)
        {
            foreach (MeshNode node in AllNodes())
            {
                if (TryGetSignal(senderId, node.Id, out double signal))
                {
                    node.Post(new RouteAnnouncement(senderId, hopsToShore, nextHopId, signal));
                }
            }
        }

        public bool Transmit(long fromId, long toId, DataMessage message)
        {
            if (!TryGetSignal(fromId, toId, out _))
            {
                return false;
            }

            lock (_nodes)
            {
                return _nodes.TryGetValue(toId, out MeshNode? node) && node.Post(new DataReceived(message));
            }
        }

        public void ReportDelivered(DataMessage message)
        {
            lock (_delivered)
            {
                _delivered.Add(message);
            }
        }

        public void ReportDropped(DataMessage message, long atId, string reason)
        {
            lock (_dropped)
            {
                _dropped.Add(message);
            }
        }

        public void Dispose()
        {
            foreach (MeshNode node in AllNodes())
            {
                node.Stop();
            }
        }

        private List<MeshNode> AllNodes()
        {
            lock (_nodes)
            {
                return _nodes.Values.ToList();
            }
        }

        private bool TryGetSignal(long a, long b, out double signal)
        {
            lock (_links)
            {
                return _links.TryGetValue((a, b), out signal);
            }
        }
    }
}
