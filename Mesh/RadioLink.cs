namespace ShipsMeshSimulator.Mesh;

/// <summary>A simulated radio connection between two nodes, identified by their IDs.</summary>
public sealed record RadioLink(long NodeA, long NodeB, double DistanceKm, double SignalPercent);
