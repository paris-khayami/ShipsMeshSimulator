namespace ShipsMeshSimulator.Mesh;

/// <summary>Settings for the simulated network. They come from appsettings.json.</summary>
public sealed class MeshOptions
{
    /// <summary>Two nodes can connect if they're no farther apart than this.</summary>
    public double RadioRangeKm { get; set; } = 10;

    /// <summary>How long a message takes to cross one link, so it can be seen on the map.</summary>
    public int HopDelayMilliseconds { get; set; } = 800;

    public string ShoreName { get; set; } = "Shore station";
    public double ShoreLatitude { get; set; }
    public double ShoreLongitude { get; set; }
}
