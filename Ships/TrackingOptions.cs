namespace ShipsMeshSimulator.Ships;

/// <summary>Settings for ship tracking. They come from appsettings.json.</summary>
public sealed class TrackingOptions
{
    /// <summary>Remove a ship after this long without a report.</summary>
    public double StaleAfterMinutes { get; set; } = 10;

    /// <summary>How often to clean up the list and print a summary.</summary>
    public double ReportEverySeconds { get; set; } = 10;
}
