using Microsoft.Extensions.Options;

namespace ShipsMeshSimulator.Ships;

/// <summary>
/// On a timer, removes ships that have stopped reporting and prints a summary.
/// The map will replace this printed summary in Milestone 3.
/// </summary>
public sealed class ShipReporter : BackgroundService
{
    private const int ShipsToPrint = 10;

    private readonly ShipTracker _tracker;
    private readonly TrackingOptions _options;

    public ShipReporter(ShipTracker tracker, IOptions<TrackingOptions> options)
    {
        _tracker = tracker;
        _options = options.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        TimeSpan maxAge = TimeSpan.FromMinutes(_options.StaleAfterMinutes);
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(_options.ReportEverySeconds));

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                DateTimeOffset now = DateTimeOffset.UtcNow;
                int removed = _tracker.RemoveStale(now, maxAge);
                PrintSummary(now, removed);
            }
        }
        catch (OperationCanceledException)
        {
            // The app is shutting down, which is not an error.
        }
    }

    private void PrintSummary(DateTimeOffset now, int removed)
    {
        IReadOnlyList<Ship> ships = _tracker.GetAll();

        Console.WriteLine();
        Console.WriteLine($"Tracking {ships.Count} ships. Removed {removed} that stopped reporting.");
        Console.WriteLine($"Heard most recently (up to {ShipsToPrint}):");

        foreach (Ship ship in ships.OrderByDescending(s => s.LastSeenUtc).Take(ShipsToPrint))
        {
            string name = ship.Name.Length > 0 ? ship.Name : "(no name)";
            double ageSeconds = (now - ship.LastSeenUtc).TotalSeconds;

            Console.WriteLine(
                $"  {name,-24} | MMSI {ship.Mmsi} | {ship.Latitude,8:F4}, {ship.Longitude,8:F4} | " +
                $"{ship.SpeedKnots,4:F1} kn | {ship.CourseDegrees,5:F1} deg | {ageSeconds,3:F0} s ago");
        }
    }
}
