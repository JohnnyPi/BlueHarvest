namespace BlueHarvest.Sim.World.Generation;

public sealed record Corporation(string Name, string Goal);

/// <summary>A kind of facility. Placed in table order: the objective anchors (docks, airstrip, comms, control) first.</summary>
public sealed record FacilityType(string Key, string Name, string Icon, int Count, int Corp, double Apart = 0, bool Major = false);

public sealed record Runway(double X1, double Y1, double X2, double Y2, double H);

/// <summary>A placed facility.</summary>
public sealed class Site
{
    public required int Id { get; init; }
    public required string Kind { get; init; }
    public required string Icon { get; init; }
    public required string KindName { get; init; }
    public required double X { get; init; }
    public required double Y { get; init; }
    /// <summary>Compound radius, m.</summary>
    public required int R { get; init; }
    public required double H { get; set; }
    public required Region Region { get; init; }
    public required string Owner { get; init; }
    public required string OwnerGoal { get; init; }
    public required bool Major { get; init; }
    public string Name { get; set; } = "";
    /// <summary>Docks: the outward coast normal.</summary>
    public (double X, double Y)? Dir { get; set; }
    public Runway? Runway { get; set; }
}

public enum FenceKind : byte { Low, Standard, Heavy }

/// <summary>A paddock species: size in km² at full island scale, the ground it wants, fence and extras.</summary>
public sealed record PaddockSpecies(string Key, string Name, string Occupant, double Km2, IReadOnlyDictionary<Biome, double> Biomes,
    double LakeWeight, bool Water, FenceKind Fence, bool Moat, bool Wallow);

public sealed class Paddock
{
    public required int Id { get; init; }
    public required PaddockSpecies Species { get; init; }
    public required double X { get; init; }
    public required double Y { get; init; }
    /// <summary>The fence line, closed, as x0, y0, x1, y1, … in metres.</summary>
    public required double[] Poly { get; init; }
    public required double Km2 { get; init; }
    public required Biome Biome { get; init; }
    /// <summary>The fence vertex nearest the genetics complex, and the outward direction there.</summary>
    public required (double X, double Y) Gate { get; init; }
    public required (double X, double Y) Out { get; init; }
    public (double X, double Y)? Wallow { get; init; }
    public required bool HasLake { get; init; }
    /// <summary>Heavy fences: the second fence line 5 m inside.</summary>
    public double[]? Inner { get; set; }
    public string Key => Species.Key;
    public string Name => Species.Name;
}

public sealed record PowerLine(double X1, double Y1, double X2, double Y2, string From, string To);

public sealed record LeyLine(double X1, double Y1, double X2, double Y2);

/// <summary>
/// Stage 4 of island generation: the works of people. Stage 4a: facilities, species paddocks,
/// power lines and the compounds' buildings.
/// </summary>
public sealed class IslandWorks
{
    public static readonly Corporation[] Corps =
    [
        new("Joint Operations Authority", "shared"),
        new("Halcyon Genomics", "experimental"),
        new("Ostrander Power & Light", "extractive"),
        new("Vantage Survey Group", "expansionist"),
        new("Kestrel Maritime", "isolationist"),
        new("Meridian Atmospherics", "preservationist"),
    ];

    public static readonly FacilityType[] Facilities =
    [
        new("docks", "Docks", "D", 2, 4, Apart: 12000, Major: true),
        new("airstrip", "Airstrip", "A", 1, 3, Major: true),
        new("comms", "Comms relay", "C", 1, 3),
        new("control", "Control centre", "H", 1, 0, Major: true),
        new("genetics", "Genetics complex", "X", 1, 1, Major: true),
        new("raptors", "Velociraptor pen", "V", 1, 1),
        new("nuclear", "Nuclear plant", "N", 1, 2, Major: true),
        new("geothermal", "Geothermal plant", "G", 1, 2, Major: true),
        new("weather", "Weather control array", "W", 1, 5),
        new("orgone", "Orgone research station", "O", 1, 5),
        new("lab", "Research lab", "R", 2, 1, Apart: 6000, Major: true),
        new("depot", "Supply depot", "S", 2, 4, Apart: 6000, Major: true),
        new("ley", "Ley-line survey station", "L", 3, 3),
        new("watch", "Cordon watch post", "P", 3, 4, Apart: 10000),
    ];

