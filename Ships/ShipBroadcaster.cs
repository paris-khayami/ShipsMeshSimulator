using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
using ShipsMeshSimulator.Mesh;

namespace ShipsMeshSimulator.Ships;

/// <summary>
/// On a timer, removes ships that have stopped reporting, works out the simulated radio links,
/// and sends the result to every browser.
/// </summary>
public sealed class ShipBroadcaster : BackgroundService
{
    // AIS uses 102.3 knots to mean "speed not available".
    private const double SpeedUnavailableKnots = 102;
    private const double MovingSpeedKnots = 1;

    private readonly ShipTracker _tracker;
    private readonly IHubContext<ShipHub> _hub;
    private readonly TrackingOptions _options;
    private readonly MeshOptions _meshOptions;
    private readonly ShoreStation _shore;
    private readonly MeshNetwork _network;
    private readonly ILogger<ShipBroadcaster> _logger;

    public ShipBroadcaster(
        ShipTracker tracker,
        IHubContext<ShipHub> hub,
        IOptions<TrackingOptions> options,
        IOptions<MeshOptions> meshOptions,
        MeshNetwork network,
        ILogger<ShipBroadcaster> logger)
    {
        _tracker = tracker;
        _hub = hub;
        _options = options.Value;
        _meshOptions = meshOptions.Value;
        _network = network;
        _shore = new ShoreStation(
            ShoreStation.NodeId,
            _meshOptions.ShoreName,
            _meshOptions.ShoreLatitude,
            _meshOptions.ShoreLongitude);
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
                IReadOnlyList<Ship> shipsOnMap = SelectShipsForMap(ships);

                // Killed ships stay on the map, but their radios can't make links.
                List<Ship> shipsWithRadios = shipsOnMap
                    .Where(ship => !_network.IsKilled(ship.Mmsi))
                    .ToList();
                IReadOnlyList<RadioLink> links = RadioLinkCalculator.FindLinks(
                    shipsWithRadios, _shore, _meshOptions.RadioRangeKm);

                _network.Sync(shipsWithRadios, _shore, links);

                var update = new ShipsUpdate(
                    ships.Count,
                    shipsOnMap,
                    _shore,
                    links,
                    _network.GetRoutes(),
                    _network.GetKilledIds(),
                    _meshOptions.RadioRangeKm);

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
