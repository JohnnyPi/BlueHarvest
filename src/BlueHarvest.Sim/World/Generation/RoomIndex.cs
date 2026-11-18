namespace BlueHarvest.Sim.World.Generation;

/// <summary>
/// One named room of a generated building, in island metres (inclusive, walls included, so rooms
/// side by side share a wall line, and a door between them lies in both). A building's corridor, the
/// space between its two rows of rooms, is no room: it has no name and isn't indexed.
/// </summary>
/// <param name="Id">Its place in the index: buildings in drawing order, rooms in each building's order. Stable per island seed.</param>
/// <param name="Name">The room's own name, as the programme gives it ("Server hall").</param>
/// <param name="Building">The building it's in ("Server farm"; "Station at Genetics complex" for a transit station).</param>
/// <param name="Place">The facility's name; the paddock's for a viewing station; for transit, the station's ("Monorail station at Genetics complex").</param>
/// <param name="SiteIndex">Index into <see cref="IslandWorks.Sites"/>, or −1 for paddocks and transit.</param>
/// <param name="Kind">The facility's kind ("genetics"), or "paddock" or "transit".</param>
/// <param name="KeycardLevel">0 for an ordinary room, 2 or 3 for one behind a security door (<see cref="RoomRules.KeycardLevel"/>).</param>
/// <param name="Motorised">Its doors are motorised (<see cref="RoomRules.MotorRooms"/>).</param>
/// <param name="Label">The room's index into <see cref="IslandWorks.Labels"/> (unique per room).</param>
public sealed record IndexedRoom(int Id, string Name, string Building, string Place, int SiteIndex, string Kind,
    int X0, int Y0, int X1, int Y1, int KeycardLevel, bool Motorised, int Label)
{
    public bool Contains(long x, long y) => x >= X0 && x <= X1 && y >= Y0 && y <= Y1;
}

/// <summary>
/// Every named room on the island, read once from the generator's buildings (compounds, paddock
/// viewing stations and transit stations) so that the play map, items, power and quests can ask
/// "which room is this?" without going back to the labels. Built from the island alone; the same
/// island always gives the same rows.
/// </summary>
public sealed class RoomIndex
{
    /// <summary>The lookup buckets' size, m: one island chunk.</summary>
    public const int BucketM = 64;

    private readonly List<IndexedRoom> _rooms;
    private readonly Dictionary<(int X, int Y), List<IndexedRoom>> _buckets = new();
    private readonly Dictionary<int, List<IndexedRoom>> _bySite = new();

    private RoomIndex(List<IndexedRoom> rooms)
    {
        _rooms = rooms;
        foreach (IndexedRoom r in rooms)
        {
            for (int by = Bucket(r.Y0); by <= Bucket(r.Y1); by++)
            {
                for (int bx = Bucket(r.X0); bx <= Bucket(r.X1); bx++)
                {
                    if (!_buckets.TryGetValue((bx, by), out List<IndexedRoom>? list))
                        _buckets[(bx, by)] = list = new List<IndexedRoom>();
                    list.Add(r);
                }
            }
            if (r.SiteIndex >= 0)
            {
                if (!_bySite.TryGetValue(r.SiteIndex, out List<IndexedRoom>? mine))
                    _bySite[r.SiteIndex] = mine = new List<IndexedRoom>();
                mine.Add(r);
            }
        }
    }

    /// <summary>Every room, by id.</summary>
    public IReadOnlyList<IndexedRoom> Rooms => _rooms;

    public int Count => _rooms.Count;

    /// <summary>Indexes every room of every building on the island.</summary>
    public static RoomIndex Build(IslandWorld world)
    {
        IslandWorks works = world.Works;
        var siteByName = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < works.Sites.Count; i++)
            siteByName.TryAdd(works.Sites[i].Name, i);
        var paddocks = new HashSet<string>(works.Paddocks.Select(q => q.Name), StringComparer.Ordinal);

