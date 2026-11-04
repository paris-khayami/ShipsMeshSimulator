namespace ShipsMeshSimulator.Mesh;

/// <summary>A message on its way to shore. Path lists every node it has passed through.</summary>
public sealed record DataMessage(Guid MessageId, long OriginId, IReadOnlyList<long> Path);

/// <summary>What a node currently believes about its route to shore.</summary>
public sealed record NodeRoute(long NodeId, long? NextHopId, int? HopsToShore);

// Events sent to the browser so it can show a message's progress.
public sealed record MessageHop(Guid MessageId, long FromId, long ToId);
public sealed record MessageDelivered(Guid MessageId, IReadOnlyList<long> Path);
public sealed record MessageDropped(Guid MessageId, long AtId, string Reason);

// Everything that can arrive in a node's inbox.
public abstract record NodeEvent;
public sealed record TickEvent : NodeEvent;
public sealed record RouteAnnouncement(
    long SenderId, int HopsToShore, long? SenderNextHopId, double SignalPercent) : NodeEvent;
public sealed record DataReceived(DataMessage Message) : NodeEvent;