    public static readonly PaddockSpecies[] PaddockSpeciesList =
    [
        new("apato", "Apatosaurus paddock", "Apatosaurus herd", 9,
            new Dictionary<Biome, double> { [Biome.Grassland] = 1, [Biome.Mesic] = 0.9, [Biome.Moist] = 0.5, [Biome.Marsh] = 0.6 }, 0.7, true, FenceKind.Low, false, false),
        new("rex", "Tyrannosaur paddock", "Tyrannosaurus rex", 2.2,
            new Dictionary<Biome, double> { [Biome.Mesic] = 1, [Biome.Moist] = 0.9, [Biome.Grassland] = 0.8, [Biome.Wet] = 0.6 }, 0, false, FenceKind.Heavy, true, false),
        new("trike", "Triceratops paddock", "Triceratops herd", 1.4,
            new Dictionary<Biome, double> { [Biome.Grassland] = 1, [Biome.DryScrub] = 0.8, [Biome.Mesic] = 0.7 }, 0, false, FenceKind.Standard, false, true),
        new("stego", "Stegosaurus paddock", "Stegosaurus herd", 1.4,
            new Dictionary<Biome, double> { [Biome.Mesic] = 1, [Biome.Moist] = 0.8, [Biome.Grassland] = 0.6 }, 0, false, FenceKind.Standard, false, false),
    ];

    private static readonly string[] Dirs8 = ["east", "south-east", "south", "south-west", "west", "north-west", "north", "north-east"];

    private readonly IslandShape _w;
    private readonly IslandClimate _c;
    private readonly int _nx, _ny, _n;
    private readonly double _cell, _area, _s, _sq;
    private readonly int[] _lblM;
    private readonly int _mainL;

    public List<Site> Sites { get; } = [];
    public List<LeyLine> LeyLines { get; } = [];
    public List<Paddock> Paddocks { get; } = [];
    /// <summary>Paddock id of each cell, −1 outside.</summary>
    public short[] PaddockCell { get; }
    /// <summary>Cells next to a river.</summary>
    public byte[] NearRiver { get; }
    public List<PowerLine> Power { get; } = [];
    public double PowerKm { get; private set; }
    /// <summary>Names of buildings, rooms and features; primitives refer to them by index.</summary>
    public List<string> Labels { get; } = [];
    public PrimList Prims { get; } = new();
    /// <summary>Centre of the land, for compass names.</summary>
    public double CenterX { get; private set; }
    public double CenterY { get; private set; }

    private IslandWorks(IslandShape w, IslandClimate c)
    {
        _w = w; _c = c;
        _nx = w.Nx; _ny = w.Ny; _n = w.N; _cell = w.Cell; _area = _cell * _cell;
        _s = w.Anchors.S; _sq = Math.Sqrt(_s);
        PaddockCell = new short[_n];
        Array.Fill(PaddockCell, (short)-1);
        NearRiver = new byte[_n];
        // facilities go on the main island only (islets are for wrecks, stacks and later set pieces)
        _lblM = new int[_n];
        List<int> sizes = GenGrid.Components(w.Land, _nx, _ny, _lblM);
        for (int k = 0; k < sizes.Count; k++) if (sizes[k] > sizes[_mainL]) _mainL = k;
    }

    public static IslandWorks Compute(IslandShape w, IslandClimate c)
    {
        var works = new IslandWorks(w, c);
        works.PlaceFacilities();
        works.FencePaddocks();
        works.StringPower();
        works.BuildCompounds();
        return works;
    }

    private double CxOf(int i) => (i % _nx + 0.5) * _cell;
    private double CyOf(int i) => (i / _nx + 0.5) * _cell;

    private int Idx(double x, double y) =>
        (int)GenMath.Clamp(Math.Floor(y / _cell), 0, _ny - 1) * _nx + (int)GenMath.Clamp(Math.Floor(x / _cell), 0, _nx - 1);

    private bool Dry(int i) => _w.Land[i] != 0 && float.IsNaN(_w.LakeLevel[i]);

    private bool OnMain(int i) => _lblM[i] == _mainL && Dry(i);

    private string Compass(double x, double y)
    {
        double a = GenMath.Atan2(y - CenterY, x - CenterX);
        int k = (int)GenMath.Round(a / (GenMath.Pi / 4));
        return Dirs8[((k % 8) + 8) % 8];
    }

    private bool TooClose(double x, double y, double rad, string kind, double apart)
    {
        foreach (Site t in Sites)
        {
            double d = GenMath.Hyp(t.X - x, t.Y - y);
            double lim = kind == "raptors" || t.Kind == "raptors" ? rad + t.R + 150 : Math.Max(2500 * _s, rad + t.R + 600);
            if (d < lim || (kind == t.Kind && apart != 0 && d < apart * _s)) return true;
        }
        return false;
    }

