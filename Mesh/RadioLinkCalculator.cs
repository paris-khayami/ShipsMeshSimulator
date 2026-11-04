using ShipsMeshSimulator.Ships;

namespace ShipsMeshSimulator.Mesh;

/// <summary>
/// Works out which ships could talk to each other, using their real distances.
/// The links are simulated: real ships aren't connected this way.
/// </summary>
public static class RadioLinkCalculator
{
    public static IReadOnlyList<RadioLink> FindLinks(
        IReadOnlyList<Ship> ships,
        ShoreStation shore,
        double rangeKm)
    {
        var links = new List<RadioLink>();

        if (rangeKm <= 0)
        {
            return links;
        }

        for (int i = 0; i < ships.Count; i++)
        {
            Ship ship = ships[i];

            // Each pair is only checked once, so j starts just after i.
            for (int j = i + 1; j < ships.Count; j++)
            {
                Ship other = ships[j];
                AddLinkIfInRange(
                    links, rangeKm,
                    ship.Mmsi, ship.Latitude, ship.Longitude,
                    other.Mmsi, other.Latitude, other.Longitude);
            }

            AddLinkIfInRange(
                links, rangeKm,
                ship.Mmsi, ship.Latitude, ship.Longitude,
                shore.Id, shore.Latitude, shore.Longitude);
        }

        return links;
    }

    private static void AddLinkIfInRange(
        List<RadioLink> links,
        double rangeKm,
        long idA, double latitudeA, double longitudeA,
        long idB, double latitudeB, double longitudeB)
    {
        double distanceKm = GeoMath.DistanceKm(latitudeA, longitudeA, latitudeB, longitudeB);

        if (distanceKm <= rangeKm)
        {
            // A simple model: 100% when side by side, falling to 0% at the edge of the range.
            double signalPercent = 100 * (1 - distanceKm / rangeKm);
            links.Add(new RadioLink(idA, idB, distanceKm, signalPercent));
        }
    }
}
