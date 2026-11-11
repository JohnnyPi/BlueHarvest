namespace BlueHarvest.Sim.World.Generation;

/// <summary>One square of the island at 1 m per tile. Arrays are row-major, Size × Size.</summary>
public sealed class IslandChunk
{
    public required int Size { get; init; }
    /// <summary>World position of the first tile, in metres.</summary>
    public required int X0 { get; init; }
    public required int Y0 { get; init; }
    /// <summary>Ground (or river/sea bed) height, m.</summary>
    public required float[] Height { get; init; }
    /// <summary>The Cover value of each tile.</summary>
    public required byte[] Cover { get; init; }
    /// <summary>Water surface level, NaN on dry tiles.</summary>
    public required float[] Water { get; init; }
    /// <summary>Hillshade, 0.45–1.25, light from the north-west.</summary>
    public required float[] Shade { get; init; }
    /// <summary>The Biome value of each tile.</summary>
    public required byte[] Biome { get; init; }
    /// <summary>Index into the works' labels (room and feature names), −1 for none.</summary>
    public required short[] Label { get; init; }
    /// <summary>
    /// Which way river water runs on each tile and how fast: east and south components in decimetres
    /// per second. Zero on dry ground, lakes and the sea.
    /// </summary>
    public required sbyte[] FlowX { get; init; }
    public required sbyte[] FlowY { get; init; }
}

/// <summary>
/// Stage 5 of island generation: turns any square of the island into 1 m tiles. A chunk is a pure
/// function of the island, so neighbouring chunks agree exactly along their seams and chunks can be
/// built in any order, on any thread. Build one chunker per island and keep it.
/// </summary>
public sealed class IslandChunker
{
    public const int ChunkSize = 64;
    private const int Bin = 256, PrimBin = 32;

    // ground cover inside each biome, from open to closed ground, as (cover, cumulative share)
    private static readonly (Cover C, double Upto)[][] Mix = BuildMix();

    private static (Cover, double)[][] BuildMix()
    {
        (Cover, double)[][] raw =
        [
            [(Cover.Shallow, 1)], // ocean
            [(Cover.Beach, 0.8), (Cover.Grass, 0.08), (Cover.Scrub, 0.12)], // strand
            [(Cover.Shallow, 0.12), (Cover.Mud, 0.16), (Cover.Reeds, 0.44), (Cover.TallGrass, 0.28)], // marsh
            [(Cover.Boulder, 0.05), (Cover.Grass, 0.25), (Cover.Scrub, 0.52), (Cover.Sparse, 0.18)], // dry scrub
            [(Cover.Grass, 0.5), (Cover.TallGrass, 0.3), (Cover.Scrub, 0.1), (Cover.Sparse, 0.1)], // grassland
            [(Cover.Grass, 0.2), (Cover.Scrub, 0.14), (Cover.Sparse, 0.42), (Cover.Dense, 0.24)], // mesic
            [(Cover.Sparse, 0.3), (Cover.Fern, 0.12), (Cover.Dense, 0.58)], // moist
            [(Cover.Sparse, 0.08), (Cover.Fern, 0.22), (Cover.Dense, 0.7)], // wet
            [(Cover.Fern, 0.38), (Cover.Cloud, 0.62)], // cloud
            [(Cover.Rock, 0.18), (Cover.Grass, 0.15), (Cover.Scrub, 0.42), (Cover.Fern, 0.25)], // ridge
            [(Cover.Rock, 0.72), (Cover.Boulder, 0.14), (Cover.Fern, 0.14)], // cliff
            [(Cover.Lava, 0.86), (Cover.Scrub, 0.08), (Cover.Fern, 0.06)], // lava
            [(Cover.Lake, 1)], // lake
        ];
        foreach ((Cover, double)[] m in raw)
        {
            double acc = 0;
            for (int k = 0; k < m.Length; k++) { acc += m[k].Item2; m[k] = (m[k].Item1, acc); }
        }
        return raw;
    }

    private static Cover PickCover(int biome, double u)
    {
        (Cover C, double Upto)[] m = Mix[biome];
        for (int k = 0; k < m.Length; k++) if (u < m[k].Upto) return m[k].C;
        return m[^1].C;
    }

    public static double RiverWidth(double q) => Math.Max(1.5, 2.6 * Math.Sqrt(q));
    public static double RiverDepth(double q) => Math.Max(0.3, 0.3 * GenMath.Pow(q, 0.4));

    private static double Cr(double p0, double p1, double p2, double p3, double t) =>
        p1 + 0.5 * t * (p2 - p0 + t * (2 * p0 - 5 * p1 + 4 * p2 - p3 + t * (3 * (p1 - p2) + p3 - p0)));

    private static int ClampI(int v, int a, int b) => v < a ? a : v > b ? b : v;

    private static double Bicubic(float[] f, int nx, int ny, double gx, double gy)
    {
        double fix = Math.Floor(gx), fiy = Math.Floor(gy);
        int ix = (int)fix, iy = (int)fiy;
        double fx = gx - fix, fy = gy - fiy;
        Span<double> c = stackalloc double[4];
        for (int m = -1; m <= 2; m++)
        {
            int row = ClampI(iy + m, 0, ny - 1) * nx;
            c[m + 1] = Cr(f[row + ClampI(ix - 1, 0, nx - 1)], f[row + ClampI(ix, 0, nx - 1)], f[row + ClampI(ix + 1, 0, nx - 1)], f[row + ClampI(ix + 2, 0, nx - 1)], fx);
        }
        return Cr(c[0], c[1], c[2], c[3], fy);
    }

