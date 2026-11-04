using Microsoft.Extensions.Options;

namespace ShipsMeshSimulator.Ships;

/// <summary>
/// Plays the recording back at its original speed when the app is in replay mode. It also watches
/// the live feed and switches to the recording if the feed goes silent.
/// </summary>
public sealed class ShipReplayer : BackgroundService
{
    // Moored ships report only every few minutes. Starting this far into the recording
    // fills the map right away instead of slowly.
    private static readonly TimeSpan HeadStart = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan LongestWait = TimeSpan.FromMilliseconds(500);

    private readonly ShipFeed _feed;
    private readonly ShipRecording _recording;
    private readonly RecordingOptions _options;
    private readonly ILogger<ShipReplayer> _logger;

    public ShipReplayer(
        ShipFeed feed,
        ShipRecording recording,
        IOptions<RecordingOptions> options,
        ILogger<ShipReplayer> logger)
    {
        _feed = feed;
        _recording = recording;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        TimeSpan silenceLimit = TimeSpan.FromSeconds(_options.SilenceBeforeReplaySeconds);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                if (_feed.Mode == ShipDataMode.Replay)
                {
                    await PlayRecordingAsync(stoppingToken);
                }
                else if (_feed.IsLiveSilentFor(silenceLimit) && _feed.SetMode(ShipDataMode.Replay))
                {
                    _logger.LogWarning("The live feed is silent, so the app switched to the recording.");
                }

                await Task.Delay(CheckInterval, stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            // The app is shutting down, which is not an error.
        }
    }

    private async Task PlayRecordingAsync(CancellationToken stoppingToken)
    {
        IReadOnlyList<RecordedPosition> positions;
        try
        {
            positions = _recording.Load();
        }
        catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException)
        {
            _logger.LogError("Couldn't read the recording: {Reason}", ex.Message);
            _feed.SetMode(ShipDataMode.Live);
            return;
        }

        DateTimeOffset startedAt = DateTimeOffset.UtcNow - HeadStart;

        foreach (RecordedPosition position in positions)
        {
            // Wait until it's time for this report, checking often in case the mode changes.
            TimeSpan wait = startedAt + TimeSpan.FromSeconds(position.OffsetSeconds) - DateTimeOffset.UtcNow;
            while (wait > TimeSpan.Zero && _feed.Mode == ShipDataMode.Replay)
            {
                await Task.Delay(wait < LongestWait ? wait : LongestWait, stoppingToken);
                wait = startedAt + TimeSpan.FromSeconds(position.OffsetSeconds) - DateTimeOffset.UtcNow;
            }

            if (_feed.Mode != ShipDataMode.Replay)
            {
                return;
            }

            // The ship's report is real, but it's stamped with the current time.
            // Otherwise the tracker would treat it as out of date and remove the ship.
            _feed.ReportReplay(new Ship(
                position.Mmsi,
                position.Name,
                position.Latitude,
                position.Longitude,
                position.SpeedKnots,
                position.CourseDegrees,
                DateTimeOffset.UtcNow));
        }
    }
}
