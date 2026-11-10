namespace BlueHarvest.Sim.World.Generation;

public enum RiverEnd
{
    /// <summary>Runs into another river.</summary>
    Join,
    /// <summary>Reaches the sea.</summary>
    Sea,
}

/// <summary>A river traced from its head, Chaikin-smoothed. Heights never rise downstream.</summary>
public sealed class River
{
    public required float[] Xs { get; init; }
    public required float[] Ys { get; init; }
    /// <summary>Mean discharge, m³/s.</summary>
    public required float[] Qs { get; init; }
    /// <summary>Water surface height, m.</summary>
    public required float[] Hs { get; init; }
    public required RiverEnd End { get; init; }
    /// <summary>The grid cell the river starts in.</summary>
    public required int Head { get; init; }
}

/// <summary>A run of steep steps on a stream; a cascade when the stream is below river size.</summary>
public sealed record Waterfall(double X, double Y, double Drop, double Q, bool IntoSea, bool OnRiver);

public sealed class Lake
{
    /// <summary>Depression label in the lake pass.</summary>
    public required int Id { get; init; }
    public required double AreaKm2 { get; init; }
    public required double Depth { get; init; }
    public required double Level { get; init; }
    public double Cx { get; set; }
    public double Cy { get; set; }
}

public static class CoastClass
{
    public const byte None = 0, Beach = 1, Rocky = 2, Cliff = 3;
}

public static class SeaBand
{
    public const byte Land = 0, Deep = 1, Shelf = 2, Reef = 3, Lagoon = 4;
}

/// <summary>
/// Stage 2 of island generation: the island's shape on the coarse grid. Landmass, coast and sea cliffs,
/// rain, erosion, lakes, rivers and waterfalls, and the coast classes. Grids are row-major (y * Nx + x),
/// cell centres at ((x + 0.5) · Cell, (y + 0.5) · Cell) metres.
/// </summary>
public sealed class IslandShape
{
    private IslandShape(IslandParams p, IslandAnchors a, int nx, int ny)
    {
        Params = p; Anchors = a; Nx = nx; Ny = ny; N = nx * ny; Cell = p.Cell;
        int n = N;
        Height = new double[n]; OldWeight = new float[n]; Lava = new float[n]; Erodibility = new float[n]; PaliCut = new float[n];
        LakeLevel = new float[n]; Coast = new byte[n]; Sea = new byte[n];
        CoastNx = new float[n]; CoastNy = new float[n]; CoastExposure = new float[n]; CliffMask = new float[n];
        Flow = new double[n]; ToSea = new double[n]; IsRiver = new byte[n];
        Tree = new DrainageTree(nx, ny, p.Cell);
        Filled = new double[n];
        Land = new byte[n];
        Ocean = new byte[n];
        Height0 = [];
        DistLand = [];
        DistSea = [];
        Rain = [];
    }

    public IslandParams Params { get; }
    public IslandAnchors Anchors { get; }
    public int Nx { get; }
    public int Ny { get; }
    public int N { get; }
    public double Cell { get; }

    /// <summary>Final height after erosion and lake filling.</summary>
    public double[] Height { get; }
    /// <summary>Height after the coast was cut, before erosion.</summary>
    public float[] Height0 { get; private set; }
    public float[] OldWeight { get; }
    public float[] Lava { get; }
    public float[] Erodibility { get; }
    public float[] PaliCut { get; }
    public byte[] Ocean { get; private set; }
    public byte[] Land { get; }
    /// <summary>Land cells: metres to the sea. Sea cells: 0.</summary>
    public float[] DistLand { get; private set; }
    /// <summary>Sea cells: metres to land. Land cells: 0.</summary>
    public float[] DistSea { get; private set; }
    /// <summary>The depression-free surface of the last flood.</summary>
    public double[] Filled { get; }
    public DrainageTree Tree { get; }
    /// <summary>Mean annual rain, mm.</summary>
    public double[] Rain { get; private set; }
    /// <summary>Lake surface level, NaN where there is no lake.</summary>
    public float[] LakeLevel { get; }
    public List<Lake> Lakes { get; } = [];
    /// <summary>Mean discharge, m³/s.</summary>
    public double[] Flow { get; }
    /// <summary>Metres along the drainage path to the sea.</summary>
    public double[] ToSea { get; }
    public byte[] IsRiver { get; }
    /// <summary>River head cells, longest path to the sea first.</summary>
    public List<int> Heads { get; } = [];
    public List<River> Rivers { get; } = [];
    public List<Waterfall> Falls { get; } = [];
    /// <summary><see cref="CoastClass"/> of each coastal land cell.</summary>
    public byte[] Coast { get; }
    /// <summary>Outward coast normal and its exposure to wind and swell.</summary>
    public float[] CoastNx { get; }
    public float[] CoastNy { get; }
    public float[] CoastExposure { get; }
    /// <summary><see cref="SeaBand"/> of each sea cell.</summary>
    public byte[] Sea { get; }
    /// <summary>1 on cliff-coast land cells and the sea cells beside them.</summary>
    public float[] CliffMask { get; }

