namespace ShipsMeshSimulator.Mesh;

/// <summary>
/// The only way a node can reach the outside world. Nodes can't see the whole network,
/// which is what makes them independent, as they would be with real radios.
/// </summary>
public interface IMeshRadio
{
    /// <summary>Tells every node in range how far this node is from shore.</summary>
    void Announce(long senderId, int hopsToShore, long? nextHopId);

    /// <summary>Sends a message to one neighbor. Returns false if it's out of range.</summary>
    bool Transmit(long fromId, long toId, DataMessage message);

    void ReportDelivered(DataMessage message);
    void ReportDropped(DataMessage message, long atId, string reason);
}