    private Site AddSite(FacilityType f, double x, double y)
    {
        int i = Idx(x, y);
        Corporation corp = Corps[f.Corp];
        var t = new Site
        {
            Id = Sites.Count, Kind = f.Key, Icon = f.Icon, KindName = f.Name, X = GenMath.Round(x), Y = GenMath.Round(y),
            R = FacilityPrograms.Layout(f.Key).R, H = Math.Max(2, _w.Height[i]), Region = (Region)_c.Regions[i],
            Owner = corp.Name, OwnerGoal = corp.Goal, Major = f.Major,
        };
        Sites.Add(t);
        return t;
    }

    // ---------- facilities ----------
    private void PlaceFacilities()
    {
        IslandShape w = _w;
        IslandClimate c = _c;
        IslandParams p = w.Params;
        IslandAnchors a = w.Anchors;
        int nx = _nx, ny = _ny, n = _n;
        double s = _s;
        double[] h = w.Height, rain = w.Rain;
        byte[] land = w.Land, coast = w.Coast, region = c.Regions, biome = c.Biomes;
        float[] slopeD = c.SlopeDeg, coastEx = w.CoastExposure, crest = c.Crest, distLand = w.DistLand;

        // island centre (for compass names) and a seaward-openness map for headlands
        double mx = 0, my = 0;
        int mc = 0;
        for (int i = 0; i < n; i++) if (land[i] != 0) { mx += CxOf(i); my += CyOf(i); mc++; }
        mx /= mc; my /= mc;
        CenterX = mx; CenterY = my;
        var seaFrac = new double[n];
        for (int i = 0; i < n; i++) seaFrac[i] = land[i] != 0 ? 0 : 1;
        GenGrid.BoxBlur(seaFrac, nx, ny, Math.Max(1, (int)GenMath.Round(1500 / _cell)), 1);

        byte[] nearRiver = NearRiver; // within one cell of a river
        for (int i = 0; i < n; i++)
        {
            if (w.IsRiver[i] == 0) continue;
            int x = i % nx, y = i / nx;
            for (int k = 0; k < 8; k++)
            {
                int xx = x + GenGrid.DX[k], yy = y + GenGrid.DY[k];
                if (xx >= 0 && yy >= 0 && xx < nx && yy < ny) nearRiver[yy * nx + xx] = 1;
            }
        }

        const double No = -1e9;
        byte rgLeeward = (byte)Region.Leeward, rgCoast = (byte)Region.Coast, rgPlateau = (byte)Region.Plateau, rgWindward = (byte)Region.Windward, rgRidge = (byte)Region.Ridge;
        Func<int, double> Score(string key) => key switch
        {
            "docks" => i => coast[i] != 0 && coast[i] != CoastClass.Cliff && slopeD[i] < 6
                ? 2 - coastEx[i] * 1.5 + (nearRiver[i] != 0 ? 0.4 : 0) + (region[i] == rgLeeward ? 0.3 : 0) : No,
            "comms" => i => slopeD[i] < 30 && biome[i] != (byte)Biome.Cliff ? h[i] / 300 + crest[i] : No,
            "control" => i => slopeD[i] < 5 && region[i] != rgCoast
                ? 3 - GenMath.Hyp(CxOf(i) - mx, CyOf(i) - my) / (6000 * s) - slopeD[i] / 6 + (region[i] == rgPlateau ? 1 : 0) : No,
            "genetics" => i => slopeD[i] < 8 && h[i] > 30 && h[i] < 450 && rain[i] > 1400 && region[i] != rgCoast
                ? 2 + Math.Min(1, rain[i] / 4000) - slopeD[i] / 10 + (region[i] == rgWindward ? 0.6 : 0) : No,
            "nuclear" => i => distLand[i] < 700 && slopeD[i] < 4 && h[i] < 40 && GenMath.Hyp(CxOf(i) - a.Vent.X, CyOf(i) - a.Vent.Y) > 6000 * s
                ? 2 - coastEx[i] - slopeD[i] / 4 + (region[i] == rgLeeward ? 0.5 : 0) : No,
            "geothermal" => i =>
            {
                double d = GenMath.Hyp(CxOf(i) - a.Vent.X, CyOf(i) - a.Vent.Y) / a.Vent.R;
                return d > 1.1 && d < 3 && slopeD[i] < 10 ? 3 - Math.Abs(d - 1.6) - slopeD[i] / 10 : No;
            },
            "weather" => i => slopeD[i] < 20 && h[i] > 0.55 * p.Peak && region[i] == rgRidge ? h[i] / 400 + crest[i] : No,
            "orgone" => i =>
            {
                double d = GenMath.Hyp(CxOf(i) - a.Caldera.X, CyOf(i) - a.Caldera.Y) / a.Caldera.R;
                return d < 1.3 && slopeD[i] < 14 ? 3 - Math.Abs(d - 0.7) : No;
            },
            "lab" => i => slopeD[i] < 8 && h[i] < 300 && region[i] != rgCoast ? 2 - slopeD[i] / 8 + (region[i] == rgLeeward ? 0.7 : 0) : No,
            "depot" => i => slopeD[i] < 6 && h[i] < 120 && distLand[i] < 3000 ? 2 - slopeD[i] / 6 + (region[i] == rgLeeward ? 0.4 : 0) : No,
            "raptors" => i =>
            {
                Site? g0 = Sites.Find(t => t.Kind == "genetics");
                if (g0 == null) return No;
                double d = GenMath.Hyp(CxOf(i) - g0.X, CyOf(i) - g0.Y);
                return d > g0.R + 250 && d < 1600 && slopeD[i] < 10 ? 3 - d / 1600 - slopeD[i] / 10 : No;
            },
            "watch" => i => land[i] != 0 && distLand[i] < 500 && h[i] > 20 && slopeD[i] < 25 ? seaFrac[i] * 3 + Math.Min(1, h[i] / 150) : No,
            _ => throw new ArgumentOutOfRangeException(nameof(key), key, "no placement score"),
        };

        int PlaceBest(FacilityType f, int k)
        {
            Func<int, double> sc = Score(f.Key);
            uint js = GenHash.Stream(p.Seed, "sites." + f.Key + "." + k);
            int rad = FacilityPrograms.Layout(f.Key).R;
            int best = -1;
            double bs = No;
            for (int i = 0; i < n; i++)
            {
                if (!OnMain(i)) continue;
                double v = sc(i);
                if (v < -1e8) continue;
                v += 0.6 * GenHash.Hash2(js, i, 7) / 4294967296.0;
                if (v > bs && !TooClose(CxOf(i), CyOf(i), rad, f.Key, f.Apart)) { bs = v; best = i; }
            }
            return best;
        }

        // airstrip: the flattest straight 1.4 km on low ground
        void PlaceAirstrip(FacilityType f)
        {
            double len = 1400 * Math.Min(1, 0.45 + s * 0.55), half = len / 2;
            bool found = false;
            double bx = 0, by = 0, bdx = 0, bdy = 0, bs = No;
            uint js = GenHash.Stream(p.Seed, "sites.airstrip");
            for (int i = 0; i < n; i += 2)
            {
                if (!OnMain(i) || slopeD[i] > 3 || h[i] > 400 || biome[i] == (byte)Biome.Marsh) continue;
                double x = CxOf(i), y = CyOf(i);
                if (TooClose(x, y, half, f.Key, 0)) continue;
                for (int ai = 0; ai < 8; ai++)
                {
                    double ang = ai * GenMath.Pi / 8, dx = GenMath.Cos(ang), dy = GenMath.Sin(ang);
                    double dev = 0;
                    bool ok = true;
                    for (double t = -half; t <= half; t += _cell)
                    {
                        int j = Idx(x + dx * t, y + dy * t);
                        if (!Dry(j) || slopeD[j] > 5) { ok = false; break; }
                        dev = Math.Max(dev, Math.Abs(h[j] - h[i]));
                    }
                    if (!ok) continue;
                    double v = 3 - dev / 10 + (region[i] == rgPlateau ? 0.6 : 0) + 0.5 * GenHash.Hash2(js, i, ai) / 4294967296.0;
                    if (v > bs) { bs = v; found = true; bx = x; by = y; bdx = dx; bdy = dy; }
                }
            }
            if (!found) return;
            // the terminal compound sits beside the runway, not on it
            double off = 22.5 + FacilityPrograms.Layout("airstrip").R + 15;
            Site t0 = AddSite(f, bx - bdy * off, by + bdx * off);
            int i1 = Idx(bx - bdx * half, by - bdy * half), i2 = Idx(bx + bdx * half, by + bdy * half);
            var rw = new Runway(bx - bdx * half, by - bdy * half, bx + bdx * half, by + bdy * half, (h[i1] + h[i2] + t0.H) / 3);
            t0.Runway = rw;
            t0.H = rw.H;
        }

        // ley lines: straight lines that the herds follow; survey stations sit where they cross
        var landCells = new List<int>();
        for (int i = 0; i < n; i += 3) if (land[i] != 0) landCells.Add(i);
        double big = a.W + a.H;
        void LineThrough(double ax, double ay, double bx, double by)
        {
            double d = GenMath.Hyp(bx - ax, by - ay);
            if (d == 0) d = 1;
            double dx = (bx - ax) / d, dy = (by - ay) / d;
            LeyLines.Add(new LeyLine(ax - dx * big, ay - dy * big, ax + dx * big, ay + dy * big));
        }

        foreach (FacilityType f in Facilities)
        {
            if (f.Key == "airstrip") { PlaceAirstrip(f); continue; }
            if (f.Key == "ley")
            {
                // pick the three crossings first, on the main island and clear of other sites, then draw the lines
                // through them as a triangle (so every crossing is on land), plus a fourth line for the herds
                var rl = new GenRng(p.Seed, "worldgen.ley");
                var pts = new List<(double X, double Y)>();
                int leyR = FacilityPrograms.Layout("ley").R;
                for (int tries = 0; tries < 4000 && pts.Count < f.Count; tries++)
                {
                    int i = landCells[(int)Math.Floor(rl.Next() * landCells.Count)];
                    if (!OnMain(i) || slopeD[i] >= 18) continue;
                    double x = CxOf(i), y = CyOf(i);
                    if (TooClose(x, y, leyR, f.Key, 0) || pts.Exists(q => GenMath.Hyp(q.X - x, q.Y - y) < 6000 * s)) continue;
                    if (pts.Count == 2)
                    {
                        // not too flat a triangle
                        (double X, double Y) pa = pts[0], pb = pts[1];
                        double ab = GenMath.Hyp(pb.X - pa.X, pb.Y - pa.Y), area2 = Math.Abs((pb.X - pa.X) * (y - pa.Y) - (pb.Y - pa.Y) * (x - pa.X));
                        if (area2 / (ab * ab) < 0.25) continue;
                    }
                    pts.Add((x, y));
                }
                for (int k = 0; k < pts.Count; k++)
                {
                    (double X, double Y) pa = pts[k], pb = pts[(k + 1) % pts.Count];
                    if (pts.Count > 1 && (pts.Count > 2 || k == 0)) LineThrough(pa.X, pa.Y, pb.X, pb.Y);
                }
                int i4 = landCells[(int)Math.Floor(rl.Next() * landCells.Count)];
                double ang = rl.Next() * GenMath.Pi;
                LineThrough(CxOf(i4), CyOf(i4), CxOf(i4) + GenMath.Cos(ang), CyOf(i4) + GenMath.Sin(ang));
                foreach ((double x, double y) in pts) AddSite(f, x, y);
                continue;
            }
            for (int k = 0; k < f.Count; k++)
            {
                int i = PlaceBest(f, k);
                if (i < 0) break;
                AddSite(f, CxOf(i), CyOf(i));
            }
        }

        // names: add a compass word where a kind repeats
        var cnt = new Dictionary<string, int>();
        foreach (Site t in Sites) cnt[t.Kind] = cnt.GetValueOrDefault(t.Kind) + 1;
        foreach (Site t in Sites)
        {
            t.Name = cnt[t.Kind] > 1 ? t.KindName + ", " + Compass(t.X, t.Y) : t.KindName;
            int same = Sites.Count(u => u != t && u.Name == t.Name && u.Id < t.Id);
            if (same > 0) t.Name += " " + (same + 1);
            if (t.Kind == "docks")
            {
                int i = Idx(t.X, t.Y);
                t.Dir = (w.CoastNx[i], w.CoastNy[i]);
            }
        }
    }