    public double CutKm2 { get; private set; }
    public double StreamLength { get; private set; }
    public double SeaCliffTop { get; private set; }
    public int SeaCliffCells { get; private set; }
    public int PaliCells { get; private set; }
    public double PaliDrop { get; private set; }

    public double CellX(int i) => (i % Nx + 0.5) * Cell;
    public double CellY(int i) => (i / Nx + 0.5) * Cell;

    public static IslandShape Generate(IslandParams p)
    {
        IslandAnchors a = IslandAnchors.Create(p);
        double cell = p.Cell;
        int nx = (int)GenMath.Round(a.W / cell), ny = (int)GenMath.Round(a.H / cell);
        var shape = new IslandShape(p, a, nx, ny);
        shape.Build();
        return shape;
    }

    private static double Exposure(double ox, double oy, double wfx, double wfy, double sfx, double sfy) =>
        Math.Max(ox * wfx + oy * wfy, 0.65 * (ox * sfx + oy * sfy));

    /// <summary>Ocean = at or below sea level and connected to the frame edge (4-connected); other low ground is inland.</summary>
    private byte[] OceanMask()
    {
        int nx = Nx, ny = Ny, n = N;
        double[] h = Height;
        var oc = new byte[n];
        var q = new int[n];
        int qh = 0, qt = 0;
        void Seed(int i)
        {
            if (h[i] <= 0 && oc[i] == 0) { oc[i] = 1; q[qt++] = i; }
        }
        for (int x = 0; x < nx; x++) { Seed(x); Seed((ny - 1) * nx + x); }
        for (int y = 0; y < ny; y++) { Seed(y * nx); Seed(y * nx + nx - 1); }
        while (qh < qt)
        {
            int c = q[qh++], x = c % nx, y = c / nx;
            for (int k = 0; k < 8; k += 2)
            {
                int xx = x + GenGrid.DX[k], yy = y + GenGrid.DY[k];
                if (xx < 0 || yy < 0 || xx >= nx || yy >= ny) continue;
                int j = yy * nx + xx;
                if (oc[j] == 0 && h[j] <= 0) { oc[j] = 1; q[qt++] = j; }
            }
        }
        return oc;
    }

    private void SetLandFromOcean()
    {
        for (int i = 0; i < N; i++) Land[i] = Ocean[i] != 0 ? (byte)0 : (byte)1;
    }

