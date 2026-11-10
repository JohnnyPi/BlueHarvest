namespace BlueHarvest.Sim.World.Generation;

public enum CaveKind : byte { LavaTube, SeaCave }

/// <summary>A lava tube (a path downhill from the young flanks) or a sea cave (one point in a cliff).</summary>
public sealed class Cave
{
    public required int Id { get; init; }
    public required CaveKind Kind { get; init; }
    public required string Name { get; init; }
    public required List<double> Xs { get; init; }
    public required List<double> Ys { get; init; }
    /// <summary>Roof thickness, m.</summary>
    public required double Depth { get; init; }
    public required double LengthKm { get; init; }
    /// <summary>Where the cave can be entered.</summary>
    public required List<(double X, double Y)> Mouths { get; init; }
    public required List<(double X, double Y)> Skylights { get; init; }
}

public sealed record WreckEra(string Kind, string Era, int Length);

public sealed record Wreck(int Id, double X, double Y, string Kind, string Era, int Length, double Heading, bool OnReef, bool Beached, bool Inland, string Name);

public sealed record RuinKind(string Key, string Name, string Era, int R);

public sealed record Ruin(int Id, string Kind, double X, double Y, int R, string Era, string Name, double Rot);

/// <summary>
/// A kind of wildlife: habitat weight by biome plus modifiers (0 = not used). PerKm2 is the density
/// where the habitat is perfect.
/// </summary>
public sealed record FaunaKind(string Key, string Name, string Note, double PerKm2, IReadOnlyDictionary<Biome, double> Biomes,
    double People = 0, bool PeopleOnly = false, double Water = 0, double Steep = 0, bool Low = false, double Caves = 0, double Rocky = 0, double Cliffs = 0);

/// <summary>Where a kind of wildlife lives: density 0–255 per cell, and the island's population.</summary>
public sealed record FaunaMap(FaunaKind Kind, byte[] Density, double Population);

public sealed partial class IslandWorks
{
    public static readonly WreckEra[] WreckEras =
    [
        new("three-masted barque", "1790s", 40), new("whaling ship", "1840s", 34), new("steam freighter", "1890s", 70), new("Pacific schooner", "1920s", 30),
        new("navy landing craft", "1940s", 24), new("fishing trawler", "1970s", 26), new("research yacht", "2000s", 18), new("corporate supply barge", "year 4", 45),
    ];

    public static readonly RuinKind[] RuinKinds =
    [
        new("terraces", "Stone-walled terraces", "ancient", 30),
        new("stones", "Ring of standing stones", "ancient", 12),
        new("platform", "Stone platform", "ancient", 16),
        new("chapel", "Mission chapel", "1800s", 10),
        new("mill", "Sugar mill", "1800s", 18),
        new("lighthouse", "Lighthouse", "1890s", 8),
        new("bunker", "Coastal gun bunker", "1940s", 10),
        new("radar", "Radar station", "1950s", 14),
        new("camp", "Abandoned survey camp", "year 2", 14),
        new("fieldlab", "Burned field lab", "year 5", 12),
    ];

    private static Dictionary<Biome, double> Hab(params (Biome B, double W)[] w) => w.ToDictionary(e => e.B, e => e.W);

