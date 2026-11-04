namespace ShipsMeshSimulator.Mesh;

/// <summary>
/// The gateway that connects the ships' network to the outside world.
/// Real ships use MMSI numbers as IDs, so the shore station uses 0, which no ship should have.
/// </summary>
public sealed record ShoreStation(long Id, string Name, double Latitude, double Longitude)
{
    public const long NodeId = 0;
}
