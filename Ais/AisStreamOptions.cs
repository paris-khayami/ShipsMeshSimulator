namespace ShipsMeshSimulator.Ais;

/// <summary>Settings for the ship data feed. They come from appsettings.json.</summary>
public sealed class AisStreamOptions
{
    /// <summary>Secret. Set it with "dotnet user-secrets", never in appsettings.json.</summary>
    public string ApiKey { get; set; } = "";

    public string Url { get; set; } = "wss://stream.aisstream.io/v0/stream";

    // The two corners of the map area we want ships from.
    public double MinLatitude { get; set; }
    public double MinLongitude { get; set; }
    public double MaxLatitude { get; set; }
    public double MaxLongitude { get; set; }
}
