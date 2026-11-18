namespace BlueHarvest.Sim.World.Generation;

/// <summary>A cave's way in: a mouth (walked into) or a skylight (a hole in the roof), in island metres.</summary>
/// <param name="R">How close counts as standing in it, m (as the chunker draws it: 3.5 for a mouth, 2.5 for a skylight).</param>
public readonly record struct CaveOpening(int CaveId, double X, double Y, double R, bool Skylight);

/// <summary>
/// Ruins and wrecks by island-map cell, and cave openings by 64 m chunk, built once per island so
/// that finding them as the survivor walks is a cheap lookup (step 25b-1).
/// </summary>
public sealed class FeatureIndex
{
    public const int OpeningBucketM = 64;

    private readonly Dictionary<int, List<int>> _ruins = new(), _wrecks = new();
    private readonly Dictionary<(int X, int Y), List<CaveOpening>> _openings = new();
    private readonly int _nx, _ny;
    private readonly double _cell;

    private FeatureIndex(IslandWorld world)
    {
        _nx = world.Shape.Nx;
        _ny = world.Shape.Ny;
        _cell = world.Shape.Cell;
        IslandWorks w = world.Works;
        foreach (Ruin r in w.Ruins)
            Add(_ruins, CellAt(r.X, r.Y), r.Id);
        foreach (Wreck r in w.Wrecks)
            Add(_wrecks, CellAt(r.X, r.Y), r.Id);
        foreach (Cave c in w.Caves)
        {
            foreach ((double x, double y) in c.Mouths)
                AddOpening(new CaveOpening(c.Id, x, y, 3.5, false));
            foreach ((double x, double y) in c.Skylights)
                AddOpening(new CaveOpening(c.Id, x, y, 2.5, true));
        }
    }

    public static FeatureIndex Build(IslandWorld world) => new(world);

    /// <summary>The island-map cell holding a spot in island metres, clamped to the grid.</summary>
    public int CellAt(double x, double y)
    {
        int cx = Math.Clamp((int)Math.Floor(x / _cell), 0, _nx - 1), cy = Math.Clamp((int)Math.Floor(y / _cell), 0, _ny - 1);
        return cy * _nx + cx;
    }

    /// <summary>Ruin ids whose middle lies in a cell.</summary>
    public IReadOnlyList<int> RuinsIn(int cell) => _ruins.TryGetValue(cell, out List<int>? ids) ? ids : Array.Empty<int>();

    /// <summary>Wreck ids whose middle lies in a cell.</summary>
    public IReadOnlyList<int> WrecksIn(int cell) => _wrecks.TryGetValue(cell, out List<int>? ids) ? ids : Array.Empty<int>();

    /// <summary>Cave openings within a distance of a spot, by cave id (mouths before skylights, then west to east).</summary>
    public List<CaveOpening> OpeningsNear(double x, double y, double range)
    {
        var near = new List<CaveOpening>();
        for (int by = Bucket(y - range); by <= Bucket(y + range); by++)
        {
            for (int bx = Bucket(x - range); bx <= Bucket(x + range); bx++)
            {
                if (!_openings.TryGetValue((bx, by), out List<CaveOpening>? list))
                    continue;
                foreach (CaveOpening o in list)
                {
                    double dx = o.X - x, dy = o.Y - y;
                    if (dx * dx + dy * dy <= range * range)
                        near.Add(o);
                }
            }
        }
        near.Sort((a, b) => a.CaveId != b.CaveId ? a.CaveId.CompareTo(b.CaveId)
            : a.Skylight != b.Skylight ? a.Skylight.CompareTo(b.Skylight)
            : a.X != b.X ? a.X.CompareTo(b.X) : a.Y.CompareTo(b.Y));
        return near;
    }

    private void AddOpening(CaveOpening o)
    {
        var key = (Bucket(o.X), Bucket(o.Y));
        if (!_openings.TryGetValue(key, out List<CaveOpening>? list))
            _openings[key] = list = new List<CaveOpening>();
        list.Add(o);
    }

    private static void Add(Dictionary<int, List<int>> map, int cell, int id)
    {
        if (!map.TryGetValue(cell, out List<int>? ids))
            map[cell] = ids = new List<int>();
        ids.Add(id);
    }

    private static int Bucket(double m) => (int)Math.Floor(m / OpeningBucketM);
}

/// <summary>
/// What a survivor sees of the island at island scale (step 25b-1, decision 4): 250 m in daylight on
/// open ground; 60 m at night, indoors or in dense jungle (jungle, rainforest, cloud forest); ×1.5 from
/// 20 m or more above the surrounding ground. The cell you stand in is always seen. Pure: the
/// simulation calls it after each of the survivor's actions; the island lab ports it
/// (tools/island-lab/golden-knowledge.js).
/// </summary>
public static class MapSight
{
    public const double OpenRangeM = 250, ShortRangeM = 60, HighFactor = 1.5, HighAboveM = 20;