    // ---------- what the index holds ----------
    private sealed class Way
    {
        public required double[] Xs;
        public required double[] Ys;
        public required double[] Hs;
        public required RoadFlags[] Flags;
        public required double W;
        public bool Runway;
    }

    private enum ItemKind : byte { Way, Fence, Pylon, Rail, Site, Pier, Ruin, Wreck, Cave }

    private sealed class Item
    {
        public ItemKind Kind;
        public Way? Way;
        public int K;
        public double Ax, Ay, Bx, By; // fence, rail
        public bool HasGate;
        public double GateX, GateY, Gap;
        public double X, Y, R; // pylon, cave
        public bool Sky;
        public Site? Site;
        public double L; // pier length
        public Ruin? Ruin;
        public Wreck? Wreck;
    }

    private readonly IslandWorld _world;
    private readonly int _nx, _ny;
    private readonly double _cell;
    private readonly float[] _height, _rain;
    private readonly uint _nTile, _nFine, _nCover;
    private readonly Dictionary<long, List<(int River, int K)>> _riverBins = new();
    private readonly Dictionary<long, List<Item>> _bins = new();
    private readonly Dictionary<long, List<Prim>> _primBins = new();
    private readonly double[][] _riverXs, _riverYs, _riverQs, _riverHs;

    public IslandChunker(IslandWorld world)
    {
        _world = world;
        IslandShape s = world.Shape;
        _nx = s.Nx; _ny = s.Ny; _cell = s.Cell;
        // the lab hands the chunk code 32-bit copies of height and rain
        _height = new float[s.N];
        _rain = new float[s.N];
        for (int i = 0; i < s.N; i++) { _height[i] = (float)s.Height[i]; _rain[i] = (float)s.Rain[i]; }
        _nTile = s.Anchors.NTile; _nFine = s.Anchors.NTile2; _nCover = s.Anchors.NCover;
        static double[] Wide(float[] a)
        {
            var d = new double[a.Length];
            for (int i = 0; i < a.Length; i++) d[i] = a[i];
            return d;
        }
        _riverXs = s.Rivers.Select(r => Wide(r.Xs)).ToArray();
        _riverYs = s.Rivers.Select(r => Wide(r.Ys)).ToArray();
        _riverQs = s.Rivers.Select(r => Wide(r.Qs)).ToArray();
        _riverHs = s.Rivers.Select(r => Wide(r.Hs)).ToArray();
        IndexRivers();
        IndexWorks(Wide);
    }

    private static long Floor(double v) => (long)Math.Floor(v);

    private void IndexRivers()
    {
        for (int ri = 0; ri < _riverXs.Length; ri++)
        {
            double[] xs = _riverXs[ri], ys = _riverYs[ri];
            for (int k = 0; k + 1 < xs.Length; k++)
            {
                double x0 = Math.Min(xs[k], xs[k + 1]), x1 = Math.Max(xs[k], xs[k + 1]);
                double y0 = Math.Min(ys[k], ys[k + 1]), y1 = Math.Max(ys[k], ys[k + 1]);
                for (long by = Floor(y0 / Bin); by <= Floor(y1 / Bin); by++)
                {
                    for (long bx = Floor(x0 / Bin); bx <= Floor(x1 / Bin); bx++)
                    {
                        long key = bx * 4096 + by;
                        if (!_riverBins.TryGetValue(key, out var list)) _riverBins[key] = list = [];
                        list.Add((ri, k));
                    }
                }
            }
        }
    }