    // ---------- species paddocks, grown over suitable ground to each species' size ----------
    private void FencePaddocks()
    {
        IslandShape w = _w;
        IslandClimate c = _c;
        IslandParams p = w.Params;
        int nx = _nx, ny = _ny, n = _n;
        double cell = _cell, area = _area, s = _s, sq = _sq;
        byte[] land = w.Land, region = c.Regions, biome = c.Biomes;
        float[] slopeD = c.SlopeDeg, lake = w.LakeLevel;
        Site? gen = Sites.Find(t => t.Kind == "genetics");
        if (gen == null || !(p.PaddockSize > 0)) return;
        var blocked = new byte[n];
        foreach (Site t in Sites)
        {
            int rr = (int)Math.Ceiling((t.R + 200) / cell), x0 = (int)Math.Floor(t.X / cell), y0 = (int)Math.Floor(t.Y / cell);
            for (int yy = Math.Max(0, y0 - rr); yy <= Math.Min(ny - 1, y0 + rr); yy++)
                for (int xx = Math.Max(0, x0 - rr); xx <= Math.Min(nx - 1, x0 + rr); xx++) blocked[yy * nx + xx] = 1;
        }
        var ph = new GenHeap(n * 4);
        uint js = GenHash.Stream(p.Seed, "paddock.seed");
        foreach (PaddockSpecies sp in PaddockSpeciesList)
        {
            double target = Math.Max(4, sp.Km2 * p.PaddockSize * s * 1e6 / area);
            var suit = new double[n];
            for (int i = 0; i < n; i++)
            {
                if (land[i] == 0 || blocked[i] != 0 || region[i] == (byte)Region.Coast || region[i] == (byte)Region.Vent || region[i] == (byte)Region.Crater) continue;
                if (!float.IsNaN(lake[i])) { suit[i] = sp.LakeWeight; continue; }
                if (slopeD[i] > 28) continue;
                double v = sp.Biomes.GetValueOrDefault((Biome)biome[i]);
                if (sp.Water && (w.IsRiver[i] != 0 || NearRiver[i] != 0)) v += 0.4;
                suit[i] = v;
            }
            var blur = (double[])suit.Clone();
            GenGrid.BoxBlur(blur, nx, ny, Math.Max(1, (int)GenMath.Round(Math.Sqrt(target) / 2)), 1);
            int seed = -1;
            double bs = -1e9;
            for (int i = 0; i < n; i++)
            {
                if (!(suit[i] > 0.5)) continue;
                double v = blur[i] - 0.05 * GenMath.Hyp(CxOf(i) - gen.X, CyOf(i) - gen.Y) / (1000 * sq) + 0.08 * GenHash.Hash2(js, i, sp.Key.Length) / 4294967296.0;
                if (v > bs) { bs = v; seed = i; }
            }
            if (seed < 0) continue;
            // best-first growth: good ground first, staying compact around the seed
            var inP = new byte[n];
            int sx0 = seed % nx, sy0 = seed / nx;
            double rad = Math.Sqrt(target / GenMath.Pi);
            ph.Clear();
            ph.Push(0, seed);
            int count = 0;
            while (ph.Count > 0 && count < target)
            {
                int cc = ph.Pop();
                if (inP[cc] != 0) continue;
                inP[cc] = 1; count++;
                int x = cc % nx, y = cc / nx;
                for (int k = 0; k < 8; k += 2)
                {
                    int xx = x + GenGrid.DX[k], yy = y + GenGrid.DY[k];
                    if (xx < 0 || yy < 0 || xx >= nx || yy >= ny) continue;
                    int j = yy * nx + xx;
                    if (inP[j] != 0 || suit[j] < 0.25) continue;
                    ph.Push((1 - suit[j]) + 0.9 * GenMath.Hyp(xx - sx0, yy - sy0) / rad, j);
                }
            }
            if (count < target * 0.4) continue;
            int id = Paddocks.Count;
            for (int i = 0; i < n; i++)
            {
                if (inP[i] == 0) continue;
                PaddockCell[i] = (short)id;
                int x = i % nx, y = i / nx;
                for (int k = 0; k < 8; k++)
                {
                    int xx = x + GenGrid.DX[k], yy = y + GenGrid.DY[k];
                    if (xx >= 0 && yy >= 0 && xx < nx && yy < ny) blocked[yy * nx + xx] = 1;
                }
            }
            double[] poly = TraceMask(inP, nx, ny, cell);
            double pcx = 0, pcy = 0;
            for (int v = 0; v < poly.Length; v += 2) { pcx += poly[v]; pcy += poly[v + 1]; }
            pcx /= poly.Length / 2; pcy /= poly.Length / 2;
            int bv = 0;
            double bd = 1e30;
            for (int v = 0; v < poly.Length; v += 2)
            {
                double d = GenMath.Hyp(poly[v] - gen.X, poly[v + 1] - gen.Y);
                if (d < bd) { bd = d; bv = v; }
            }
            (double X, double Y) gate = (poly[bv], poly[bv + 1]);
            double gl = GenMath.Hyp(gate.X - pcx, gate.Y - pcy);
            if (gl == 0) gl = 1;
            (double X, double Y) outDir = ((gate.X - pcx) / gl, (gate.Y - pcy) / gl);
            // a point well inside, for the wallow
            int inner = seed;
            double ib = -1;
            for (int i = 0; i < n; i++)
            {
                if (inP[i] == 0 || !float.IsNaN(lake[i])) continue;
                double e = 1e9;
                int x = i % nx, y = i / nx;
                for (int k = 0; k < 8; k++)
                {
                    int xx = x + GenGrid.DX[k] * 2, yy = y + GenGrid.DY[k] * 2;
                    if (xx < 0 || yy < 0 || xx >= nx || yy >= ny || inP[yy * nx + xx] == 0) e = 0;
                }
                if (e > ib)
                {
                    ib = e; inner = i;
                    if (e > 0) break;
                }
            }
            bool hasLake = false;
            for (int i = 0; i < n; i++) if (inP[i] != 0 && !float.IsNaN(lake[i])) { hasLake = true; break; }
            Paddocks.Add(new Paddock
            {
                Id = id, Species = sp, X = pcx, Y = pcy, Poly = poly, Km2 = count * area / 1e6, Biome = (Biome)biome[seed],
                Gate = gate, Out = outDir, Wallow = sp.Wallow ? (CxOf(inner), CyOf(inner)) : null, HasLake = hasLake,
            });
        }
    }

