using ShipsMeshSimulator.Ships;
using Xunit;

namespace ShipsMeshSimulator.Tests;

public class ShipTrackerTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 4, 12, 0, 0, TimeSpan.Zero);

    private static Ship ShipNamed(string name, double latitude = 53.5, DateTimeOffset? lastSeen = null) =>
        new(1, name, latitude, 9.9, 0, 0, lastSeen ?? Now);

    [Fact]
    public void A_new_report_replaces_the_old_one()
    {
        var tracker = new ShipTracker();

        tracker.Update(ShipNamed("A", latitude: 53.5));
        tracker.Update(ShipNamed("A", latitude: 53.6));

        Ship ship = Assert.Single(tracker.GetAll());
        Assert.Equal(53.6, ship.Latitude);
    }

    [Fact]
    public void A_report_with_no_name_keeps_the_known_name()
    {
        var tracker = new ShipTracker();

        tracker.Update(ShipNamed("KNOWN NAME"));
        tracker.Update(ShipNamed(""));

        Assert.Equal("KNOWN NAME", Assert.Single(tracker.GetAll()).Name);
    }

    [Fact]
    public void A_ship_that_stopped_reporting_is_removed()
    {
        var tracker = new ShipTracker();
        tracker.Update(ShipNamed("OLD", lastSeen: Now.AddMinutes(-11)));

        int removed = tracker.RemoveStale(Now, TimeSpan.FromMinutes(10));

        Assert.Equal(1, removed);
        Assert.Empty(tracker.GetAll());
    }

    [Fact]
    public void A_ship_that_reported_recently_is_kept()
    {
        var tracker = new ShipTracker();
        tracker.Update(ShipNamed("RECENT", lastSeen: Now.AddMinutes(-9)));

        Assert.Equal(0, tracker.RemoveStale(Now, TimeSpan.FromMinutes(10)));
        Assert.Single(tracker.GetAll());
    }
}
