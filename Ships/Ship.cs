namespace ShipsMeshSimulator.Ships;

/// <summary>
/// What we know about one ship at one moment. A record can't be changed after it's created,
/// so a new position means a new Ship object. That makes it safe to share between threads.
/// </summary>
public sealed record Ship(
    long Mmsi,
    string Name,
    double Latitude,
    double Longitude,
    double SpeedKnots,
    double CourseDegrees,
    DateTimeOffset LastSeenUtc);
