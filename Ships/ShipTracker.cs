using System.Collections.Concurrent;

namespace ShipsMeshSimulator.Ships;

/// <summary>
/// Remembers the latest position of every ship, looked up by its MMSI number.
/// </summary>
public sealed class ShipTracker
{
    // Concurrent means several threads can use it at once. One thread adds ships from the feed
    // while another removes old ones, and a normal Dictionary isn't safe for that.
    private readonly ConcurrentDictionary<long, Ship> _ships = new();

    public int Count => _ships.Count;

    public void Clear() => _ships.Clear();

    /// <summary>Adds a new ship, or replaces what we know about one we've already seen.</summary>
    public void Update(Ship update)
    {
        _ships.AddOrUpdate(
            update.Mmsi,
            update,
            // Some messages have no name, so keep the name we already know.
            (_, existing) => update.Name.Length > 0 ? update : update with { Name = existing.Name });
    }

    /// <summary>Returns a copy of the current list, so the caller can use it safely.</summary>
    public IReadOnlyList<Ship> GetAll() => _ships.Values.ToList();

    /// <summary>Removes ships we haven't heard from recently and returns how many were removed.</summary>
    public int RemoveStale(DateTimeOffset now, TimeSpan maxAge)
    {
        int removed = 0;

        foreach (KeyValuePair<long, Ship> entry in _ships)
        {
            // This form of TryRemove only removes the ship if it hasn't just been updated.
            if (now - entry.Value.LastSeenUtc > maxAge && _ships.TryRemove(entry))
            {
                removed++;
            }
        }

        return removed;
    }
}