    /// <summary>A cave is found within ordinary sight of an opening, m.</summary>
    public const double CaveSightM = 30;

    /// <summary>How far a survivor at a spot (island metres) sees at island scale, m.</summary>
    public static double Range(IslandWorld world, double x, double y, bool daylight, bool indoors)
    {
        IslandShape s = world.Shape;
        int cx = Math.Clamp((int)Math.Floor(x / s.Cell), 0, s.Nx - 1), cy = Math.Clamp((int)Math.Floor(y / s.Cell), 0, s.Ny - 1);
        int i = cy * s.Nx + cx;
        var biome = (Biome)world.Climate.Biomes[i];
        bool dense = biome is Biome.Moist or Biome.Wet or Biome.Cloud;
        double range = daylight && !indoors && !dense ? OpenRangeM : ShortRangeM;
        // the ground round about: the mean of the neighbouring cells (heights as the chunker reads them)
        double sum = 0;
        int n = 0;
        for (int dy = -1; dy <= 1; dy++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                int ax = cx + dx, ay = cy + dy;
                if ((dx == 0 && dy == 0) || ax < 0 || ay < 0 || ax >= s.Nx || ay >= s.Ny)
                    continue;
                sum += (float)s.Height[ay * s.Nx + ax];
                n++;
            }
        }
        if (n > 0 && (float)s.Height[i] - sum / n >= HighAboveM)
            range *= HighFactor;
        return range;
    }

    /// <summary>
    /// Fills in what a survivor at a spot sees within a range: every cell any part of which lies within
    /// it (and always the cell they stand in), the ruins and wrecks in newly seen cells, cave openings
    /// within <see cref="CaveSightM"/>, the trail, and any site they stand in. Returns the features
    /// found and the sites entered for the first time, in a fixed order.
    /// </summary>
    public static (List<FoundFeature> Found, List<int> Sites) See(MapKnowledge k, IslandWorld world, FeatureIndex features,
        double x, double y, double range, long tick)
    {
        IslandShape s = world.Shape;
        double cell = s.Cell;
        var found = new List<FoundFeature>();
        var sites = new List<int>();
        int here = features.CellAt(x, y);
        k.Walk(here, tick);
        int cx0 = Math.Max(0, (int)Math.Floor((x - range) / cell)), cx1 = Math.Min(s.Nx - 1, (int)Math.Floor((x + range) / cell));
        int cy0 = Math.Max(0, (int)Math.Floor((y - range) / cell)), cy1 = Math.Min(s.Ny - 1, (int)Math.Floor((y + range) / cell));
        SeeCell(here);
        for (int cy = cy0; cy <= cy1; cy++)
        {
            for (int cx = cx0; cx <= cx1; cx++)
            {
                double ddx = Math.Max(0, Math.Max(cx * cell - x, x - (cx + 1) * cell));
                double ddy = Math.Max(0, Math.Max(cy * cell - y, y - (cy + 1) * cell));
                if (ddx * ddx + ddy * ddy <= range * range)
                    SeeCell(cy * s.Nx + cx);
            }
        }
        // a cave in sight is found; standing in any of its openings, it's entered
        var caves = new SortedDictionary<int, FoundHow>();
        foreach (CaveOpening o in features.OpeningsNear(x, y, CaveSightM))
        {
            double dx = o.X - x, dy = o.Y - y;
            FoundHow how = dx * dx + dy * dy <= o.R * o.R ? FoundHow.Entered : FoundHow.Seen;
            if (!caves.TryGetValue(o.CaveId, out FoundHow was) || how == FoundHow.Entered && was == FoundHow.Seen)
                caves[o.CaveId] = how;
        }
        foreach ((int id, FoundHow how) in caves)
        {
            var f = new FoundFeature(FeatureKind.Cave, id, how, tick);
            if (k.Find(f))
                found.Add(f);
        }
        IReadOnlyList<Site> all = world.Works.Sites;
        for (int i = 0; i < all.Count; i++)
        {
            Site t = all[i];
            if (Math.Max(Math.Abs(x - t.X), Math.Abs(y - t.Y)) <= t.R && k.EnterSite(i))
                sites.Add(i);
        }
        return (found, sites);

        void SeeCell(int c)
        {
            if (!k.See(c))
                return;
            foreach (int id in features.RuinsIn(c))
            {
                var f = new FoundFeature(FeatureKind.Ruin, id, FoundHow.Seen, tick);
                if (k.Find(f))
                    found.Add(f);
            }
            foreach (int id in features.WrecksIn(c))
            {
                var f = new FoundFeature(FeatureKind.Wreck, id, FoundHow.Seen, tick);
                if (k.Find(f))
                    found.Add(f);
            }
        }
    }
}
