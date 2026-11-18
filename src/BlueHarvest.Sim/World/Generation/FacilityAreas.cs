namespace BlueHarvest.Sim.World.Generation;

/// <summary>
/// The island divided into areas, each dominated by one facility (step 25b-2; the base for step 31's
/// threat zones and step 56's facility effects). Every land cell of the island map goes to the
/// facility quickest to walk to, by the walking model (slopes, fords and roads count), with minor
/// facilities' times counting double, so the major ones dominate and each minor one holds a smaller
/// patch of its own. Cells no facility can walk to go to the nearest in a straight line. A facility's
/// footprint (its compound's square) is always its own, and some kinds hold a guaranteed radius round
/// them (<see cref="MinRadiusM"/>: the raptor pen), outside other footprints. Each area is cut like a pie round its facility
/// into <see cref="SheetCount"/> sheets of equal cell count: what one park map charts, a tenth of the
/// area. Pure and generator-level; ported in the island lab (gen.js facilityAreas).
/// </summary>
public sealed class FacilityAreas
{
    public const int SheetCount = 10;

    /// <summary>
    /// Kinds whose area always holds the land within this many metres of them, except other facilities'
    /// footprints (the user's decision, 18 Nov: the raptor pen, 1 km, without touching the genetics complex).
    /// </summary>
    public static readonly IReadOnlyDictionary<string, double> MinRadiusM = new Dictionary<string, double>(StringComparer.Ordinal) { ["raptors"] = 1000 };

    private readonly int[] _area;
    private readonly sbyte[] _sheet;
    private readonly int[] _facilitySheet;
    private readonly int[][] _order;
    private readonly List<int>[] _cells;
    private readonly List<int>[][] _sheetCells;
    private readonly int _nx, _ny;
    private readonly double _cell;

    private FacilityAreas(IslandWorld world)
    {
        IslandShape s = world.Shape;
        IslandWorks w = world.Works;
        IReadOnlyList<Site> sites = w.Sites;
        _nx = s.Nx;
        _ny = s.Ny;
        _cell = s.Cell;
        int n = s.N, cnx = w.WalkNx, cny = w.WalkNy;
        double cs = w.WalkCellM;

        var majors = new List<int>();
        var minors = new List<int>();
        for (int a = 0; a < sites.Count; a++)
            (sites[a].Major ? majors : minors).Add(a);
        var (majorSec, majorFrom) = w.WalkFrom(majors);
        var (minorSec, minorFrom) = w.WalkFrom(minors);

        _area = new int[n];
        Array.Fill(_area, -1);
        for (int i = 0; i < n; i++)
        {
            if (s.Land[i] == 0)
                continue;
            double x = (i % _nx + 0.5) * _cell, y = (i / _nx + 0.5) * _cell;
            int c = Math.Clamp((int)Math.Floor(y / cs), 0, cny - 1) * cnx + Math.Clamp((int)Math.Floor(x / cs), 0, cnx - 1);
            double tMajor = majorSec[c], tMinor = 2 * minorSec[c];
            if (double.IsPositiveInfinity(tMajor) && double.IsPositiveInfinity(tMinor))
            {
                int best = -1;
                double bd = double.PositiveInfinity;
                for (int a = 0; a < sites.Count; a++)
                {
                    double d2 = (sites[a].X - x) * (sites[a].X - x) + (sites[a].Y - y) * (sites[a].Y - y);
                    if (d2 < bd) { bd = d2; best = a; }
                }
                _area[i] = best;
            }
            else
            {
                _area[i] = tMajor <= tMinor ? majorFrom[c] : minorFrom[c];
            }
            // a facility's footprint (its compound's square) is always its own; round some kinds, a guaranteed radius
            int footprint = -1, held = -1;
            double fd = double.PositiveInfinity, hd = double.PositiveInfinity;
            for (int a = 0; a < sites.Count; a++)
            {
                double dx = sites[a].X - x, dy = sites[a].Y - y, d2 = dx * dx + dy * dy;
                if (Math.Max(Math.Abs(dx), Math.Abs(dy)) <= sites[a].R && d2 < fd) { footprint = a; fd = d2; }
                if (MinRadiusM.TryGetValue(sites[a].Kind, out double r) && d2 <= r * r && d2 < hd) { held = a; hd = d2; }
            }
            if (footprint >= 0) _area[i] = footprint;
            else if (held >= 0) _area[i] = held;
        }

        _sheet = new sbyte[n];
        Array.Fill(_sheet, (sbyte)-1);
        _facilitySheet = new int[sites.Count];
        _order = new int[sites.Count][];
        _cells = new List<int>[sites.Count];
        _sheetCells = new List<int>[sites.Count][];
        for (int a = 0; a < sites.Count; a++)
        {
            _cells[a] = new List<int>();
            _sheetCells[a] = new List<int>[SheetCount];
            for (int k = 0; k < SheetCount; k++)
                _sheetCells[a][k] = new List<int>();
        }
        for (int i = 0; i < n; i++)
        {
            if (_area[i] >= 0)
                _cells[_area[i]].Add(i);
        }
        uint stream = GenHash.Stream(unchecked((uint)world.Params.Seed), "parkmap.sheets");
        for (int a = 0; a < sites.Count; a++)
        {
            Site t = sites[a];
            var pie = new List<(int I, double Angle, double D2)>(_cells[a].Count);
            foreach (int i in _cells[a])
            {
                double dx = (i % _nx + 0.5) * _cell - t.X, dy = (i / _nx + 0.5) * _cell - t.Y;
                pie.Add((i, GenMath.Atan2(dy, dx), dx * dx + dy * dy));
            }
            pie.Sort((p, q) => p.Angle != q.Angle ? p.Angle.CompareTo(q.Angle) : p.D2 != q.D2 ? p.D2.CompareTo(q.D2) : p.I.CompareTo(q.I));
            int count = pie.Count;
            for (int k = 0; k < SheetCount; k++)
            {
                for (int j = k * count / SheetCount; j < (k + 1) * count / SheetCount; j++)
                    _sheet[pie[j].I] = (sbyte)k;
            }
            int fs = -1, bi = -1;
            double best = double.PositiveInfinity;
            foreach (var c in pie)
            {
                if (c.D2 < best || (c.D2 == best && c.I < bi)) { best = c.D2; bi = c.I; fs = _sheet[c.I]; }
            }
            _facilitySheet[a] = fs;
            var rest = new List<(int K, uint H)>();
            for (int k = 0; k < SheetCount; k++)
            {
                if (k != fs)
                    rest.Add((k, GenHash.Hash2(stream, a, k)));
            }
            rest.Sort((p, q) => p.H != q.H ? p.H.CompareTo(q.H) : p.K.CompareTo(q.K));
            _order[a] = (fs >= 0 ? new[] { fs } : Array.Empty<int>()).Concat(rest.Select(r => r.K)).ToArray();
        }
        for (int i = 0; i < n; i++)
        {
            if (_area[i] >= 0 && _sheet[i] >= 0)
                _sheetCells[_area[i]][_sheet[i]].Add(i);
        }
    }

