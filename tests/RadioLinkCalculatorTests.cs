using ShipsMeshSimulator.Mesh;
using ShipsMeshSimulator.Ships;
using Xunit;

namespace ShipsMeshSimulator.Tests;

public class RadioLinkCalculatorTests
{
    // Far from the test ships, so it only links when a test places a ship beside it.
    private static readonly ShoreStation Shore = new(ShoreStation.NodeId, "Shore", 10, 10);

    private static Ship ShipAt(long mmsi, double latitude, double longitude = 9.9) =>
        new(mmsi, "SHIP", latitude, longitude, 0, 0, DateTimeOffset.UtcNow);

    [Fact]
    public void Ships_within_range_are_linked()
    {
        // These ships are about 2.2 km apart.
        var ships = new[] { ShipAt(1, 53.50), ShipAt(2, 53.52) };

        RadioLink link = Assert.Single(RadioLinkCalculator.FindLinks(ships, Shore, rangeKm: 3));

        Assert.Equal(1, link.NodeA);
        Assert.Equal(2, link.NodeB);
    }

    [Fact]
    public void Ships_out_of_range_are_not_linked()
    {
        var ships = new[] { ShipAt(1, 53.50), ShipAt(2, 53.60) };

        Assert.Empty(RadioLinkCalculator.FindLinks(ships, Shore, rangeKm: 3));
    }

    [Fact]
    public void Each_pair_of_ships_gets_only_one_link()
    {
        var ships = new[] { ShipAt(1, 53.50), ShipAt(2, 53.501), ShipAt(3, 53.502) };

        Assert.Equal(3, RadioLinkCalculator.FindLinks(ships, Shore, rangeKm: 3).Count);
    }

    [Fact]
    public void A_ship_near_the_shore_station_is_linked_to_it()
    {
        var ships = new[] { ShipAt(1, 10.001, 10) };

        RadioLink link = Assert.Single(RadioLinkCalculator.FindLinks(ships, Shore, rangeKm: 3));

        Assert.Equal(ShoreStation.NodeId, link.NodeB);
    }

    [Fact]
    public void Signal_gets_weaker_with_distance()
    {
        RadioLink close = Assert.Single(RadioLinkCalculator.FindLinks(
            new[] { ShipAt(1, 53.50), ShipAt(2, 53.505) }, Shore, rangeKm: 3));
        RadioLink far = Assert.Single(RadioLinkCalculator.FindLinks(
            new[] { ShipAt(1, 53.50), ShipAt(2, 53.52) }, Shore, rangeKm: 3));

        Assert.True(close.SignalPercent > far.SignalPercent);
    }

    [Fact]
    public void A_range_of_zero_gives_no_links()
    {
        var ships = new[] { ShipAt(1, 53.50), ShipAt(2, 53.50) };

        Assert.Empty(RadioLinkCalculator.FindLinks(ships, Shore, rangeKm: 0));
    }
}
