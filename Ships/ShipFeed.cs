namespace ShipsMeshSimulator.Ships;

public enum ShipDataMode
{
    Live,
    Replay
}

/// <summary>
/// The single entrance for ship data. The live feed and the recording both report here, and only
/// the current mode's reports reach the tracker. The rest of the app can't tell the difference.
/// </summary>
public sealed class ShipFeed
{
    private readonly ShipTracker _tracker;
    private readonly ShipRecorder _recorder;
    private readonly ShipRecording _recording;
    private volatile ShipDataMode _mode = ShipDataMode.Live;
    private long _lastLiveReportTicks = DateTime.UtcNow.Ticks;

    public ShipFeed(ShipTracker tracker, ShipRecorder recorder, ShipRecording recording)
    {
        _tracker = tracker;
        _recorder = recorder;
        _recording = recording;
    }

    public ShipDataMode Mode => _mode;

    public bool RecordingAvailable => _recording.Exists;

    public void ReportLive(Ship ship)
    {
        Volatile.Write(ref _lastLiveReportTicks, DateTime.UtcNow.Ticks);
        _recorder.Add(ship);

        if (_mode == ShipDataMode.Live)
        {
            _tracker.Update(ship);
        }
    }

    public void ReportReplay(Ship ship)
    {
        if (_mode == ShipDataMode.Replay)
        {
            _tracker.Update(ship);
        }
    }

    /// <summary>Switches modes and starts with an empty map, so the two data sources never mix.</summary>
    public bool SetMode(ShipDataMode mode)
    {
        if (mode == ShipDataMode.Replay && !RecordingAvailable)
        {
            return false;
        }

        if (mode != _mode)
        {
            // Give the live feed a fresh chance before it's treated as silent.
            Volatile.Write(ref _lastLiveReportTicks, DateTime.UtcNow.Ticks);
            _mode = mode;
            _tracker.Clear();
        }

        return true;
    }

    public bool IsLiveSilentFor(TimeSpan duration)
    {
        long lastReport = Volatile.Read(ref _lastLiveReportTicks);
        return DateTime.UtcNow.Ticks - lastReport > duration.Ticks;
    }
}