    public static readonly FaunaKind[] FaunaKinds =
    [
        new("compy", "Compsognathus", "free-roaming everywhere; thickest near people, ruins and forest edges", 40,
            Hab((Biome.Strand, 0.4), (Biome.Marsh, 0.5), (Biome.DryScrub, 0.6), (Biome.Grassland, 0.7), (Biome.Mesic, 1), (Biome.Moist, 1), (Biome.Wet, 0.8), (Biome.Cloud, 0.5), (Biome.Ridge, 0.4), (Biome.Cliff, 0.1), (Biome.Lava, 0.2)), People: 0.8),
        new("pig", "Feral pigs", "forest and woodland near water", 12,
            Hab((Biome.Mesic, 1), (Biome.Moist, 1), (Biome.Wet, 0.8), (Biome.Cloud, 0.5), (Biome.Marsh, 0.6), (Biome.Grassland, 0.3)), Water: 0.5),
        new("goat", "Feral goats", "dry slopes, crests and cliffs", 10,
            Hab((Biome.DryScrub, 1), (Biome.Ridge, 0.9), (Biome.Cliff, 0.8), (Biome.Grassland, 0.5), (Biome.Lava, 0.4)), Steep: 0.6),
        new("fowl", "Jungle fowl", "forest edges everywhere", 60,
            Hab((Biome.Mesic, 1), (Biome.Moist, 0.8), (Biome.Wet, 0.6), (Biome.Grassland, 0.5), (Biome.DryScrub, 0.4), (Biome.Strand, 0.3))),
        new("bat", "Fruit bats", "roost in caves and tall forest, forage at night", 30,
            Hab((Biome.Moist, 1), (Biome.Wet, 1), (Biome.Cloud, 0.6), (Biome.Mesic, 0.5)), Caves: 1),
        new("monitor", "Monitor lizards", "escaped; lowland forest and wetlands", 3,
            Hab((Biome.Marsh, 1), (Biome.Moist, 0.8), (Biome.Mesic, 0.6), (Biome.Strand, 0.4)), Low: true),
        new("toad", "Cane toads", "wetlands and streams", 200,
            Hab((Biome.Marsh, 1), (Biome.Moist, 0.4), (Biome.Mesic, 0.3)), Water: 1),
        new("mongoose", "Mongoose", "dry lowland, around rubbish", 8,
            Hab((Biome.DryScrub, 1), (Biome.Grassland, 0.8), (Biome.Strand, 0.6), (Biome.Mesic, 0.5)), Low: true, People: 0.5),
        new("macaque", "Macaques", "escaped from the labs; forest near facilities", 6,
            Hab((Biome.Moist, 1), (Biome.Wet, 0.9), (Biome.Mesic, 0.7)), People: 1.5, PeopleOnly: true),
        new("turtle", "Sea turtles", "nest on beaches", 20, Hab((Biome.Strand, 1))),
        new("seal", "Monk seals", "haul out on beaches and rocky shores", 4, Hab((Biome.Strand, 0.8)), Rocky: 1),
        new("seabird", "Seabird colonies", "sea cliffs, islets and crests", 150, Hab((Biome.Cliff, 1), (Biome.Ridge, 0.3)), Cliffs: 1),
    ];

    public List<Cave> Caves { get; } = [];
    public List<Wreck> Wrecks { get; } = [];
    public List<Ruin> Ruins { get; } = [];
    public List<FaunaMap> Fauna { get; } = [];

    private static string Capitalise(string s) => char.ToUpperInvariant(s[0]) + s[1..];