    /// <summary>
    /// Traces the outline of a cell mask into a closed polygon (the longest loop) in metres:
    /// collinear points dropped, then three rounds of closed Chaikin smoothing.
    /// </summary>
    public static double[] TraceMask(byte[] mask, int nx, int ny, double cell)
    {
        // boundary edges keyed by their start vertex, in insertion order; a later edge from the same
        // vertex replaces the earlier one in place (JavaScript Map semantics)
        var keyIndex = new Dictionary<long, int>();
        var edges = new List<(long Key, int Bx, int By, int Ax, int Ay)>();
        static long Key(int x, int y) => x * 100003L + y;
        void Edge(int ax, int ay, int bx, int by)
        {
            long k = Key(ax, ay);
            if (keyIndex.TryGetValue(k, out int at)) edges[at] = (k, bx, by, ax, ay);
            else { keyIndex[k] = edges.Count; edges.Add((k, bx, by, ax, ay)); }
        }
        bool Out(int xx, int yy) => xx < 0 || yy < 0 || xx >= nx || yy >= ny || mask[yy * nx + xx] == 0;
        for (int y = 0; y < ny; y++)
        {
            for (int x = 0; x < nx; x++)
            {
                if (mask[y * nx + x] == 0) continue;
                // inside on the left as we walk each edge
                if (Out(x, y - 1)) Edge(x + 1, y, x, y);
                if (Out(x, y + 1)) Edge(x, y + 1, x + 1, y + 1);
                if (Out(x - 1, y)) Edge(x, y, x, y + 1);
                if (Out(x + 1, y)) Edge(x + 1, y + 1, x + 1, y);
            }
        }
        List<(int X, int Y)>? best = null;
        var used = new HashSet<long>();
        foreach (var e0 in edges)
        {
            if (used.Contains(e0.Key)) continue;
            var loop = new List<(int X, int Y)>();
            long k = e0.Key;
            (long Key, int Bx, int By, int Ax, int Ay)? e = e0;
            int guard = 0;
            while (e != null && !used.Contains(k) && guard++ < 200000)
            {
                used.Add(k);
                loop.Add((e.Value.Ax, e.Value.Ay));
                k = Key(e.Value.Bx, e.Value.By);
                e = keyIndex.TryGetValue(k, out int at) ? edges[at] : null;
            }
            if (best == null || loop.Count > best.Count) best = loop;
        }
        if (best == null) return [];
        var pts = new List<(int X, int Y)>();
        for (int i = 0; i < best.Count; i++)
        {
            (int X, int Y) q = best[i], a = best[(i + best.Count - 1) % best.Count], b = best[(i + 1) % best.Count];
            if ((long)(q.X - a.X) * (b.Y - q.Y) != (long)(q.Y - a.Y) * (b.X - q.X)) pts.Add(q);
        }
        var ring = pts.Select(q => (X: q.X * cell, Y: q.Y * cell)).ToList();
        for (int it = 0; it < 3; it++)
        {
            var output = new List<(double X, double Y)>(ring.Count * 2);
            for (int i = 0; i < ring.Count; i++)
            {
                (double X, double Y) a = ring[i], b = ring[(i + 1) % ring.Count];
                output.Add((a.X * 0.75 + b.X * 0.25, a.Y * 0.75 + b.Y * 0.25));
                output.Add((a.X * 0.25 + b.X * 0.75, a.Y * 0.25 + b.Y * 0.75));
            }
            ring = output;
        }
        var flat = new double[ring.Count * 2];
        for (int i = 0; i < ring.Count; i++) { flat[2 * i] = ring[i].X; flat[2 * i + 1] = ring[i].Y; }
        return flat;
    }