    /// <summary>Spatial index of everything people built (and caves, wrecks, ruins), in 256 m bins; buildings and furniture in 32 m bins.</summary>
    private void IndexWorks(Func<float[], double[]> wide)
    {
        IslandWorks w = _world.Works;
        void Put(double x0, double y0, double x1, double y1, Item item)
        {
            for (long by = Floor(y0 / Bin); by <= Floor(y1 / Bin); by++)
            {
                for (long bx = Floor(x0 / Bin); bx <= Floor(x1 / Bin); bx++)
                {
                    long key = bx * 4096 + by;
                    if (!_bins.TryGetValue(key, out var list)) _bins[key] = list = [];
                    list.Add(item);
                }
            }
        }
        void Seg(double ax, double ay, double bx, double by, double pad, Item item) =>
            Put(Math.Min(ax, bx) - pad, Math.Min(ay, by) - pad, Math.Max(ax, bx) + pad, Math.Max(ay, by) + pad, item);

        var ways = new List<Way>();
        foreach (Road r in w.Roads)
            ways.Add(new Way { Xs = wide(r.Xs), Ys = wide(r.Ys), Hs = wide(r.Hs), Flags = r.Flags, W = r.Class == RoadClass.Main ? 7 : 4 });
        foreach (Site t in w.Sites)
        {
            if (t.Runway is not { } rw) continue;
            ways.Add(new Way { Xs = [rw.X1, rw.X2], Ys = [rw.Y1, rw.Y2], Hs = [rw.H, rw.H], Flags = [RoadFlags.None, RoadFlags.None], W = 45, Runway = true });
        }
        foreach (Way r in ways)
            for (int k = 0; k + 1 < r.Xs.Length; k++)
                Seg(r.Xs[k], r.Ys[k], r.Xs[k + 1], r.Ys[k + 1], r.W / 2 + 12, new Item { Kind = ItemKind.Way, Way = r, K = k });
        foreach (Paddock q in w.Paddocks)
        {
            for (int v = 0; v < q.Poly.Length; v += 2)
            {
                int a = v, b = (v + 2) % q.Poly.Length;
                Seg(q.Poly[a], q.Poly[a + 1], q.Poly[b], q.Poly[b + 1], 2, new Item
                {
                    Kind = ItemKind.Fence, Ax = q.Poly[a], Ay = q.Poly[a + 1], Bx = q.Poly[b], By = q.Poly[b + 1],
                    HasGate = true, GateX = q.Gate.X, GateY = q.Gate.Y, Gap = 2.5,
                });
            }
        }
        foreach (PowerLine l in w.Power)
        {
            double len = GenMath.Hyp(l.X2 - l.X1, l.Y2 - l.Y1);
            int nP = (int)Math.Floor(len / 250);
            for (int k = 1; k < nP; k++)
            {
                double x = l.X1 + (l.X2 - l.X1) * k / nP, y = l.Y1 + (l.Y2 - l.Y1) * k / nP;
                Put(x - 3, y - 3, x + 3, y + 3, new Item { Kind = ItemKind.Pylon, X = x, Y = y });
            }
        }
        foreach (Paddock q in w.Paddocks)
        {
            if (q.Inner is not { } inner) continue;
            for (int v = 0; v < inner.Length; v += 2)
            {
                int a = v, b = (v + 2) % inner.Length;
                Seg(inner[a], inner[a + 1], inner[b], inner[b + 1], 2, new Item
                {
                    Kind = ItemKind.Fence, Ax = inner[a], Ay = inner[a + 1], Bx = inner[b], By = inner[b + 1],
                    HasGate = true, GateX = q.Gate.X, GateY = q.Gate.Y, Gap = 9,
                });
            }
        }
        if (w.Tram is { } tram)
        {
            double[] xs = wide(tram.Xs), ys = wide(tram.Ys);
            for (int k = 0; k + 1 < xs.Length; k++)
                Seg(xs[k], ys[k], xs[k + 1], ys[k + 1], 3, new Item { Kind = ItemKind.Rail, Ax = xs[k], Ay = ys[k], Bx = xs[k + 1], By = ys[k + 1] });
        }
        foreach (Site t in w.Sites)
            Put(t.X - t.R - 40, t.Y - t.R - 40, t.X + t.R + 40, t.Y + t.R + 40, new Item { Kind = ItemKind.Site, Site = t });

        void PPut(double x0, double y0, double x1, double y1, Prim item)
        {
            for (long by = Floor(y0 / PrimBin); by <= Floor(y1 / PrimBin); by++)
            {
                for (long bx = Floor(x0 / PrimBin); bx <= Floor(x1 / PrimBin); bx++)
                {
                    long key = bx * 8192 + by;
                    if (!_primBins.TryGetValue(key, out var list)) _primBins[key] = list = [];
                    list.Add(item);
                }
            }
        }
        foreach (Prim q in w.Prims.InOrder())
        {
            switch (q.Kind)
            {
                case PrimKind.Circle: PPut(q.X - q.R - 1, q.Y - q.R - 1, q.X + q.R + 1, q.Y + q.R + 1, q); break;
                case PrimKind.Tower: PPut(q.X - 2, q.Y - 2, q.X + 2, q.Y + 2, q); break;
                case PrimKind.Line: PPut(Math.Min(q.Ax, q.Bx) - q.W, Math.Min(q.Ay, q.By) - q.W, Math.Max(q.Ax, q.Bx) + q.W, Math.Max(q.Ay, q.By) + q.W, q); break;
                default: PPut(q.X0 - 1, q.Y0 - 1, q.X1 + 1, q.Y1 + 1, q); break;
            }
        }
        foreach (Site t in w.Sites)
        {
            if (t.Kind != "docks" || t.Dir is not { } dir) continue;
            double len = t.R + 90;
            Seg(t.X, t.Y, t.X + dir.X * len, t.Y + dir.Y * len, 6, new Item { Kind = ItemKind.Pier, Site = t, L = len });
        }
        foreach (Ruin q in w.Ruins) Put(q.X - q.R - 3, q.Y - q.R - 3, q.X + q.R + 3, q.Y + q.R + 3, new Item { Kind = ItemKind.Ruin, Ruin = q });
        foreach (Wreck q in w.Wrecks) Put(q.X - q.Length, q.Y - q.Length, q.X + q.Length, q.Y + q.Length, new Item { Kind = ItemKind.Wreck, Wreck = q });
        foreach (Cave c in w.Caves)
        {
            foreach ((double mx, double my) in c.Mouths) Put(mx - 5, my - 5, mx + 5, my + 5, new Item { Kind = ItemKind.Cave, X = mx, Y = my, R = 3.5 });
            foreach ((double mx, double my) in c.Skylights) Put(mx - 4, my - 4, mx + 4, my + 4, new Item { Kind = ItemKind.Cave, X = mx, Y = my, R = 2.5, Sky = true });
        }
    }

    private static double SegDist(double x, double y, double ax, double ay, double bx, double by, out double t)
    {
        double dx = bx - ax, dy = by - ay, l2 = dx * dx + dy * dy;
        if (l2 == 0) l2 = 1;
        t = ((x - ax) * dx + (y - ay) * dy) / l2;
        t = t < 0 ? 0 : t > 1 ? 1 : t;
        return GenMath.Hyp(ax + dx * t - x, ay + dy * t - y);
    }