    // ---------- caves: lava tubes on the young flanks, sea caves in the cliffs ----------
    private void TraceCaves()
    {
        IslandShape w = _w;
        IslandParams p = w.Params;
        IslandAnchors a = w.Anchors;
        int nx = _nx, ny = _ny, n = _n;
        double cell = _cell, s = _s, sq = _sq;
        double[] h = w.Height;
        float[] h0 = w.Height0, slopeD = _c.SlopeDeg;
        List<int> landCells = _landCells;
        var rc = new GenRng(p.Seed, "worldgen.caves");
        (double X, double Y) Grad(double x, double y)
        {
            double gx = x / cell - 0.5, gy = y / cell - 0.5;
            const double e = 0.5;
            return ((GenGrid.Bilinear(h0, nx, ny, gx + e, gy) - GenGrid.Bilinear(h0, nx, ny, gx - e, gy)) / (2 * e * cell),
                (GenGrid.Bilinear(h0, nx, ny, gx, gy + e) - GenGrid.Bilinear(h0, nx, ny, gx, gy - e)) / (2 * e * cell));
        }
        for (int k = 0, tries = 0; k < p.Tubes && tries < 300; tries++)
        {
            int i = landCells[(int)Math.Floor(rc.Next() * landCells.Count)];
            double vd = GenMath.Hyp(CxOf(i) - a.Vent.X, CyOf(i) - a.Vent.Y) / a.Vent.R;
            if (!(w.OldWeight[i] < 0.45 || vd < 3) || h0[i] < 200 || h0[i] > 950 || slopeD[i] > 25) continue;
            if (Caves.Exists(c => GenMath.Hyp(c.Xs[0] - CxOf(i), c.Ys[0] - CyOf(i)) < 2500 * s)) continue;
            double x = CxOf(i), y = CyOf(i);
            bool hasHeading = false;
            double heading = 0;
            var xs = new List<double> { x };
            var ys = new List<double> { y };
            double maxLen = (3000 + rc.Next() * 7000) * sq, step = Math.Min(cell * 0.6, 60);
            double depth = 5 + rc.Next() * 12;
            uint nsd = GenHash.Stream(p.Seed, "tube." + k);
            double len = 0;
            bool toSea = false;
            while (len < maxLen)
            {
                (double gx, double gy) = Grad(x, y);
                double gl = GenMath.Hyp(gx, gy);
                if (gl < 0.004) break;
                double ang = GenMath.Atan2(-gy, -gx) + 0.7 * GenNoise.Noise(nsd, len / 600, 0.5);
                if (hasHeading)
                {
                    double da = ang - heading;
                    while (da > GenMath.Pi) da -= 2 * GenMath.Pi;
                    while (da < -GenMath.Pi) da += 2 * GenMath.Pi;
                    ang = heading + GenMath.Clamp(da, -0.5, 0.5);
                }
                heading = ang; hasHeading = true;
                x += GenMath.Cos(ang) * step; y += GenMath.Sin(ang) * step; len += step;
                int j = Idx(x, y);
                if (w.Land[j] == 0) { toSea = true; break; }
                xs.Add(x); ys.Add(y);
            }
            if (len < 1500 * sq) continue;
            // where valleys have since cut below the tube's roof, the tube opens
            var mouths = new List<(double X, double Y)>();
            var skylights = new List<(double X, double Y)>();
            bool open = false;
            int every = Math.Max(4, (int)GenMath.Round(1300 / step));
            for (int m = 0; m < xs.Count; m++)
            {
                int j = Idx(xs[m], ys[m]);
                bool breached = h0[j] - h[j] > depth;
                if (breached != open)
                {
                    if (mouths.Count == 0 || GenMath.Hyp(mouths[^1].X - xs[m], mouths[^1].Y - ys[m]) > 300) mouths.Add((xs[m], ys[m]));
                    open = breached;
                }
                if (m > 0 && m % every == 0 && !breached && rc.Next() < 0.7) skylights.Add((xs[m], ys[m]));
            }
            if (toSea) mouths.Add((xs[^1], ys[^1]));
            if (mouths.Count == 0) mouths.Add((xs[0], ys[0])); // a collapse at the head
            Caves.Add(new Cave
            {
                Id = Caves.Count, Kind = CaveKind.LavaTube, Name = "Lava tube " + (Caves.Count + 1), Xs = xs, Ys = ys, Depth = depth,
                LengthKm = len / 1000, Mouths = mouths, Skylights = skylights,
            });
            k++;
        }
        var cliffCells = new List<int>();
        for (int i = 0; i < n; i++) if (w.Coast[i] == CoastClass.Cliff) cliffCells.Add(i);
        for (int k = 0, tries = 0; k < 4 && tries < 200 && cliffCells.Count > 0; tries++)
        {
            int i = cliffCells[(int)Math.Floor(rc.Next() * cliffCells.Count)];
            double x = CxOf(i) + w.CoastNx[i] * cell * 0.45, y = CyOf(i) + w.CoastNy[i] * cell * 0.45;
            if (Caves.Exists(c => c.Kind == CaveKind.SeaCave && GenMath.Hyp(c.Xs[0] - x, c.Ys[0] - y) < 3000 * s)) continue;
            Caves.Add(new Cave
            {
                Id = Caves.Count, Kind = CaveKind.SeaCave, Name = "Sea cave " + (k + 1), Xs = [x], Ys = [y], Depth = 0,
                LengthKm = 0.05 + rc.Next() * 0.2, Mouths = [(x, y)], Skylights = [],
            });
            k++;
        }
    }