    private void Build()
    {
        IslandParams p = Params;
        IslandAnchors a = Anchors;
        int nx = Nx, ny = Ny, n = N;
        double cell = Cell, area = cell * cell, s = a.S;
        double[] h = Height;

        // ---------- landmass ----------
        LandSample o = default;
        for (int y = 0; y < ny; y++)
        {
            for (int x = 0; x < nx; x++)
            {
                int i = y * nx + x;
                double wx = (x + 0.5) * cell, wy = (y + 0.5) * cell;
                a.Sample(wx, wy, ref o);
                h[i] = o.E; OldWeight[i] = (float)o.Old; Lava[i] = (float)o.Lava; PaliCut[i] = (float)o.Pali;
                Erodibility[i] = (float)GenMath.Clamp(
                    (1 + 0.35 * o.Old + 0.25 * GenNoise.Fbm(a.NErod, wx / (5000 * s), wy / (5000 * s), 3)) * (1 - 0.75 * GenMath.SmoothStep(0.35, 0.8, o.Lava)),
                    0.15, 2);
            }
        }

        // ---------- coast ----------
        Ocean = OceanMask();
        SetLandFromOcean();
        DistLand = GenGrid.Chamfer(Land, nx, ny, cell);

        // Sea cliffs: waves cut back coasts that face the trade wind or the north swell,
        // further where rock is soft, less on young lava. What is left behind is a cliff.
        double wa = p.Wind * GenMath.Pi / 180, wfx = GenMath.Sin(wa), wfy = -GenMath.Cos(wa);
        double sa = 330 * GenMath.Pi / 180, sfx = GenMath.Sin(sa), sfy = -GenMath.Cos(sa);
        if (p.Cliffs > 0)
        {
            double maxR = p.Cliffs * 1000 * s * 1.6;
            var cut = new List<int>();
            float[] dl = DistLand;
            for (int i = 0; i < n; i++)
            {
                if (Land[i] == 0 || dl[i] > maxR) continue;
                int x = i % nx, y = i / nx;
                double gx = (double)dl[y * nx + Math.Min(x + 1, nx - 1)] - dl[y * nx + Math.Max(x - 1, 0)];
                double gy = (double)dl[Math.Min(y + 1, ny - 1) * nx + x] - dl[Math.Max(y - 1, 0) * nx + x];
                double gl = GenMath.Hyp(gx, gy);
                if (gl == 0) continue;
                double ex = GenMath.SmoothStep(0.15, 0.75, Exposure(-gx / gl, -gy / gl, wfx, wfy, sfx, sfy));
                if (ex <= 0) continue;
                double wx = (x + 0.5) * cell, wy = (y + 0.5) * cell;
                double nr = GenMath.Clamp(0.85 + 1.1 * GenNoise.Fbm(a.NCliff, wx / (3500 * s), wy / (3500 * s), 3), 0.15, 1.5);
                double r = p.Cliffs * 1000 * s * ex * nr * (1 - 0.75 * Lava[i]) * (0.55 + 0.45 * Erodibility[i]);
                if (dl[i] < r) cut.Add(i);
            }
            foreach (int i in cut) h[i] = -2;
            Ocean = OceanMask();
            foreach (int i in cut) if (Ocean[i] == 0) h[i] = 1; // a cut cell left inland stays land
            CutKm2 = cut.Count * area / 1e6;
            SetLandFromOcean();
            DistLand = GenGrid.Chamfer(Land, nx, ny, cell);
        }

        // Fragments the sea has cut off (and noise specks) under 0.5 km² are worn down to one sea stack each,
        // so the planned islets stay the only real islets.
        {
            var lbl = new int[n];
            List<int> sizes = GenGrid.Components(Land, nx, ny, lbl);
            int mainK = 0;
            for (int k = 0; k < sizes.Count; k++) if (sizes[k] > sizes[mainK]) mainK = k;
            var planned = new HashSet<int>();
            foreach (Islet isl in a.Islets)
            {
                int iy = (int)GenMath.Clamp(Math.Floor(isl.Y / cell), 0, ny - 1), ix = (int)GenMath.Clamp(Math.Floor(isl.X / cell), 0, nx - 1);
                planned.Add(lbl[iy * nx + ix]);
            }
            double limit = 0.5 * s * 1e6 / area;
            var top = new Dictionary<int, int>();
            for (int i = 0; i < n; i++)
            {
                int k = lbl[i];
                if (k < 0 || k == mainK || planned.Contains(k) || sizes[k] >= limit) continue;
                if (!top.TryGetValue(k, out int t) || h[i] > h[t]) top[k] = i;
            }
            if (top.Count > 0)
            {
                var keep = new HashSet<int>(top.Values);
                for (int i = 0; i < n; i++)
                {
                    int k = lbl[i];
                    if (k >= 0 && top.ContainsKey(k) && !keep.Contains(i)) h[i] = -2;
                }
                Ocean = OceanMask();
                SetLandFromOcean();
                DistLand = GenGrid.Chamfer(Land, nx, ny, cell);
            }
        }
        DistSea = GenGrid.Chamfer(Ocean, nx, ny, cell);
        for (int i = 0; i < n; i++)
        {
            if (Land[i] != 0)
            {
                if (h[i] > 0) h[i] += 1;
            }
            else
            {
                double dc = DistSea[i];
                h[i] = -Math.Min(3800, 1 + Math.Min(dc, 1500) * 0.012 + Math.Max(0, dc - 1500) * 0.12);
            }
        }
        Height0 = new float[n];
        for (int i = 0; i < n; i++) Height0[i] = (float)h[i];

        Erode();
        FindLakes();
        TraceRivers();
        FindFalls();
        ClassifyCoast(wfx, wfy, sfx, sfy);
    }

