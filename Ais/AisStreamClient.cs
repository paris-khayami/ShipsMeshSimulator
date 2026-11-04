using System.Net.WebSockets;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace ShipsMeshSimulator.Ais;

/// <summary>
/// Keeps a live connection to aisstream.io and prints each ship position it receives.
/// BackgroundService means this runs alongside the rest of the app until the app stops.
/// </summary>
public sealed class AisStreamClient : BackgroundService
{
    // If the feed is silent for this long, assume the connection is dead and reconnect.
    private static readonly TimeSpan SilenceLimit = TimeSpan.FromMinutes(2);
    private const int MaxRetryDelaySeconds = 60;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        // Accept both "Latitude" and "latitude".
        PropertyNameCaseInsensitive = true
    };

    private readonly AisStreamOptions _options;
    private readonly ILogger<AisStreamClient> _logger;
    private readonly IHostApplicationLifetime _lifetime;

    // How many connection attempts have failed in a row.
    private int _failedAttempts;

    public AisStreamClient(
        IOptions<AisStreamOptions> options,
        ILogger<AisStreamClient> logger,
        IHostApplicationLifetime lifetime)
    {
        _options = options.Value;
        _logger = logger;
        _lifetime = lifetime;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            _logger.LogError(
                "No API key found. Run this in the project folder, then start the app again: " +
                "dotnet user-secrets set \"AisStream:ApiKey\" \"YOUR_KEY\"");
            _lifetime.StopApplication();
            return;
        }

        // Networks fail, so keep reconnecting until the app is stopped.
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ConnectAndReadAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return; // The app is shutting down, which is not an error.
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("No ship data for {Minutes} minutes.", SilenceLimit.TotalMinutes);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Lost the connection to the ship feed: {Reason}", ex.Message);
            }

            _failedAttempts++;
            TimeSpan delay = GetRetryDelay(_failedAttempts);
            _logger.LogInformation("Reconnecting in {Seconds:F0} seconds...", delay.TotalSeconds);

            try
            {
                await Task.Delay(delay, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async Task ConnectAndReadAsync(CancellationToken stoppingToken)
    {
        using var socket = new ClientWebSocket();

        // Ask for compressed data. The service may limit bandwidth on uncompressed connections.
        socket.Options.DangerousDeflateOptions = new WebSocketDeflateOptions();

        await socket.ConnectAsync(new Uri(_options.Url), stoppingToken);
        _logger.LogInformation("Connected to the ship feed.");

        // The service closes the connection if this doesn't arrive within 3 seconds.
        await SendSubscriptionAsync(socket, stoppingToken);

        bool subscriptionConfirmed = false;

        while (socket.State == WebSocketState.Open)
        {
            // Give up on this read if the app stops or the feed goes silent.
            using var readTimeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            readTimeout.CancelAfter(SilenceLimit);

            byte[]? json = await ReadWholeMessageAsync(socket, readTimeout.Token);
            if (json is null)
            {
                break; // The server closed the connection.
            }

            AisEnvelope? envelope = ParseMessage(json);
            if (envelope?.MessageType == "SubscriptionConfirmation")
            {
                subscriptionConfirmed = true;
                _logger.LogInformation("Subscription accepted. Waiting for ships...");
            }
            else if (envelope is not null)
            {
                _failedAttempts = 0; // Data is flowing, so the connection is healthy.
                PrintShipPosition(envelope);
            }
        }

        if (subscriptionConfirmed)
        {
            _logger.LogWarning("The ship feed closed the connection.");
        }
        else
        {
            _logger.LogWarning(
                "The ship feed closed the connection before accepting the subscription. " +
                "Check that your API key is correct.");
        }
    }

    private async Task SendSubscriptionAsync(ClientWebSocket socket, CancellationToken token)
    {
        // These property names must match the aisstream.io documentation exactly.
        var subscription = new
        {
            APIKey = _options.ApiKey,
            BoundingBoxes = new[]
            {
                new[]
                {
                    new[] { _options.MinLatitude, _options.MinLongitude },
                    new[] { _options.MaxLatitude, _options.MaxLongitude }
                }
            },
            FilterMessageTypes = new[] { "PositionReport" }
        };

        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(subscription);
        await socket.SendAsync(
            new ArraySegment<byte>(bytes), WebSocketMessageType.Text, endOfMessage: true, token);
    }

    /// <summary>
    /// A WebSocket message can arrive in several pieces, so collect them all.
    /// Returns null if the server closed the connection.
    /// </summary>
    private static async Task<byte[]?> ReadWholeMessageAsync(ClientWebSocket socket, CancellationToken token)
    {
        var buffer = new byte[16 * 1024];
        using var message = new MemoryStream();

        WebSocketReceiveResult result;
        do
        {
            result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), token);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                return null;
            }

            message.Write(buffer, 0, result.Count);
        }
        while (!result.EndOfMessage);

        return message.ToArray();
    }

    private AisEnvelope? ParseMessage(byte[] json)
    {
        try
        {
            return JsonSerializer.Deserialize<AisEnvelope>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            // One bad message shouldn't stop the feed, so skip it and carry on.
            _logger.LogWarning("Skipped a message that couldn't be read: {Reason}", ex.Message);
            return null;
        }
    }

    private static void PrintShipPosition(AisEnvelope envelope)
    {
        PositionReport? report = envelope.Message?.PositionReport;
        AisMetaData? metaData = envelope.MetaData;

        // AIS uses out-of-range values, such as latitude 91, to mean "position not available".
        if (report is null || metaData is null || !report.Valid
            || Math.Abs(report.Latitude) > 90 || Math.Abs(report.Longitude) > 180)
        {
            return;
        }

        // Ship names are padded with spaces and are sometimes empty.
        string name = metaData.ShipName?.Trim() ?? "";
        if (name.Length == 0)
        {
            name = "(no name)";
        }

        // Compare these two times to see how far the feed lags behind the current time.
        string receivedUtc = DateTime.UtcNow.ToString("HH:mm:ss");
        string reportedUtc = GetTimeOfDay(metaData.TimeUtc);

        Console.WriteLine(
            $"received {receivedUtc} | reported {reportedUtc} | {name,-24} | MMSI {metaData.MMSI} | " +
            $"{report.Latitude,8:F4}, {report.Longitude,8:F4} | {report.Sog,4:F1} kn | {report.Cog,5:F1} deg");
    }

    /// <summary>Gets "11:40:02" from a time like "2026-11-04 11:40:02.123456 +0000 UTC".</summary>
    private static string GetTimeOfDay(string? time)
    {
        const int start = 11;
        const int length = 8;
        return time is not null && time.Length >= start + length
            ? time.Substring(start, length)
            : "unknown ";
    }

    /// <summary>
    /// Wait longer after each failure (2, 4, 8... seconds), with a little randomness.
    /// This avoids hammering a service that's having problems.
    /// </summary>
    private static TimeSpan GetRetryDelay(int failedAttempts)
    {
        double seconds = Math.Min(Math.Pow(2, Math.Min(failedAttempts, 6)), MaxRetryDelaySeconds);
        return TimeSpan.FromSeconds(seconds + Random.Shared.NextDouble());
    }
}
