namespace ShipsMeshSimulator.Ships;

/// <summary>Settings for ship tracking. They come from appsettings.json.</summary>
public sealed class TrackingOptions
{
    /// <summary>Remove a ship after this long without a report.</summary>
    public double StaleAfterMinutes { get; set; } = 10;

    /// <summary>How often to send the current ships to the browser.</summary>
    public double BroadcastEverySeconds { get; set; } = 2;

    /// <summary>The most ships to show on the map at once.</summary>
    public int MaxShipsOnMap { get; set; } = 30;
}