    // ---------- hydrology and erosion ----------
    private GenHeap? _heap;
    private byte[]? _closed;
    private int[]? _pit;

    private void FloodFill()
    {
        _heap ??= new GenHeap(N);
        _closed ??= new byte[N];
        _pit ??= new int[N];
        Flood.Fill(Height, Land, Nx, Ny, Filled, _closed, _heap, _pit);
    }

    /// <summary>Implicit stream-power erosion on the drainage tree, plus a light thermal pass.</summary>
    private void Erode()
    {
        IslandParams p = Params;
        int nx = Nx, ny = Ny, n = N;
        double cell = Cell, area = cell * cell;
        double[] h = Height;
        var aeff = new double[n];
        var hn = new double[n];
        Rain = RainModel.Compute(h, Land, nx, ny, cell, p.Wind, p.Rain);
        double kdt = 0.012 * p.Erosion * (cell / 128), fillRate = 0.08 * Math.Min(1.5, p.Erosion);
        const double tanT = 0.75;
        DrainageTree tr = Tree;
        for (int it = 0; it < p.Iterations; it++)
        {
            FloodFill();
            tr.Build(Filled, Land);
            int[] st = tr.Stack, rec = tr.Receiver;
            int ns = tr.StackCount;
            for (int k = 0; k < ns; k++) { int i = st[k]; aeff[i] = area * Rain[i] / 2000; }
            for (int k = ns - 1; k >= 0; k--) { int i = st[k], r = rec[i]; if (r >= 0) aeff[r] += aeff[i]; }
            for (int k = 0; k < ns; k++)
            {
                int i = st[k], r = rec[i];
                double hr = r >= 0 ? hn[r] : 0, hi = h[i];
                double nh;
                if (hi > hr)
                {
                    double f = kdt * Erodibility[i] * (1 - 0.4 * PaliCut[i]) * Math.Sqrt(aeff[i]) / tr.ReceiverDistance[i];
                    nh = (hi + f * hr) / (1 + f);
                }
                else nh = hi + (Filled[i] - hi) * fillRate;
                hn[i] = Math.Max(nh, Math.Min(hi, 1));
            }
            for (int k = 0; k < ns; k++) { int i = st[k]; h[i] = hn[i]; }
            // light thermal pass: crumble crags steeper than ~37 degrees, but leave sea cliffs and young lava alone
            for (int i = 0; i < n; i++)
            {
                if (Land[i] == 0 || DistLand[i] < 600 || Erodibility[i] < 0.5 || PaliCut[i] > 0.25) continue;
                int x = i % nx, y = i / nx, lo = -1;
                double ld = 0;
                for (int k = 0; k < 8; k++)
                {
                    int xx = x + GenGrid.DX[k], yy = y + GenGrid.DY[k];
                    if (xx < 0 || yy < 0 || xx >= nx || yy >= ny) continue;
                    int j = yy * nx + xx;
                    if (Land[j] == 0) continue;
                    double dd = h[i] - h[j] - tanT * GenGrid.DL[k] * cell;
                    if (dd > ld) { ld = dd; lo = j; }
                }
                if (lo >= 0) { double mv = ld * 0.15; h[i] -= mv; h[lo] += mv; }
            }
        }
    }