    // ---------- power: a tree of straight lines from the generators to every site ----------
    private void StringPower()
    {
        var roots = Sites.Where(t => t.Kind == "nuclear" || t.Kind == "geothermal").ToList();
        var on = new HashSet<int>(roots.Select(t => t.Id));
        if (roots.Count == 0 && Sites.Count > 0) on.Add(Sites[0].Id);
        while (on.Count < Sites.Count)
        {
            Site? ba = null, bb = null;
            double bd = 1e30;
            foreach (Site a in Sites)
            {
                if (!on.Contains(a.Id)) continue;
                foreach (Site b in Sites)
                {
                    if (on.Contains(b.Id)) continue;
                    double d = GenMath.Hyp(a.X - b.X, a.Y - b.Y);
                    if (d < bd) { bd = d; ba = a; bb = b; }
                }
            }
            on.Add(bb!.Id);
            Power.Add(new PowerLine(ba!.X, ba.Y, bb.X, bb.Y, ba.Name, bb.Name));
        }
        // paddock fences hang on the genetics complex's substation
        Site? gen = Sites.Find(t => t.Kind == "genetics");
        if (gen != null)
            foreach (Paddock q in Paddocks)
                Power.Add(new PowerLine(gen.X, gen.Y, q.Gate.X, q.Gate.Y, gen.Name, q.Name));
        double km = 0;
        foreach (PowerLine l in Power) km += GenMath.Hyp(l.X2 - l.X1, l.Y2 - l.Y1) / 1000;
        PowerKm = km;
    }