    // ---------- wrecks: where reefs and swell trap ships; a few aircraft inland ----------
    private void ScatterWrecks()
    {
        IslandShape w = _w;
        IslandParams p = w.Params;
        int nx = _nx, ny = _ny, n = _n;
        double s = _s;
        byte[] land = w.Land, sea = w.Sea, coast = w.Coast, biome = _c.Biomes;
        float[] distSea = w.DistSea, coastEx = w.CoastExposure;
        var rw = new GenRng(p.Seed, "worldgen.wrecks");
        uint js = GenHash.Stream(p.Seed, "wrecks.pick");
        var cand = new List<(int I, double Score, bool OnReef, bool Beached)>();
        for (int i = 0; i < n; i++)
        {
            double ex = -1;
            bool onReef = false, beached = false;
            if (land[i] == 0 && (sea[i] == SeaBand.Reef || sea[i] == SeaBand.Shelf) && distSea[i] < 1500)
            {
                onReef = sea[i] == SeaBand.Reef;
                // exposure of the nearest coast: look two cells toward land
                int x = i % nx, y = i / nx;
                double best = 1e9;
                for (int k = 0; k < 8; k++)
                {
                    int xx = x + GenGrid.DX[k] * 2, yy = y + GenGrid.DY[k] * 2;
                    if (xx < 0 || yy < 0 || xx >= nx || yy >= ny) continue;
                    int j = yy * nx + xx;
                    if (coast[j] != 0 && distSea[i] < best) { best = distSea[i]; ex = coastEx[j]; }
                }
                if (ex < -0.9) ex = 0;
            }
            else if (coast[i] == CoastClass.Beach) { ex = coastEx[i]; beached = true; }
            else continue;
            cand.Add((i, ex + (onReef ? 0.6 : 0) + (beached ? 0.2 : 0) + 0.8 * GenHash.Hash2(js, i, 1) / 4294967296.0, onReef, beached));
        }
        foreach (var c in cand.OrderByDescending(e => e.Score))
        {
            if (Wrecks.Count >= p.Wrecks) break;
            double x = CxOf(c.I), y = CyOf(c.I);
            if (Wrecks.Exists(q => GenMath.Hyp(q.X - x, q.Y - y) < 1500 * s)) continue;
            WreckEra e = WreckEras[(int)Math.Floor(rw.Next() * WreckEras.Length)];
            double heading = rw.Next() * GenMath.Pi;
            Wrecks.Add(new Wreck(Wrecks.Count, x, y, e.Kind, e.Era, e.Length, heading, c.OnReef, c.Beached, false, Capitalise(e.Kind) + ", " + e.Era));
        }
        for (int k = 0, tries = 0; k < (p.Wrecks > 0 ? 2 : 0) && tries < 300; tries++)
        {
            int i = _landCells[(int)Math.Floor(rw.Next() * _landCells.Count)];
            var b = (Biome)biome[i];
            if (!(b == Biome.Wet || b == Biome.Moist || b == Biome.Cloud || b == Biome.Ridge) || _c.SlopeDeg[i] > 25) continue;
            WreckEra e = k == 0 ? new WreckEra("transport aircraft", "1940s", 28) : new WreckEra("light aircraft", "1990s", 11);
            double heading = rw.Next() * GenMath.Pi;
            Wrecks.Add(new Wreck(Wrecks.Count, CxOf(i), CyOf(i), e.Kind, e.Era, e.Length, heading, false, false, true, Capitalise(e.Kind) + ", " + e.Era));
            k++;
        }
    }

    // ---------- ruins from different human ages ----------
    private void PlaceRuins()
    {
        IslandShape w = _w;
        IslandParams p = w.Params;
        double s = _s;
        double[] h = w.Height, rain = w.Rain, seaFrac = _seaFrac;
        float[] slopeD = _c.SlopeDeg, crest = _c.Crest, distLand = w.DistLand;
        byte[] nearRiver = NearRiver, biome = _c.Biomes;
        var ru = new GenRng(p.Seed, "worldgen.ruins");
        const double No = -1;
        Func<int, double> Score(string key) => key switch
        {
            "terraces" => i => rain[i] > 1800 && slopeD[i] > 6 && slopeD[i] < 25 && h[i] > 40 && h[i] < 500 ? 1 + slopeD[i] / 25 : No,
            "stones" => i => h[i] > 400 && crest[i] > 0.4 && slopeD[i] < 18 ? crest[i] + h[i] / 1000 : No,
            "platform" => i => distLand[i] < 900 && slopeD[i] < 8 && nearRiver[i] != 0 ? 1.5 : No,
            "chapel" => i => h[i] < 120 && slopeD[i] < 6 && nearRiver[i] != 0 ? 1.2 : No,
            "mill" => i => h[i] < 150 && slopeD[i] < 5 && nearRiver[i] != 0 && rain[i] < 2000 ? 1.2 : No,
            "lighthouse" => i => distLand[i] < 300 && seaFrac[i] > 0.45 && h[i] > 8 ? seaFrac[i] * 2 : No,
            "bunker" => i => distLand[i] < 500 && seaFrac[i] > 0.35 && h[i] > 15 ? seaFrac[i] * 2 : No,
            "radar" => i => h[i] > 0.6 * p.Peak && slopeD[i] < 20 ? h[i] / 500 : No,
            "camp" => i => slopeD[i] < 10 && h[i] < 600 ? 0.8 : No,
            "fieldlab" => i => (biome[i] == (byte)Biome.Wet || biome[i] == (byte)Biome.Moist) && slopeD[i] < 10 ? 1 : No,
            _ => throw new ArgumentOutOfRangeException(nameof(key), key, "no ruin score"),
        };
        var used = new HashSet<string>();
        // a shuffled order of kinds: one random draw each, in table order, then sorted (stable)
        var drawn = new List<(double R, RuinKind K)>();
        foreach (RuinKind rk in RuinKinds) drawn.Add((ru.Next(), rk));
        List<RuinKind> order = drawn.OrderBy(e => e.R).Select(e => e.K).ToList();
        for (int k = 0, tries = 0; k < p.Ruins && tries < p.Ruins * 6; tries++)
        {
            RuinKind kind = order[tries % order.Count];
            if ((kind.Key == "lighthouse" || kind.Key == "radar") && used.Contains(kind.Key)) continue;
            Func<int, double> score = Score(kind.Key);
            int best = -1;
            double bs = 0;
            for (int m = 0; m < 2500; m++)
            {
                int i = _landCells[(int)Math.Floor(ru.Next() * _landCells.Count)];
                if (!Dry(i)) continue;
                double v = score(i);
                if (v <= bs) continue;
                double x = CxOf(i), y = CyOf(i);
                if (Ruins.Exists(q => GenMath.Hyp(q.X - x, q.Y - y) < 1800 * s) || Sites.Exists(t => GenMath.Hyp(t.X - x, t.Y - y) < t.R + 300)) continue;
                bs = v; best = i;
            }
            if (best < 0) continue;
            used.Add(kind.Key);
            Ruins.Add(new Ruin(Ruins.Count, kind.Key, CxOf(best), CyOf(best), kind.R, kind.Era, kind.Name + " (" + kind.Era + ")", ru.Next() * GenMath.Pi / 2));
            k++;
        }
    }