    /// <summary>Deep, large depressions (and the caldera's) stay lakes; the rest are filled in.</summary>
    private void FindLakes()
    {
        IslandParams p = Params;
        int nx = Nx, ny = Ny, n = N;
        double cell = Cell, area = cell * cell;
        double[] h = Height;
        FloodFill();
        var depMask = new byte[n];
        for (int i = 0; i < n; i++) if (Land[i] != 0 && Filled[i] - h[i] > 0.3) depMask[i] = 1;
        var label = new int[n];
        List<int> dsz = GenGrid.Components(depMask, nx, ny, label);
        var dmax = new double[dsz.Count];
        var dlev = new double[dsz.Count];
        Array.Fill(dlev, -1e9);
        for (int i = 0; i < n; i++)
        {
            int l = label[i];
            if (l >= 0) { dmax[l] = Math.Max(dmax[l], Filled[i] - h[i]); dlev[l] = Math.Max(dlev[l], Filled[i]); }
        }
        Array.Fill(LakeLevel, float.NaN);
        Caldera cal = Anchors.Caldera;
        var inCal = new bool[dsz.Count];
        for (int i = 0; i < n; i++)
        {
            int l = label[i];
            if (l >= 0 && GenMath.Hyp((i % nx + 0.5) * cell - cal.X, (i / nx + 0.5) * cell - cal.Y) < cal.R) inCal[l] = true;
        }
        var keep = new bool[dsz.Count];
        var byId = new Dictionary<int, Lake>();
        for (int l = 0; l < dsz.Count; l++)
        {
            int sz = dsz[l];
            keep[l] = (dmax[l] >= p.LakeDepth && sz * area / 1e6 >= p.LakeArea) || (inCal[l] && dmax[l] >= 4 && sz * area / 1e6 >= 0.05);
            if (!keep[l]) continue;
            var lake = new Lake { Id = l, AreaKm2 = sz * area / 1e6, Depth = dmax[l], Level = dlev[l] };
            Lakes.Add(lake);
            byId[l] = lake;
        }
        for (int i = 0; i < n; i++)
        {
            int l = label[i];
            if (l < 0) continue;
            if (keep[l])
            {
                LakeLevel[i] = (float)dlev[l];
                Lake lake = byId[l];
                lake.Cx += (i % nx + 0.5) * cell;
                lake.Cy += (i / nx + 0.5) * cell;
            }
            else h[i] = Filled[i];
        }
        foreach (Lake lake in Lakes)
        {
            int c = dsz[lake.Id];
            lake.Cx /= c; lake.Cy /= c;
        }
        Tree.Build(Filled, Land);
        Rain = RainModel.Compute(h, Land, nx, ny, cell, p.Wind, p.Rain);
    }

    private void TraceRivers()
    {
        IslandParams p = Params;
        int nx = Nx, n = N;
        double cell = Cell, area = cell * cell;
        double[] h = Height, q = Flow;
        DrainageTree tr = Tree;
        int[] st = tr.Stack, rec = tr.Receiver;
        int ns = tr.StackCount;
        const double runoff = 0.55, year = 3.156e7;
        for (int k = 0; k < ns; k++) { int i = st[k]; q[i] = Rain[i] / 1000 * area * runoff / year; }
        for (int k = ns - 1; k >= 0; k--)
        {
            int i = st[k], r = rec[i];
            if (r >= 0) q[r] += q[i] * GenMath.Pow(0.985, (double)tr.ReceiverDistance[i] / 1000);
        }
        for (int k = 0; k < ns; k++) { int i = st[k], r = rec[i]; ToSea[i] = (r >= 0 ? ToSea[r] : 0) + tr.ReceiverDistance[i]; }
        var upRiver = new byte[n];
        for (int i = 0; i < n; i++) if (Land[i] != 0 && q[i] >= p.RiverQ) IsRiver[i] = 1;
        for (int i = 0; i < n; i++) if (IsRiver[i] != 0 && rec[i] >= 0 && IsRiver[rec[i]] != 0) upRiver[rec[i]] = 1;
        // heads, biggest first so trunks are traced from their longest source (stable: ties keep cell order)
        var heads = new List<int>();
        for (int i = 0; i < n; i++) if (IsRiver[i] != 0 && upRiver[i] == 0) heads.Add(i);
        Heads.AddRange(heads.OrderByDescending(i => ToSea[i]));

        var visited = new byte[n];
        uint nJit = GenHash.Stream(p.Seed, "worldgen.riverjitter");
        double Cx(int i)
        {
            int x = i % nx;
            return (x + 0.5 + (GenHash.Hash2(nJit, i, 1) / 4294967296.0 - 0.5) * 0.5) * cell;
        }
        double Cy(int i)
        {
            int y = i / nx;
            return (y + 0.5 + (GenHash.Hash2(nJit, i, 2) / 4294967296.0 - 0.5) * 0.5) * cell;
        }
        double streamLen = 0;
        foreach (int h0 in Heads)
        {
            var pts = new List<PolyPoint>();
            int i = h0;
            RiverEnd end = RiverEnd.Join;
            while (true)
            {
                pts.Add(new PolyPoint(Cx(i), Cy(i), q[i], Math.Max(0, float.IsNaN(LakeLevel[i]) ? h[i] : LakeLevel[i])));
                if (visited[i] != 0) break;
                visited[i] = 1;
                int r = rec[i];
                if (r < 0)
                {
                    streamLen += (double)tr.ReceiverDistance[i] / 2;
                    int m = tr.Mouth[i];
                    if (m >= 0)
                    {
                        double mx = (m % nx + 0.5) * cell, my = (m / nx + 0.5) * cell;
                        PolyPoint last = pts[^1];
                        pts.Add(new PolyPoint((last.X + mx) / 2, (last.Y + my) / 2, q[i], 0));
                    }
                    end = RiverEnd.Sea;
                    break;
                }
                streamLen += tr.ReceiverDistance[i];
                i = r;
            }
            if (pts.Count < 2) continue;
            List<PolyPoint> sm = GenGrid.Chaikin(pts, 2);
            for (int k = 1; k < sm.Count; k++)
                if (sm[k].H > sm[k - 1].H) sm[k] = sm[k] with { H = sm[k - 1].H };
            int len = sm.Count;
            var xs = new float[len];
            var ys = new float[len];
            var qs = new float[len];
            var hs = new float[len];
            for (int k = 0; k < len; k++)
            {
                xs[k] = (float)sm[k].X; ys[k] = (float)sm[k].Y; qs[k] = (float)sm[k].Q; hs[k] = (float)sm[k].H;
            }
            Rivers.Add(new River { Xs = xs, Ys = ys, Qs = qs, Hs = hs, End = end, Head = h0 });
        }
        StreamLength = streamLen;
    }