    // ---------- compounds: each facility built from its programme ----------
    private void BuildCompounds()
    {
        List<string> labels = Labels;
        PrimList prims = Prims;
        foreach (Site t in Sites) FacilityPrograms.BuildCompound(t.X, t.Y, t.Name, t.Kind, labels, prims);
        foreach (Paddock q in Paddocks)
        {
            int vx = (int)GenMath.Round(q.Gate.X + q.Out.X * 30), vy = (int)GenMath.Round(q.Gate.Y + q.Out.Y * 30);
            FacilityPrograms.Building(FacilityPrograms.ViewingStation, vx - 8, vy - 4, vx + 7, vy + 4, q.Name, labels, prims);
            int fx = (int)GenMath.Round(q.Gate.X - q.Out.X * 28), fy = (int)GenMath.Round(q.Gate.Y - q.Out.Y * 28);
            labels.Add(q.Name + " · Feeding platform");
            prims.Add(new Prim { Kind = PrimKind.Rect, X0 = fx - 4, Y0 = fy - 4, X1 = fx + 4, Y1 = fy + 4, Cover = Cover.Floor, Label = labels.Count - 1 });
            if (q.Wallow is { } wl)
            {
                labels.Add(q.Name + " · Mud wallow");
                int l = labels.Count - 1;
                prims.Add(new Prim { Kind = PrimKind.Circle, X = wl.X, Y = wl.Y, R = 16, Cover = Cover.Mud, Label = l });
                prims.Add(new Prim { Kind = PrimKind.Circle, X = wl.X, Y = wl.Y, R = 9, Cover = Cover.Shallow, Label = l });
            }
            if (q.Species.Fence == FenceKind.Heavy)
            {
                // a second fence 5 m inside, and a dry moat where visitors look in
                var inner = new double[q.Poly.Length];
                for (int v = 0; v < q.Poly.Length; v += 2)
                {
                    double dx = q.X - q.Poly[v], dy = q.Y - q.Poly[v + 1], dl = GenMath.Hyp(dx, dy);
                    if (dl == 0) dl = 1;
                    inner[v] = q.Poly[v] + dx / dl * 5;
                    inner[v + 1] = q.Poly[v + 1] + dy / dl * 5;
                }
                q.Inner = inner;
            }
            if (q.Species.Moat)
            {
                labels.Add(q.Name + " · Moat (engineered drop)");
                int l = labels.Count - 1;
                for (int v = 0; v < q.Poly.Length; v += 2)
                {
                    int w2 = (v + 2) % q.Poly.Length;
                    if (GenMath.Hyp(q.Poly[v] - vx, q.Poly[v + 1] - vy) > 180) continue;
                    double mxp = q.Poly[v], myp = q.Poly[v + 1], nxp = q.Poly[w2], nyp = q.Poly[w2 + 1];
                    double dx = q.X - mxp, dy = q.Y - myp, dl = GenMath.Hyp(dx, dy);
                    if (dl == 0) dl = 1;
                    const double o = 12;
                    prims.Add(new Prim
                    {
                        Kind = PrimKind.Line, Ax = mxp + dx / dl * o, Ay = myp + dy / dl * o, Bx = nxp + dx / dl * o, By = nyp + dy / dl * o,
                        W = 8, Cover = Cover.Moat, Depth = 6, Label = l,
                    });
                }
            }
        }
    }
}
