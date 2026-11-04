using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Extensions.Options;

namespace ShipsMeshSimulator.Ships;

/// <summary>
/// Saves live ship reports to the recording file, if recording is turned on.
/// It writes to a temporary file first, so stopping early can't damage an existing recording.
/// </summary>
public sealed class ShipRecorder : BackgroundService
{
    private readonly Channel<Ship> _queue = Channel.CreateUnbounded<Ship>();
    private readonly ShipRecording _recording;
    private readonly RecordingOptions _options;
    private readonly ILogger<ShipRecorder> _logger;
    private volatile bool _isRecording;

    public ShipRecorder(
        ShipRecording recording,
        IOptions<RecordingOptions> options,
        ILogger<ShipRecorder> logger)
    {
        _recording = recording;
        _options = options.Value;
        _logger = logger;
        _isRecording = _options.Enabled;
    }

    public void Add(Ship ship)
    {
        if (_isRecording)
        {
            _queue.Writer.TryWrite(ship);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            return;
        }

        _logger.LogInformation("Recording ship data for {Minutes} minutes...", _options.Minutes);

        try
        {
            int count = await WriteTemporaryFileAsync(stoppingToken);
            File.Move(TemporaryFilePath, _recording.FilePath, overwrite: true);
            _logger.LogInformation(
                "Recording finished. Saved {Count} ship reports to {Path}.", count, _recording.FilePath);
        }
        catch (IOException ex)
        {
            _logger.LogError("Couldn't save the recording: {Reason}", ex.Message);
        }
        finally
        {
            _isRecording = false;
        }
    }

    private string TemporaryFilePath => _recording.FilePath + ".tmp";

    private async Task<int> WriteTemporaryFileAsync(CancellationToken stoppingToken)
    {
        string? folder = Path.GetDirectoryName(_recording.FilePath);
        if (!string.IsNullOrEmpty(folder))
        {
            Directory.CreateDirectory(folder);
        }

        using var timeLimit = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        timeLimit.CancelAfter(TimeSpan.FromMinutes(_options.Minutes));

        DateTimeOffset startedAt = DateTimeOffset.UtcNow;
        int count = 0;

        await using var writer = new StreamWriter(TemporaryFilePath);

        try
        {
            await foreach (Ship ship in _queue.Reader.ReadAllAsync(timeLimit.Token))
            {
                var position = new RecordedPosition(
                    (ship.LastSeenUtc - startedAt).TotalSeconds,
                    ship.Mmsi,
                    ship.Name,
                    ship.Latitude,
                    ship.Longitude,
                    ship.SpeedKnots,
                    ship.CourseDegrees);

                await writer.WriteLineAsync(JsonSerializer.Serialize(position));
                count++;
            }
        }
        catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
        {
            // The time limit was reached, so the recording is complete.
        }

        return count;
    }
}