    /// <summary>Waterfalls: steep steps along a stream, merged from the top of each run.</summary>
    private void FindFalls()
    {
        IslandParams p = Params;
        int nx = Nx, n = N;
        double cell = Cell;
        double[] h = Height, q = Flow;
        DrainageTree tr = Tree;
        int[] rec = tr.Receiver;
        var steep = new byte[n];
        var stepDrop = new double[n];
        for (int i = 0; i < n; i++)
        {
            if (Land[i] == 0 || q[i] < 0.02) continue; // cascades on small streams count too
            int r = rec[i];
            double hr = r >= 0 ? h[r] : 0, drop = h[i] - hr;
            if (drop > Math.Max(15, 0.3 * tr.ReceiverDistance[i])) { steep[i] = 1; stepDrop[i] = drop; }
        }
        for (int i = 0; i < n; i++)
        {
            if (steep[i] == 0) continue;
            bool top = true;
            for (int qd = tr.DonorStart[i]; qd < tr.DonorStart[i + 1]; qd++)
                if (steep[tr.Donors[qd]] != 0) { top = false; break; }
            if (!top) continue;
            int j = i;
            double total = 0;
            while (j >= 0 && steep[j] != 0) { total += stepDrop[j]; j = rec[j]; }
            if (total < 40 && q[i] < p.RiverQ) continue;
            Falls.Add(new Waterfall((i % nx + 0.5) * cell, (i / nx + 0.5) * cell, total, q[i], j < 0, q[i] >= p.RiverQ));
        }
    }

    // A low, sheltered coast in a bay collects sand.
    private const double BeachH = 30, BeachEx = 0.2, BeachBay = 0.6;

