namespace ShipsMeshSimulator.Ships;

/// <summary>Settings for recording and replaying ship data. They come from appsettings.json.</summary>
public sealed class RecordingOptions
{
    /// <summary>Save the live feed to a file while the app runs.</summary>
    public bool Enabled { get; set; }

    public string FilePath { get; set; } = "recordings/ships.jsonl";

    public double Minutes { get; set; } = 15;

    /// <summary>Switch to the recording if the live feed is silent for this long.</summary>
    public double SilenceBeforeReplaySeconds { get; set; } = 60;
}