    /// <summary>
    /// The nearest river reach: how far away, its discharge and water height there, the unit vector
    /// pointing downstream along it (rivers are traced from their heads), and how steeply it falls (m per m).
    /// </summary>
    private void NearestRiver(double x, double y, out double dist, out double q, out double h, out double ux, out double uy, out double drop)
    {
        dist = 1e9; q = 0; h = 0; ux = 0; uy = 0; drop = 0;
        long bx = Floor(x / Bin), by = Floor(y / Bin);
        for (long yy = by - 1; yy <= by + 1; yy++)
        {
            for (long xx = bx - 1; xx <= bx + 1; xx++)
            {
                if (!_riverBins.TryGetValue(xx * 4096 + yy, out var list)) continue;
                foreach ((int ri, int k) in list)
                {
                    double[] xs = _riverXs[ri], ys = _riverYs[ri], qs = _riverQs[ri], hs = _riverHs[ri];
                    double ax = xs[k], ay = ys[k], dx = xs[k + 1] - ax, dy = ys[k + 1] - ay, l2 = dx * dx + dy * dy;
                    if (l2 == 0) l2 = 1;
                    double t = ((x - ax) * dx + (y - ay) * dy) / l2;
                    t = t < 0 ? 0 : t > 1 ? 1 : t;
                    double d = GenMath.Hyp(ax + dx * t - x, ay + dy * t - y);
                    if (d < dist)
                    {
                        dist = d; q = qs[k] + (qs[k + 1] - qs[k]) * t; h = hs[k] + (hs[k + 1] - hs[k]) * t;
                        double len = Math.Sqrt(l2);
                        ux = dx / len; uy = dy / len; drop = (hs[k] - hs[k + 1]) / len;
                    }
                }
            }
        }
    }

    /// <summary>
    /// How fast a river runs, in m/s: its discharge over its cross-section, faster on steep reaches.
    /// A first guess for tuning; nothing in the terrain depends on it.
    /// </summary>
    public static double RiverSpeed(double q, double drop)
    {
        double flat = q / (RiverWidth(q) * RiverDepth(q));
        double steep = GenMath.Clamp(0.5 + 6 * Math.Sqrt(Math.Max(0, drop)), 0.5, 2.5);
        return GenMath.Clamp(flat * steep, 0.2, 6);
    }

    private static byte Mk(Cover c) => (byte)((int)c + 1); // tile marks are a cover plus one; 0 is "no mark"

    private static readonly byte MkRoad = Mk(Cover.Road), MkBridge = Mk(Cover.Bridge), MkRunway = Mk(Cover.Runway), MkYard = Mk(Cover.Yard), MkWall = Mk(Cover.Wall),
        MkFloor = Mk(Cover.Floor), MkDoor = Mk(Cover.Door), MkFence = Mk(Cover.Fence), MkPylon = Mk(Cover.Pylon), MkRuin = Mk(Cover.Ruin), MkRubble = Mk(Cover.Rubble),
        MkWreck = Mk(Cover.Wreck), MkCave = Mk(Cover.Cave), MkPier = Mk(Cover.Pier), MkBigWall = Mk(Cover.BigWall), MkRail = Mk(Cover.Rail), MkMast = Mk(Cover.Mast);