    private void ClassifyCoast(double wfx, double wfy, double sfx, double sfy)
    {
        int nx = Nx, ny = Ny, n = N;
        double cell = Cell;
        double[] h = Height;
        int bayR = Math.Max(2, (int)GenMath.Round(1000 / cell));
        double SeaNear(int x, int y)
        {
            int sc = 0, tot = 0;
            for (int yy = Math.Max(0, y - bayR); yy <= Math.Min(ny - 1, y + bayR); yy++)
            {
                for (int xx = Math.Max(0, x - bayR); xx <= Math.Min(nx - 1, x + bayR); xx++)
                {
                    tot++;
                    if (Land[yy * nx + xx] == 0) sc++;
                }
            }
            return (double)sc / tot;
        }
        for (int i = 0; i < n; i++)
        {
            if (Land[i] == 0) continue;
            int x = i % nx, y = i / nx, ox = 0, oy = 0;
            bool isC = false;
            for (int k = 0; k < 8; k++)
            {
                int xx = x + GenGrid.DX[k], yy = y + GenGrid.DY[k];
                if (xx < 0 || yy < 0 || xx >= nx || yy >= ny) continue;
                if (Land[yy * nx + xx] == 0)
                {
                    ox += GenGrid.DX[k]; oy += GenGrid.DY[k];
                    if ((k & 1) == 0) isC = true;
                }
            }
            if (!isC) continue;
            double ol = GenMath.Hyp(ox, oy);
            if (ol == 0) ol = 1;
            double expo = Exposure(ox / ol, oy / ol, wfx, wfy, sfx, sfy);
            CoastNx[i] = (float)(ox / ol); CoastNy[i] = (float)(oy / ol); CoastExposure[i] = (float)expo;
            double hIn = 0;
            int rad = Math.Max(1, (int)GenMath.Round(300 / cell));
            for (int yy = Math.Max(0, y - rad); yy <= Math.Min(ny - 1, y + rad); yy++)
            {
                for (int xx = Math.Max(0, x - rad); xx <= Math.Min(nx - 1, x + rad); xx++)
                {
                    int j = yy * nx + xx;
                    if (Land[j] != 0) hIn = Math.Max(hIn, h[j]);
                }
            }
            double slopeIn = hIn / (rad * cell);
            if (Lava[i] > 0.6) Coast[i] = hIn > 40 ? CoastClass.Cliff : CoastClass.Rocky;
            else if (hIn > 45 && (expo > 0.25 || slopeIn > 0.2)) Coast[i] = CoastClass.Cliff;
            else if (hIn < 14) Coast[i] = CoastClass.Beach;
            else if (hIn < BeachH && expo < BeachEx && SeaNear(x, y) < BeachBay) Coast[i] = CoastClass.Beach; // sand collects in sheltered bays
            else Coast[i] = CoastClass.Rocky;
        }
        uint nReef = Anchors.NReef;
        for (int i = 0; i < n; i++)
        {
            if (Land[i] != 0) continue;
            double dc = DistSea[i], x = (i % nx + 0.5) * cell, y = (i / nx + 0.5) * cell;
            double nr = GenNoise.Fbm(nReef, x / 2500, y / 2500, 3);
            if (dc > 1800) Sea[i] = SeaBand.Deep;
            else if (Lava[i] < 0.45 && dc >= 300 && dc <= 1100 && nr > -0.05) Sea[i] = SeaBand.Reef;
            else if (Lava[i] < 0.45 && dc < 300 && nr > 0.08) Sea[i] = SeaBand.Lagoon;
            else Sea[i] = SeaBand.Shelf;
        }
        // Cliff mask for the chunk detail: cliff-coast land cells and the sea cells beside them.
        double seaCliffTop = 0;
        int seaCliffCells = 0;
        for (int i = 0; i < n; i++)
        {
            if (Coast[i] != CoastClass.Cliff) continue;
            CliffMask[i] = 1; seaCliffCells++; seaCliffTop = Math.Max(seaCliffTop, h[i]);
            int x = i % nx, y = i / nx;
            for (int k = 0; k < 8; k++)
            {
                int xx = x + GenGrid.DX[k], yy = y + GenGrid.DY[k];
                if (xx >= 0 && yy >= 0 && xx < nx && yy < ny && Land[yy * nx + xx] == 0) CliffMask[yy * nx + xx] = 1;
            }
        }
        SeaCliffTop = seaCliffTop; SeaCliffCells = seaCliffCells;
        // Pali: wall cells (steep, inside the cut) and the tallest drop within 800 m.
        int paliCells = 0;
        double paliDrop = 0;
        int pr = Math.Max(1, (int)Math.Ceiling(800 / cell));
        for (int i = 0; i < n; i++)
        {
            if (Land[i] == 0 || PaliCut[i] < 0.3) continue;
            int x = i % nx, y = i / nx;
            double sx = (h[y * nx + Math.Min(x + 1, nx - 1)] - h[y * nx + Math.Max(x - 1, 0)]) / (2 * cell);
            double sy = (h[Math.Min(y + 1, ny - 1) * nx + x] - h[Math.Max(y - 1, 0) * nx + x]) / (2 * cell);
            if (GenMath.Hyp(sx, sy) < 0.47) continue; // 25 degrees
            paliCells++;
            double lo = h[i], hi = h[i];
            for (int yy = Math.Max(0, y - pr); yy <= Math.Min(ny - 1, y + pr); yy++)
            {
                for (int xx = Math.Max(0, x - pr); xx <= Math.Min(nx - 1, x + pr); xx++)
                {
                    int j = yy * nx + xx;
                    if (Land[j] != 0) { if (h[j] < lo) lo = h[j]; if (h[j] > hi) hi = h[j]; }
                }
            }
            paliDrop = Math.Max(paliDrop, hi - lo);
        }
        PaliCells = paliCells; PaliDrop = paliDrop;
    }
}