    // ---------- wildlife habitat ----------
    private void MapWildlife()
    {
        IslandShape w = _w;
        int nx = _nx, ny = _ny, n = _n;
        double area = _area;
        double[] h = w.Height;
        byte[] land = w.Land, coast = w.Coast, biome = _c.Biomes;
        float[] lake = w.LakeLevel, slopeD = _c.SlopeDeg, wet = _c.Wetness;
        var people = new byte[n];
        Array.Fill(people, (byte)1);
        foreach (Site t in Sites) people[Idx(t.X, t.Y)] = 0;
        foreach (Ruin q in Ruins) people[Idx(q.X, q.Y)] = 0;
        float[] dPeople = GenGrid.Chamfer(people, nx, ny, _cell);
        var caveNear = new byte[n];
        foreach (Cave c in Caves)
        {
            foreach ((double mxp, double myp) in c.Mouths)
            {
                int i0 = Idx(mxp, myp), x0 = i0 % nx, y0 = i0 / nx;
                for (int yy = Math.Max(0, y0 - 6); yy <= Math.Min(ny - 1, y0 + 6); yy++)
                    for (int xx = Math.Max(0, x0 - 6); xx <= Math.Min(nx - 1, x0 + 6); xx++) caveNear[yy * nx + xx] = 1;
            }
        }
        foreach (FaunaKind f in FaunaKinds)
        {
            var dens = new byte[n];
            double pop = 0;
            for (int i = 0; i < n; i++)
            {
                if (land[i] == 0 || !float.IsNaN(lake[i]) || PaddockCell[i] >= 0) continue;
                double v = f.Biomes.GetValueOrDefault((Biome)biome[i]);
                if (f.Rocky != 0 && coast[i] == CoastClass.Rocky) v = Math.Max(v, f.Rocky);
                if (f.Cliffs != 0 && coast[i] == CoastClass.Cliff) v = Math.Max(v, f.Cliffs);
                if (f.Cliffs != 0 && _lblM[i] != _mainL) v = Math.Max(v, 0.8); // islets
                if (v == 0) continue;
                if (f.Water != 0) v *= 0.4 + f.Water * Math.Min(1, (NearRiver[i] != 0 ? 1.0 : 0.0) + wet[i]);
                if (f.Steep != 0) v *= 1 + f.Steep * (slopeD[i] > 25 ? 1 : 0);
                if (f.Low) v *= h[i] < 300 ? 1 : 0.3;
                if (f.Caves != 0) v *= 0.6 + f.Caves * caveNear[i];
                if (f.People != 0)
                {
                    double near = GenMath.Exp(-(double)dPeople[i] / 2000);
                    v *= f.PeopleOnly ? near * f.People : 1 + f.People * near;
                }
                v = Math.Min(1, v);
                dens[i] = (byte)GenMath.Round(v * 255);
                pop += v * f.PerKm2 * area / 1e6;
            }
            Fauna.Add(new FaunaMap(f, dens, GenMath.Round(pop)));
        }
    }
}