    /// <summary>
    /// Builds the chunk whose first tile is at (ccx · 64, ccy · 64) metres. size is normally 64;
    /// a larger size extends the chunk east and south (used to test that seams match).
    /// </summary>
    public IslandChunk Build(int ccx, int ccy, int size = ChunkSize)
    {
        IslandShape s = _world.Shape;
        IslandClimate cl = _world.Climate;
        int nx = _nx, ny = _ny;
        double cell = _cell;
        int m2 = size + 2, x0 = ccx * ChunkSize - 1, y0 = ccy * ChunkSize - 1;
        var h = new float[m2 * m2];
        var water = new float[m2 * m2];
        var riverD = new float[m2 * m2];
        Array.Fill(water, float.NaN);
        Array.Fill(riverD, 1e9f);
        var flowX = new sbyte[m2 * m2];
        var flowY = new sbyte[m2 * m2];
        var kind = new sbyte[m2 * m2]; // 0 land, 1 sea, 2 river, 3 lake
        var mark = new byte[m2 * m2];
        var lab = new short[m2 * m2];
        Array.Fill(lab, (short)-1);
        byte[] land = s.Land;
        float[] lake = s.LakeLevel;

        for (int j = 0; j < m2; j++)
        {
            for (int i = 0; i < m2; i++)
            {
                int k = j * m2 + i;
                double x = x0 + i + 0.5, y = y0 + j + 0.5, gx = x / cell - 0.5, gy = y / cell - 0.5;
                double z = Bicubic(_height, nx, ny, gx, gy);
                double det = 1.3 * GenNoise.Fbm(_nTile, x / 32, y / 32, 3) + 0.35 * GenNoise.Noise(_nFine, x / 5, y / 5);
                z += z > 0 ? det : det * 0.3;
                // sea cliffs: the coastline is a jagged line through the cliff cells; land behind it stands at the
                // cliff-top height a few metres in, the sea in front drops to a wave-cut bench
                double cm = GenGrid.Bilinear(s.CliffMask, nx, ny, gx, gy);
                if (cm > 0.12)
                {
                    double sdc = GenGrid.Bilinear(s.DistLand, nx, ny, gx, gy) - GenGrid.Bilinear(s.DistSea, nx, ny, gx, gy)
                        + 20 * GenNoise.Fbm(_nFine ^ 0x3c3cu, x / 28, y / 28, 3);
                    int bx = (int)GenMath.Clamp(Math.Floor(gx), 0, nx - 2), by = (int)GenMath.Clamp(Math.Floor(gy), 0, ny - 2);
                    double top = 0;
                    for (int b = 0; b <= 1; b++)
                    {
                        for (int a = 0; a <= 1; a++)
                        {
                            int c = (by + b) * nx + bx + a;
                            if (land[c] != 0) top = Math.Max(top, _height[c]);
                        }
                    }
                    double wgt = Math.Min(1, cm * 1.6);
                    double zc = sdc > 0 ? (z > top ? z : Math.Max(0.6, top * GenMath.SmoothStep(0, 6, sdc) + det * 0.4)) : Math.Min(z, -1.2 + sdc * 0.06);
                    z = z + (zc - z) * wgt;
                }
                if (z < 0) { kind[k] = 1; water[k] = 0; }
                NearestRiver(x, y, out double rvD, out double rvQ, out double rvH, out double rvUx, out double rvUy, out double rvDrop);
                if (rvD < 60)
                {
                    double w = RiverWidth(rvQ), dep = RiverDepth(rvQ), half = w / 2, bank = 2 + w * 0.6;
                    if (rvD < half)
                    {
                        z = Math.Min(z, rvH - dep); kind[k] = 2; water[k] = (float)rvH;
                        double dms = RiverSpeed(rvQ, rvDrop) * 10;
                        flowX[k] = (sbyte)GenMath.Round(rvUx * dms); flowY[k] = (sbyte)GenMath.Round(rvUy * dms);
                    }
                    else if (rvD < half + bank * 3) z = Math.Min(z, rvH + (rvD - half) * 0.45);
                    riverD[k] = (float)(rvD - half);
                }
                // lakes: the nearest lake cells set a water level; terrain below it floods
                {
                    int ix = (int)Math.Floor(gx), iy = (int)Math.Floor(gy);
                    double lv = double.NaN;
                    for (int b = 0; b <= 1; b++)
                    {
                        for (int a = 0; a <= 1; a++)
                        {
                            int cx2 = ClampI(ix + a, 0, nx - 1), cy2 = ClampI(iy + b, 0, ny - 1);
                            double level = lake[cy2 * nx + cx2];
                            if (!double.IsNaN(level) && !(level <= lv)) lv = level;
                        }
                    }
                    if (!double.IsNaN(lv) && z < lv && kind[k] != 1) { kind[k] = 3; water[k] = (float)lv; }
                }
                // ---- the works: what people built (and caves, wrecks, ruins) ----
                if (_bins.TryGetValue(Floor(x / Bin) * 4096 + Floor(y / Bin), out List<Item>? items))
                    z = Works(items, k, x, y, z, kind, water, mark, lab);
                h[k] = (float)z;
            }
        }

        // slope and 2 m terraces on steep, rocky ground (2 m is the human climb limit)
        var slope = new float[m2 * m2];
        for (int j = 1; j < m2 - 1; j++)
        {
            for (int i = 1; i < m2 - 1; i++)
            {
                int k = j * m2 + i;
                slope[k] = (float)GenMath.Hyp(((double)h[k + 1] - h[k - 1]) / 2, ((double)h[k + m2] - h[k - m2]) / 2);
            }
        }
        var label = new short[size * size];
        var outH = new float[size * size];
        var cover = new byte[size * size];
        var lvl = new float[size * size];
        var shade = new float[size * size];
        var tBiome = new byte[size * size];
        var outFlowX = new sbyte[size * size];
        var outFlowY = new sbyte[size * size];
        double seaT = _world.Params.SeaTemp;
        for (int j = 0; j < size; j++)
        {
            for (int i = 0; i < size; i++)
            {
                int k = (j + 1) * m2 + (i + 1), o2 = j * size + i;
                double x = x0 + i + 1.5, y = y0 + j + 1.5, gx = x / cell - 0.5, gy = y / cell - 0.5;
                double z = h[k], sl = slope[k];
                double lavaV = GenGrid.Bilinear(s.Lava, nx, ny, gx, gy) + 0.2 * GenNoise.Fbm(_nCover, x / 40, y / 40, 2);
                double paliV = GenGrid.Bilinear(s.PaliCut, nx, ny, gx, gy);
                int ledge = -1; // on the pali: 1 = a ledge, 0 = a rock face
                if (kind[k] == 0 && paliV > 0.3 && sl > 0.6)
                {
                    // the pali: 6 m rock steps with narrow ledges
                    const double step6 = 6;
                    double step = Math.Floor(z / step6) * step6, f = (z - step) / step6;
                    z = step + (f < 0.3 ? 0 : (f - 0.3) / 0.7 * step6);
                    ledge = f < 0.3 ? 1 : 0;
                }
                else if (kind[k] == 0 && sl > 0.5 && lavaV < 0.55)
                {
                    double step = Math.Floor(z / 2) * 2, f = z - step;
                    z = step + (f < 0.7 ? 0 : (f - 0.7) / 0.3 * 2);
                }
                outH[o2] = (float)z; lvl[o2] = water[k];
                if (kind[k] == 2) { outFlowX[o2] = flowX[k]; outFlowY[o2] = flowY[k]; } // a lake or the works may have taken the tile since
                {
                    // hillshade, light from the north-west; uses the margin so chunk edges match
                    double sx = ((double)h[k + 1] - h[k - 1]) / 2, sy = ((double)h[k + m2] - h[k - m2]) / 2;
                    shade[o2] = (float)GenMath.Clamp(0.82 + (sx + sy) * 0.55 - (sx * sx + sy * sy) * 0.06, 0.45, 1.25);
                }
                double nC = GenNoise.Fbm(_nCover, x / 22, y / 22, 3), nC2 = GenNoise.Noise(_nCover ^ 0x5bd1u, x / 7, y / 7);
                Cover c;
                if (kind[k] == 1)
                {
                    int ci = (int)GenMath.Clamp(Math.Floor(x / cell), 0, nx - 1) + (int)GenMath.Clamp(Math.Floor(y / cell), 0, ny - 1) * nx;
                    double dep = -z;
                    c = s.Sea[ci] == SeaBand.Reef && dep < 6 && nC > -0.1 ? Cover.Reef : dep < 2.5 ? Cover.Shallow : Cover.Deep;
                }
                else if (kind[k] == 2) c = Cover.River;
                else if (kind[k] == 3) c = Cover.Lake;
                else
                {
                    double rainV = GenGrid.Bilinear(_rain, nx, ny, gx, gy);
                    double dc = GenGrid.Bilinear(s.DistLand, nx, ny, gx, gy);
                    int bx = (int)GenMath.Clamp(Math.Floor(gx), 0, nx - 2), by = (int)GenMath.Clamp(Math.Floor(gy), 0, ny - 2);
                    bool beachy = false;
                    for (int b = 0; b <= 1; b++)
                        for (int a = 0; a <= 1; a++)
                            if (s.Coast[(by + b) * nx + bx + a] == CoastClass.Beach) beachy = true;
                    double jt = GenMath.Clamp(1.6 * GenNoise.Fbm(_nCover ^ 0x5151u, x / 90, y / 90, 3), -1, 1);
                    double wetV = GenGrid.Bilinear(cl.Wetness, nx, ny, gx, gy) + (riverD[k] < 25 ? 0.15 : 0);
                    Biome bi = IslandClimate.Classify(Math.Max(0, z), rainV, seaT - 6.5 * Math.Max(0, z) / 1000, GenGrid.Bilinear(cl.Fog, nx, ny, gx, gy), wetV,
                        GenGrid.Bilinear(cl.SlopeDeg, nx, ny, gx, gy), lavaV, dc, beachy, GenGrid.Bilinear(cl.Crest, nx, ny, gx, gy), jt);
                    tBiome[o2] = (byte)bi;
                    double u = 0.5 + 0.5 * GenMath.Tanh(2.4 * (nC + 0.35 * nC2));
                    if (ledge == 0) c = Cover.Rock;
                    else if (ledge == 1) c = rainV > 2200 ? (nC2 > 0.3 ? Cover.Dense : Cover.Fern) : Cover.Scrub;
                    else if (sl > 1.1) c = Cover.Rock;
                    else if (riverD[k] < 3 + nC2 * 2 && sl < 0.2) c = Cover.Mud;
                    else if (bi == Biome.Strand) c = PickCover((int)bi, GenMath.Clamp(dc / 200 + 0.25 * nC, 0, 0.999)); // sand at the water, strand plants behind
                    else
                    {
                        c = PickCover((int)bi, u);
                        // gallery forest: rivers carry trees through dry country
                        if (riverD[k] < 16 + 10 * nC2 && (bi == Biome.DryScrub || bi == Biome.Grassland || bi == Biome.Mesic)) c = nC > 0 ? Cover.Dense : Cover.Sparse;
                        if (sl > 0.75 && c != Cover.Dense && c != Cover.Cloud) c = nC2 > 0.2 ? Cover.Boulder : Cover.Rock;
                    }
                }
                if (kind[k] != 0) tBiome[o2] = kind[k] == 1 ? (byte)Biome.Ocean : (byte)Biome.Lake;
                if (mark[k] != 0) c = (Cover)(mark[k] - 1);
                cover[o2] = (byte)c; label[o2] = lab[k];
            }
        }
        return new IslandChunk { Size = size, X0 = ccx * ChunkSize, Y0 = ccy * ChunkSize, Height = outH, Cover = cover, Water = lvl, Shade = shade, Biome = tBiome, Label = label, FlowX = outFlowX, FlowY = outFlowY };
    }

