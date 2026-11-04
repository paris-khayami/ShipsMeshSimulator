using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;

namespace ShipsMeshSimulator.Ships;

/// <summary>
/// On a timer, removes ships that have stopped reporting and sends the rest to every browser.
/// </summary>
public sealed class ShipBroadcaster : BackgroundService
{
    // AIS uses 102.3 knots to mean "speed not available".
    private const double SpeedUnavailableKnots = 102;
    private const double MovingSpeedKnots = 1;

    private readonly ShipTracker _tracker;
    private readonly IHubContext<ShipHub> _hub;
    private readonly TrackingOptions _options;
    private readonly ILogger<ShipBroadcaster> _logger;

    public ShipBroadcaster(
        ShipTracker tracker,
        IHubContext<ShipHub> hub,
        IOptions<TrackingOptions> options,
        ILogger<ShipBroadcaster> logger)
    {
        _tracker = tracker;
        _hub = hub;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        TimeSpan maxAge = TimeSpan.FromMinutes(_options.StaleAfterMinutes);
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(_options.BroadcastEverySeconds));

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                _tracker.RemoveStale(DateTimeOffset.UtcNow, maxAge);

                IReadOnlyList<Ship> ships = _tracker.GetAll();
                var update = new ShipsUpdate(ships.Count, SelectShipsForMap(ships));

                try
                {
                    await _hub.Clients.All.SendAsync("ShipsUpdated", update, stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // One failed send shouldn't stop future updates.
                    _logger.LogWarning("Couldn't send ships to the browser: {Reason}", ex.Message);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // The app is shutting down, which is not an error.
        }
    }

    /// <summary>
    /// Too many ships make the map hard to read, so choose moving ships first.
    /// Sorting by MMSI keeps the same ships on the map from one update to the next.
    /// </summary>
    private IReadOnlyList<Ship> SelectShipsForMap(IReadOnlyList<Ship> ships)
    {
        return ships
            .OrderByDescending(IsMoving)
            .ThenBy(ship => ship.Mmsi)
            .Take(_options.MaxShipsOnMap)
            .ToList();
    }

    private static bool IsMoving(Ship ship)
    {
        return ship.SpeedKnots >= MovingSpeedKnots && ship.SpeedKnots < SpeedUnavailableKnots;
    }
}
