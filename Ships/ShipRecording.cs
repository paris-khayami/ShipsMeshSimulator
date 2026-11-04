using System.Text.Json;

namespace ShipsMeshSimulator.Ships;

/// <summary>One saved ship report. OffsetSeconds is how long after the recording started it arrived.</summary>
public sealed record RecordedPosition(
    double OffsetSeconds,
    long Mmsi,
    string Name,
    double Latitude,
    double Longitude,
    double SpeedKnots,
    double CourseDegrees);

/// <summary>
/// The recording file. It has one JSON object per line, so it can be written a line at a time.
/// </summary>
public sealed class ShipRecording
{
    public ShipRecording(string filePath)
    {
        FilePath = filePath;
    }

    public string FilePath { get; }

    public bool Exists => File.Exists(FilePath);

    public IReadOnlyList<RecordedPosition> Load()
    {
        var positions = new List<RecordedPosition>();

        foreach (string line in File.ReadLines(FilePath))
        {
            RecordedPosition? position = JsonSerializer.Deserialize<RecordedPosition>(line);
            if (position is not null)
            {
                positions.Add(position);
            }
        }

        return positions;
    }
}
