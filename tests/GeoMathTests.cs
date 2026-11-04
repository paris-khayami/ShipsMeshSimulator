using ShipsMeshSimulator.Mesh;
using Xunit;

namespace ShipsMeshSimulator.Tests;

public class GeoMathTests
{
    [Fact]
    public void Same_point_is_zero_km_away()
    {
        Assert.Equal(0, GeoMath.DistanceKm(53.5, 9.9, 53.5, 9.9), precision: 6);
    }

    [Fact]
    public void A_tenth_of_a_degree_of_latitude_is_about_11_km()
    {
        Assert.Equal(11.12, GeoMath.DistanceKm(53.5, 9.9, 53.6, 9.9), precision: 2);
    }

    [Fact]
    public void Distance_is_the_same_in_both_directions()
    {
        double there = GeoMath.DistanceKm(53.54, 9.87, 53.87, 8.70);
        double back = GeoMath.DistanceKm(53.87, 8.70, 53.54, 9.87);

        Assert.Equal(there, back, precision: 6);
    }
}