    /// <summary>Divides the island. <see cref="IslandWorld.Areas"/> keeps the result.</summary>
    public static FacilityAreas Build(IslandWorld world) => new(world);

    /// <summary>How many areas: one per site, indexed like <see cref="IslandWorks.Sites"/> (an area may be empty).</summary>
    public int Count => _cells.Length;

    public int Nx => _nx;
    public int Ny => _ny;

    /// <summary>The island-map cell holding a spot in island metres, clamped to the grid.</summary>
    public int CellAt(double x, double y) =>
        Math.Clamp((int)Math.Floor(y / _cell), 0, _ny - 1) * _nx + Math.Clamp((int)Math.Floor(x / _cell), 0, _nx - 1);

    /// <summary>The site whose area a cell lies in, or −1 at sea.</summary>
    public int AreaOf(int cell) => _area[cell];

    /// <summary>The site whose area a spot (island metres) lies in, or −1 at sea.</summary>
    public int AreaAt(double x, double y) => _area[CellAt(x, y)];

    /// <summary>Which of its area's sheets (0–9) a cell is in, or −1 at sea.</summary>
    public int SheetOf(int cell) => _sheet[cell];

    /// <summary>An area's cells, in order.</summary>
    public IReadOnlyList<int> CellsOf(int site) => _cells[site];

    /// <summary>One sheet's cells, in order.</summary>
    public IReadOnlyList<int> SheetCells(int site, int sheet) => _sheetCells[site][sheet];

    /// <summary>The sheet holding the facility itself (−1 for an empty area).</summary>
    public int FacilitySheet(int site) => _facilitySheet[site];

    /// <summary>
    /// The order park maps of an area take their sheets: the sheet holding the facility first, then the
    /// others in an order shuffled per island. An eleventh map repeats the first.
    /// </summary>
    public IReadOnlyList<int> SheetOrder(int site) => _order[site];
}

/// <summary>A park map's place on the island: the area (a site index) and the sheet it charts, kept in the item's tag.</summary>
public static class ParkMaps
{
    public static string Tag(int area, int sheet) => $"area={area};sheet={sheet}";

    public static bool TryParse(string? tag, out int area, out int sheet)
    {
        area = sheet = -1;
        if (tag is null || !tag.StartsWith("area=", StringComparison.Ordinal))
            return false;
        string[] parts = tag.Split(';');
        return parts.Length == 2
            && int.TryParse(parts[0].AsSpan(5), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out area)
            && parts[1].StartsWith("sheet=", StringComparison.Ordinal)
            && int.TryParse(parts[1].AsSpan(6), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out sheet);
    }

    /// <summary>The chance (%) of finding something marked on a park map on its first read, by Tracking level 0–3.</summary>
    public static readonly int[] ReadFindPercent = [10, 25, 45, 70];

    /// <summary>The chance for a Tracking level, clamped to 0–3.</summary>
    public static int FindPercent(int tracking) => ReadFindPercent[Math.Clamp(tracking, 0, ReadFindPercent.Length - 1)];

    /// <summary>The roll (0–99) for a reader's first read of a map, addressed by the run's seed, the map and the reader; below <see cref="FindPercent"/> finds something.</summary>
    public static int ReadRoll(ulong runSeed, int mapItemId, int readerId) => (int)(ReadHash(runSeed, mapItemId, readerId) % 100);

    /// <summary>The hash behind <see cref="ReadRoll"/>; the pick among what the map can turn up hashes on from it.</summary>
    public static ulong ReadHash(ulong runSeed, int mapItemId, int readerId) =>
        new Core.StateHasher().Add(runSeed).Add("park-map.read").Add(mapItemId).Add(readerId).Value;
}