    /// <summary>Applies the works to one tile; returns the tile's new height.</summary>
    private double Works(List<Item> items, int k, double x, double y, double z, sbyte[] kind, float[] water, byte[] mark, short[] lab)
    {
        double bestW = 1e9, wT = 0;
        Way? wR = null;
        int wK = 0;
        foreach (Item it in items)
        {
            if (it.Kind != ItemKind.Way) continue;
            Way r = it.Way!;
            double d = SegDist(x, y, r.Xs[it.K], r.Ys[it.K], r.Xs[it.K + 1], r.Ys[it.K + 1], out double t);
            if (d < bestW) { bestW = d; wR = r; wK = it.K; wT = t; }
        }
        if (wR != null)
        {
            // roads: cut and fill to the road surface, bridges over water, tunnels left underground
            double half = wR.W / 2, hr = wR.Hs[wK] + (wR.Hs[wK + 1] - wR.Hs[wK]) * wT;
            RoadFlags fl = wR.Flags[wK] | wR.Flags[wK + 1];
            if ((fl & RoadFlags.Tunnel) == 0 && bestW < half + 12)
            {
                if (kind[k] == 2 || kind[k] == 3 || (kind[k] == 1 && !wR.Runway))
                {
                    if (bestW < half) mark[k] = MkBridge;
                }
                else if (bestW < half) { z = hr; mark[k] = wR.Runway ? MkRunway : MkRoad; }
                else
                {
                    double tt = GenMath.SmoothStep(0, 1, (bestW - half) / 12);
                    z = hr + (z - hr) * tt;
                }
            }
        }
        foreach (Item it in items)
        {
            if (it.Kind == ItemKind.Site)
            {
                Site t = it.Site!;
                double dx = x - t.X, dy = y - t.Y, dch = Math.Max(Math.Abs(dx), Math.Abs(dy));
                if (dch < t.R + 30)
                {
                    double wgt = dch <= t.R + 2 ? 1 : 1 - (dch - t.R - 2) / 28;
                    if (kind[k] != 1 || dch <= t.R)
                    {
                        z = z + (t.H - z) * wgt;
                        if (dch <= t.R) { kind[k] = 0; water[k] = float.NaN; }
                    }
                    if (dch <= t.R + 0.5)
                    {
                        bool ns = t.Gate == 'n' || t.Gate == 's';
                        double along = ns ? dx : dy;
                        bool onSide = t.Gate == 'n' ? dy < 0 : t.Gate == 's' ? dy > 0 : t.Gate == 'e' ? dx > 0 : dx < 0;
                        bool gateGap = onSide && Math.Abs(along) < 4 && Math.Abs(Math.Abs(onSide && ns ? dy : dx) - t.R) < 1;
                        mark[k] = dch > t.R - 0.5 && !gateGap ? MkFence : MkYard;
                    }
                }
            }
            else if (it.Kind == ItemKind.Pier)
            {
                Site t = it.Site!;
                (double X, double Y) dir = t.Dir!.Value;
                double d = SegDist(x, y, t.X, t.Y, t.X + dir.X * it.L, t.Y + dir.Y * it.L, out double st);
                if (d < 3 && st * it.L > t.R * 0.4)
                {
                    if (kind[k] == 1) { kind[k] = 0; water[k] = float.NaN; }
                    z = Math.Max(z, 1.6);
                    mark[k] = MkPier;
                }
            }
        }
        int ix = (int)Math.Floor(x), iy = (int)Math.Floor(y);
        foreach (Item it in items)
        {
            if (it.Kind == ItemKind.Rail && SegDist(x, y, it.Ax, it.Ay, it.Bx, it.By, out _) < 1.6 && kind[k] != 1) mark[k] = MkRail;
        }
        if (_primBins.TryGetValue(Floor(x / PrimBin) * 8192 + Floor(y / PrimBin), out List<Prim>? prims))
        {
            foreach (Prim q in prims)
            {
                switch (q.Kind)
                {
                    case PrimKind.Building:
                    {
                        if (ix < q.X0 || ix > q.X1 || iy < q.Y0 || iy > q.Y1) continue;
                        kind[k] = 0; water[k] = float.NaN;
                        byte m = MkFloor;
                        int lb = q.Label;
                        if (ix == q.X0 || ix == q.X1 || iy == q.Y0 || iy == q.Y1) m = MkWall;
                        else
                        {
                            foreach (Room rm in q.Rooms!)
                            {
                                if (ix < rm.X0 || ix > rm.X1 || iy < rm.Y0 || iy > rm.Y1) continue;
                                lb = rm.Label;
                                if (ix == rm.X0 || ix == rm.X1 || iy == rm.Y0 || iy == rm.Y1) m = MkWall;
                            }
                        }
                        foreach ((int dx, int dy) in q.Doors!) if (dx == ix && dy == iy) m = MkDoor;
                        mark[k] = m; lab[k] = (short)lb;
                        break;
                    }
                    case PrimKind.Furniture:
                        if (ix >= q.X0 && ix <= q.X1 && iy >= q.Y0 && iy <= q.Y1 && mark[k] == MkFloor) mark[k] = Mk(q.Cover);
                        break;
                    case PrimKind.Circle:
                    {
                        double d = GenMath.Hyp(x - q.X, y - q.Y);
                        if (d < q.R + 0.75)
                        {
                            if (q.Ring)
                            {
                                if (Math.Abs(d - q.R) < 0.75) mark[k] = q.Door && y > q.Y && Math.Abs(x - q.X) < 1 ? MkDoor : Mk(q.Cover);
                                else if (d < q.R) mark[k] = Mk(q.Fill!.Value);
                            }
                            else if (d < q.R) mark[k] = Mk(q.Cover);
                            lab[k] = (short)q.Label;
                            if (kind[k] == 1 && q.Cover != Cover.Shallow) { kind[k] = 0; water[k] = float.NaN; }
                        }
                        break;
                    }
                    case PrimKind.Rect:
                        if (ix >= q.X0 && ix <= q.X1 && iy >= q.Y0 && iy <= q.Y1)
                        {
                            mark[k] = q.Fence && (ix == q.X0 || ix == q.X1 || iy == q.Y0 || iy == q.Y1) ? MkFence : Mk(q.Cover);
                            lab[k] = (short)q.Label;
                        }
                        break;
                    case PrimKind.Tower:
                        if (Math.Abs(x - q.X) < 1.5 && Math.Abs(y - q.Y) < 1.5) { mark[k] = MkMast; lab[k] = (short)q.Label; }
                        break;
                    case PrimKind.Line:
                    {
                        double d = SegDist(x, y, q.Ax, q.Ay, q.Bx, q.By, out _), hw = q.W / 2;
                        if (d < hw && kind[k] == 0)
                        {
                            double f = d / hw;
                            z -= q.Depth * (1 - f * f);
                            mark[k] = Mk(q.Cover); lab[k] = (short)q.Label;
                        }
                        break;
                    }
                    case PrimKind.Pen:
                    {
                        if (ix < q.X0 || ix > q.X1 || iy < q.Y0 || iy > q.Y1) continue;
                        int e = Math.Min(Math.Min(ix - q.X0, q.X1 - ix), Math.Min(iy - q.Y0, q.Y1 - iy));
                        bool mid = Math.Abs(ix - (q.X0 + q.X1) / 2.0) < 1.5 && iy > (q.Y0 + q.Y1) / 2.0;
                        if (e < 2) mark[k] = mid ? MkDoor : MkBigWall;
                        else if (e == 5) mark[k] = mid ? (byte)0 : MkFence;
                        else if (mark[k] == MkYard) mark[k] = 0; // planted inside: the biome shows through
                        lab[k] = (short)q.Label;
                        break;
                    }
                }
            }
        }
        foreach (Item it in items)
        {
            switch (it.Kind)
            {
                case ItemKind.Fence:
                    if (SegDist(x, y, it.Ax, it.Ay, it.Bx, it.By, out _) < 0.55 && !(it.HasGate && GenMath.Hyp(x - it.GateX, y - it.GateY) < it.Gap)
                        && kind[k] == 0 && mark[k] != MkRoad) mark[k] = MkFence;
                    break;
                case ItemKind.Pylon:
                    if (Math.Abs(x - it.X) < 1.1 && Math.Abs(y - it.Y) < 1.1 && kind[k] == 0) mark[k] = MkPylon;
                    break;
                case ItemKind.Ruin:
                {
                    Ruin q = it.Ruin!;
                    double c = GenMath.Cos(q.Rot), sn = GenMath.Sin(q.Rot), u = (x - q.X) * c + (y - q.Y) * sn, v = -(x - q.X) * sn + (y - q.Y) * c;
                    double gap = GenHash.Hash2(_nCover, ix, iy) / 4294967296.0;
                    if (kind[k] != 0) continue;
                    if (q.Kind == "stones")
                    {
                        double rr = GenMath.Hyp(u, v), a = GenMath.Atan2(v, u), sa = GenMath.Round(a / (GenMath.Pi / 8)) * (GenMath.Pi / 8);
                        if (GenMath.Hyp(u - GenMath.Cos(sa) * q.R, v - GenMath.Sin(sa) * q.R) < 0.9) mark[k] = MkRuin;
                        else if (rr < q.R * 0.25 && gap < 0.3) mark[k] = MkRubble;
                    }
                    else if (q.Kind == "terraces")
                    {
                        if (GenMath.Hyp(u, v) < q.R && ((v % 6) + 6) % 6 < 0.8 && gap > 0.15) mark[k] = MkRuin;
                    }
                    else if (q.Kind == "lighthouse")
                    {
                        double rr = GenMath.Hyp(u, v);
                        if (rr < 3.6) mark[k] = rr > 2.6 ? (gap > 0.2 ? MkRuin : MkRubble) : MkFloor;
                    }
                    else
                    {
                        double m = Math.Max(Math.Abs(u), Math.Abs(v));
                        if (m < q.R + 0.4) mark[k] = m > q.R - 0.6 ? (gap > 0.3 ? MkRuin : MkRubble) : (gap < 0.25 ? MkRubble : mark[k]);
                    }
                    break;
                }
                case ItemKind.Wreck:
                {
                    Wreck q = it.Wreck!;
                    double c = GenMath.Cos(q.Heading), sn = GenMath.Sin(q.Heading), u = (x - q.X) * c + (y - q.Y) * sn, v = -(x - q.X) * sn + (y - q.Y) * c;
                    double a = q.Length / 2.0, b = Math.Max(2, q.Length / 7.0), e = (u / a) * (u / a) + (v / b) * (v / b);
                    double gap = GenHash.Hash2(_nFine, ix, iy) / 4294967296.0;
                    if ((e > 0.78 && e < 1.08 && gap > 0.18) || (e <= 0.78 && gap < 0.3) || (q.Inland && Math.Abs(u) < 1.2 && Math.Abs(v) < q.Length * 0.55 && gap > 0.25))
                    {
                        if (kind[k] == 1 && z > -6) z = Math.Max(z, -0.4);
                        mark[k] = MkWreck;
                    }
                    break;
                }
                case ItemKind.Cave:
                {
                    double d = GenMath.Hyp(x - it.X, y - it.Y);
                    if (d < it.R && kind[k] == 0)
                    {
                        mark[k] = MkCave;
                        if (it.Sky) z -= 4 * (1 - d / it.R);
                    }
                    break;
                }
            }
        }
        return z;
    }
}
