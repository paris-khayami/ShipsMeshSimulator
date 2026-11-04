namespace ShipsMeshSimulator.Ships;

/// <summary>The message sent to every open browser page.</summary>
public sealed record ShipsUpdate(int TotalTracked, IReadOnlyList<Ship> Ships);
