namespace ShipsMeshSimulator.Ais;

// These classes match the shape of the JSON that aisstream.io sends:
//
// {
//   "MessageType": "PositionReport",
//   "MetaData": { "MMSI": 368207620, "ShipName": "EXAMPLE", ... },
//   "Message": { "PositionReport": { "Latitude": 53.5, "Longitude": 9.9, "Sog": 12.4, ... } }
// }
//
// JSON fields without a matching property here are simply ignored.

public sealed class AisEnvelope
{
    public string? MessageType { get; set; }
    public AisMetaData? MetaData { get; set; }
    public AisBody? Message { get; set; }
}

public sealed class AisMetaData
{
    public long MMSI { get; set; }
    public string? ShipName { get; set; }
}

public sealed class AisBody
{
    public PositionReport? PositionReport { get; set; }
}

public sealed class PositionReport
{
    public double Latitude { get; set; }
    public double Longitude { get; set; }

    /// <summary>Speed over ground, in knots.</summary>
    public double Sog { get; set; }

    /// <summary>Course over ground, in degrees.</summary>
    public double Cog { get; set; }

    /// <summary>False if the message may be wrong. Treated as true if it's missing.</summary>
    public bool Valid { get; set; } = true;
}