        var rooms = new List<IndexedRoom>();
        foreach (Prim p in works.Prims.InOrder())
        {
            if (p.Kind != PrimKind.Building || p.Rooms is null)
                continue;
            string[] parts = works.Labels[p.Label].Split(" · ");
            string owner = parts[0], building = parts.Length > 1 ? parts[1] : parts[0];
            string place, kind;
            int siteIndex = -1;
            if (siteByName.TryGetValue(owner, out int si))
            {
                siteIndex = si;
                place = owner;
                kind = works.Sites[si].Kind;
            }
            else if (paddocks.Contains(owner))
            {
                place = owner;
                kind = "paddock";
            }
            else
            {
                place = StationName(owner, building, siteByName);
                kind = "transit";
            }
            foreach (Room r in p.Rooms)
            {
                string name = works.Labels[r.Label].Split(" · ")[^1];
                rooms.Add(new IndexedRoom(rooms.Count, name, building, place, siteIndex, kind, r.X0, r.Y0, r.X1, r.Y1,
                    RoomRules.KeycardLevel(name), RoomRules.IsMotorised(name), r.Label));
            }
        }
        return new RoomIndex(rooms);
    }

    /// <summary>The room at a spot in island metres; on a shared wall or a door, the one with the lower id. Null outside every room.</summary>
    public IndexedRoom? At(long x, long y)
    {
        if (x < int.MinValue || x > int.MaxValue || y < int.MinValue || y > int.MaxValue)
            return null;
        if (!_buckets.TryGetValue((Bucket((int)x), Bucket((int)y)), out List<IndexedRoom>? list))
            return null;
        foreach (IndexedRoom r in list)
        {
            if (r.Contains(x, y))
                return r;
        }
        return null;
    }

    /// <summary>Every room that overlaps a rectangle in island metres (inclusive), by id.</summary>
    public IReadOnlyList<IndexedRoom> In(int x0, int y0, int x1, int y1)
    {
        var found = new SortedDictionary<int, IndexedRoom>();
        for (int by = Bucket(Math.Min(y0, y1)); by <= Bucket(Math.Max(y0, y1)); by++)
        {
            for (int bx = Bucket(Math.Min(x0, x1)); bx <= Bucket(Math.Max(x0, x1)); bx++)
            {
                if (!_buckets.TryGetValue((bx, by), out List<IndexedRoom>? list))
                    continue;
                foreach (IndexedRoom r in list)
                {
                    if (r.X0 <= Math.Max(x0, x1) && r.X1 >= Math.Min(x0, x1) && r.Y0 <= Math.Max(y0, y1) && r.Y1 >= Math.Min(y0, y1))
                        found.TryAdd(r.Id, r);
                }
            }
        }
        return found.Values.ToList();
    }

    /// <summary>The rooms of one facility (an index into <see cref="IslandWorks.Sites"/>), by id. Empty for a site without buildings.</summary>
    public IReadOnlyList<IndexedRoom> OfSite(int siteIndex) =>
        _bySite.TryGetValue(siteIndex, out List<IndexedRoom>? mine) ? mine : Array.Empty<IndexedRoom>();

    /// <summary>How the front end names a room: the place and the room, "Genetics complex · Server hall", with no building in between.</summary>
    public static string Describe(IndexedRoom room) => room.Place + " · " + room.Name;

    /// <summary>
    /// A transit station's name from its line and building ("Station at Genetics complex"): "Monorail
    /// station at Genetics complex", "Summit lift station at Control centre", or the stop's own name
    /// where it isn't at a facility ("Skyway station, north-east").
    /// </summary>
    private static string StationName(string line, string building, Dictionary<string, int> siteByName)
    {
        const string at = "Station at ";
        string stop = building.StartsWith(at, StringComparison.Ordinal) ? building[at.Length..] : building;
        string lineName = line == "Underground monorail" ? "Monorail" : line;
        if (siteByName.ContainsKey(stop))
            return lineName + " station at " + stop;
        return stop.Length == 0 ? lineName + " station" : char.ToUpperInvariant(stop[0]) + stop[1..];
    }

    private static int Bucket(int m) => m >= 0 ? m / BucketM : -((-m + BucketM - 1) / BucketM);
}
