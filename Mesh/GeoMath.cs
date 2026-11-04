namespace ShipsMeshSimulator.Mesh;

public static class GeoMath
{
    private const double EarthRadiusKm = 6371;

    /// <summary>
    /// The distance between two points on Earth, using the haversine formula.
    /// Latitude and longitude are angles on a sphere, so plain subtraction wouldn't be accurate.
    /// </summary>
    public static double DistanceKm(double latitude1, double longitude1, double latitude2, double longitude2)
    {
        double latitudeDifference = ToRadians(latitude2 - latitude1);
        double longitudeDifference = ToRadians(longitude2 - longitude1);

        double a = Math.Pow(Math.Sin(latitudeDifference / 2), 2)
            + Math.Cos(ToRadians(latitude1)) * Math.Cos(ToRadians(latitude2))
            * Math.Pow(Math.Sin(longitudeDifference / 2), 2);

        return 2 * EarthRadiusKm * Math.Asin(Math.Sqrt(a));
    }

    private static double ToRadians(double degrees) => degrees * Math.PI / 180;
}
