namespace BlueHarvest.Sim.World.Generation;

/// <summary>A suspended lift between two stations, with towers along the cable.</summary>
public sealed record Lift(string Name, string From, string To, double X1, double Y1, double X2, double Y2, double Km,
    List<(double X, double Y)> Towers, double Crest);

public sealed record TramStop(string Name, int X, int Y);

public sealed class Tram
{
    public required string Name { get; init; }
    public required float[] Xs { get; init; }
    public required float[] Ys { get; init; }
    public required List<TramStop> Stops { get; init; }
    public required double Km { get; init; }
}

/// <summary>A monorail station: an elevator from the surface down to the line at height Z.</summary>
public sealed class MonorailStation
{
    public required string Name { get; init; }
    public required int X { get; init; }
    public required int Y { get; init; }
    public double Z { get; set; }
    /// <summary>Elevator travel from the surface to the platform, m.</summary>
    public double Shaft { get; set; }
    /// <summary>The site whose power runs the elevator.</summary>
    public string Power { get; set; } = "";
}

public sealed class Monorail
{
    public required string Name { get; init; }
    public required List<MonorailStation> Stations { get; init; }
    public required List<(int X, int Y)> Vents { get; init; }
    public required double Km { get; init; }
    public required double MinCover { get; init; }
    public required double Deepest { get; init; }
}

/// <summary>Something the player has to find.</summary>
public sealed record Objective(string Key, string Name, string Unlocks, string Site, string Room, double X, double Y);

public sealed partial class IslandWorks
{
    // ---------- walking times between facilities (Tobler's hiking function) ----------
    private int _cnx, _cny, _cn;
    private double _cs;
    private double[] _ch = [], _spd = [], _wade = [], _wd = [];
    private byte[] _pass = [], _onRoad = [], _wdone = [];
    private int[] _targets = [];
    private GenHeap _wh = new(1);

    /// <summary>Minutes on foot between every pair of sites (infinity if there is no way).</summary>
    public double[][] Walk { get; private set; } = [];
    /// <summary>The same with lifts, tram and monorail.</summary>
    public double[][] WalkTransit { get; private set; } = [];
    public List<Lift> Lifts { get; } = [];
    public Tram? Tram { get; private set; }
    public Monorail? Monorail { get; private set; }
    public List<Objective> Objectives { get; } = [];

    // walking speed factor by biome
    private static readonly double[] BiomeSpeed = [0, 0.85, 0.35, 0.85, 1, 0.8, 0.6, 0.45, 0.45, 0.7, 0.25, 0.55, 0];

    /// <summary>Walking speed in m/s on a slope (rise over run); 1.4 on the flat.</summary>
    private static double Tobler(double sl) =>
        6 * GenMath.Exp(-3.5 * Math.Abs(sl + 0.05)) / 3.6 / (6 * GenMath.Exp(-3.5 * 0.05) / 3.6) * 1.4;

    private int CoarseAt(double x, double y) =>
        (int)GenMath.Clamp(Math.Floor(y / _cs), 0, _cny - 1) * _cnx + (int)GenMath.Clamp(Math.Floor(x / _cs), 0, _cnx - 1);

    private void TimeWalks()
    {
        int nx = _nx, ny = _ny;
        double[] h = _w.Height, q = _w.Flow;
        int f = Math.Max(1, (int)GenMath.Round(256 / _cell));
        int cnx = (int)Math.Ceiling(nx / (double)f), cny = (int)Math.Ceiling(ny / (double)f), cn = cnx * cny;
        _cnx = cnx; _cny = cny; _cn = cn; _cs = _cell * f;
        _ch = new double[cn]; _spd = new double[cn]; _wade = new double[cn]; _wd = new double[cn];
        _pass = new byte[cn]; _onRoad = new byte[cn]; _wdone = new byte[cn];
        _wh = new GenHeap(cn * 8);
        for (int cy = 0; cy < cny; cy++)
        {
            for (int cx = 0; cx < cnx; cx++)
            {
                int c = cy * cnx + cx, lc = 0, cliffy = 0, tot = 0;
                double hs = 0, sp = 0, river = 0;
                bool road = false;
                for (int y = cy * f; y < Math.Min(ny, cy * f + f); y++)
                {
                    for (int x = cx * f; x < Math.Min(nx, cx * f + f); x++)
                    {
                        int i = y * nx + x;
                        tot++;
                        if (RoadCell[i] != 0) road = true; // a road (or its tunnel) can always be walked
                        if (!Dry(i)) continue;
                        lc++; hs += h[i]; sp += BiomeSpeed[_c.Biomes[i]];
                        if (_w.IsRiver[i] != 0 && q[i] > 1) river = Math.Max(river, q[i]);
                        if (_c.SlopeDeg[i] > 38) cliffy++;
                    }
                }
                if (!road && (lc * 2 < tot || cliffy * 2 > lc)) continue;
                if (lc == 0) { lc = 1; hs = 0; }
                _pass[c] = 1; _ch[c] = hs / lc; _spd[c] = road ? 1.05 : sp / lc; _onRoad[c] = road ? (byte)1 : (byte)0;
                _wade[c] = road ? 0 : river > 0 ? 120 + 30 * Math.Min(10, river) : 0;
            }
        }
        // each site's nearest passable coarse cell (watch posts sit on narrow headlands)
        _targets = new int[Sites.Count];
        for (int a = 0; a < Sites.Count; a++) _targets[a] = Target(Sites[a]);
        Walk = Travel(null);
        for (int a = 0; a < Sites.Count; a++) Sites[a].Nearest = NearestOf(Walk, a);
    }

    private int Target(Site t)
    {
        int c0 = CoarseAt(t.X, t.Y);
        if (_pass[c0] != 0) return c0;
        int x0 = c0 % _cnx, y0 = c0 / _cnx;
        for (int rr = 1; rr < 6; rr++)
            for (int yy = y0 - rr; yy <= y0 + rr; yy++)
                for (int xx = x0 - rr; xx <= x0 + rr; xx++)
                    if (xx >= 0 && yy >= 0 && xx < _cnx && yy < _cny && _pass[yy * _cnx + xx] != 0) return yy * _cnx + xx;
        return c0;
    }

    private NearestSite? NearestOf(double[][] walk, int a)
    {
        int best = -1;
        for (int b = 0; b < Sites.Count; b++)
            if (b != a && (best < 0 || walk[a][b] < walk[a][best])) best = b;
        return best >= 0 ? new NearestSite(best, Sites[best].Name, walk[a][best]) : null;
    }

    /// <summary>Minutes between every pair of sites; extra maps a coarse cell to its transit links (cell, seconds).</summary>
    private double[][] Travel(Dictionary<int, List<(int To, double Seconds)>>? extra)
    {
        int ns = Sites.Count, cnx = _cnx, cny = _cny, cn = _cn;
        double cs = _cs;
        var walk = new double[ns][];
        for (int a = 0; a < ns; a++) { walk[a] = new double[ns]; Array.Fill(walk[a], double.PositiveInfinity); }
        double[] wd = _wd;
        GenHeap wh = _wh;
        for (int a = 0; a < ns; a++)
        {
            Array.Fill(wd, double.PositiveInfinity);
            Array.Clear(_wdone);
            wh.Clear();
            int s0 = _targets[a];
            if (_pass[s0] == 0) continue;
            wd[s0] = 0;
            wh.Push(0, s0);
            int left = ns;
            while (wh.Count > 0 && left > 0)
            {
                int c = wh.Pop();
                if (_wdone[c] != 0) continue;
                _wdone[c] = 1;
                for (int b = 0; b < ns; b++)
                    if (_targets[b] == c && double.IsPositiveInfinity(walk[a][b])) { walk[a][b] = wd[c] / 60; left--; }
                int x = c % cnx, y = c / cnx;
                for (int k = 0; k < 8; k++)
                {
                    int xx = x + GenGrid.DX[k], yy = y + GenGrid.DY[k];
                    if (xx < 0 || yy < 0 || xx >= cnx || yy >= cny) continue;
                    int j = yy * cnx + xx;
                    if (_pass[j] == 0 || _wdone[j] != 0) continue;
                    double d = GenGrid.DL[k] * cs, sl = (_ch[j] - _ch[c]) / d;
                    bool roads = _onRoad[c] != 0 && _onRoad[j] != 0;
                    if (Math.Abs(sl) > 0.8 && !roads) continue;
                    double v = Tobler(roads ? sl * 0.6 : sl) * (_spd[c] + _spd[j]) / 2;
                    double nd = wd[c] + d / Math.Max(0.05, v) + _wade[j];
                    if (nd < wd[j]) { wd[j] = nd; wh.Push(nd, j); }
                }
                if (extra != null && extra.TryGetValue(c, out var links))
                {
                    foreach ((int j, double sec) in links)
                    {
                        double nd = wd[c] + sec;
                        if (nd < wd[j] && wh.Count < cn * 8 - 1) { wd[j] = nd; wh.Push(nd, j); }
                    }
                }
            }
        }
        return walk;
    }

    // ---------- transit: a skyway over the pali, a summit lift, a street tram, an underground monorail ----------
    private readonly Dictionary<int, List<(int To, double Seconds)>> _links = new();

    private void Link(int a, int b, double sec)
    {
        void One(int u, int v)
        {
            if (!_links.TryGetValue(u, out var list)) _links[u] = list = [];
            list.Add((v, sec));
        }
        One(a, b);
        One(b, a);
    }

    /// <summary>A station just outside a compound of radius r at (x, y), on the side facing (tx, ty).</summary>
    private static (int X, int Y) StationPoint(double x, double y, double r, double tx, double ty)
    {
        double dx = tx - x, dy = ty - y, dl = GenMath.Hyp(dx, dy);
        if (dl == 0) dl = 1;
        return ((int)GenMath.Round(x + dx / dl * (r + 16)), (int)GenMath.Round(y + dy / dl * (r + 16)));
    }

    private void StationBuilding((int X, int Y) pt, string line, string name, RoomSpec[] rooms) =>
        FacilityPrograms.Building(new BuildingSpec(name, 18, 10, rooms), pt.X - 9, pt.Y - 5, pt.X + 8, pt.Y + 4, line, Labels, Prims);

    private static readonly RoomSpec[] LiftRooms = [new("Cabin hall", 3), new("Winch room", 1, Furnish.Tanks)];
    private static readonly RoomSpec[] ElevatorRooms = [new("Elevator lobby", 2, Furnish.Console), new("Elevator", 1, Furnish.Lift)];

    private void MakeLift(double ax, double ay, double ar, string aName, double bx, double by, double br, string bName, string name)
    {
        double[] h = _w.Height;
        (int X, int Y) a1 = StationPoint(ax, ay, ar, bx, by), b1 = StationPoint(bx, by, br, ax, ay);
        double len = GenMath.Hyp(b1.X - a1.X, b1.Y - a1.Y);
        var towers = new List<(double X, double Y)>();
        int nT = Math.Max(1, (int)GenMath.Round(len / 350));
        for (int k = 1; k < nT; k++) towers.Add((a1.X + (b1.X - a1.X) * k / (double)nT, a1.Y + (b1.Y - a1.Y) * k / (double)nT));
        Labels.Add(name + " · Lift tower");
        int tl = Labels.Count - 1;
        foreach ((double X, double Y) q in towers) Prims.Add(new Prim { Kind = PrimKind.Tower, X = q.X, Y = q.Y, Label = tl });
        StationBuilding(a1, name, "Station at " + aName, LiftRooms);
        StationBuilding(b1, name, "Station at " + bName, LiftRooms);
        double top = 0;
        for (int k = 0; k <= 20; k++) top = Math.Max(top, h[Idx(a1.X + (b1.X - a1.X) * k / 20.0, a1.Y + (b1.Y - a1.Y) * k / 20.0)]);
        Lifts.Add(new Lift(name, aName, bName, a1.X, a1.Y, b1.X, b1.Y, len / 1000, towers, top));
        Link(CoarseAt(ax, ay), CoarseAt(bx, by), len / 5 + 180);
    }

    private void LayTransit()
    {
        IslandParams p = _w.Params;
        int nx = _nx, ny = _ny;
        double cell = _cell, sq = _sq;
        double[] h = _w.Height;
        List<Site> sites = Sites;
        double[][] walk = Walk;
        if (p.Transit > 0 && sites.Count > 2)
        {
            LaySkyway();
            // a summit lift up to the highest facility, from the nearest major one
            if (sites.Where(t => t.Kind == "weather" || t.Kind == "comms").OrderByDescending(t => t.H).FirstOrDefault() is { } summit
                && !Lifts.Exists(l => l.From == summit.Name || l.To == summit.Name))
            {
                var m = sites.Where(t => t.Major).Select(t => (Site: t, D: GenMath.Hyp(t.X - summit.X, t.Y - summit.Y))).OrderBy(e => e.D).ToList();
                if (m.Count > 0 && m[0].D < 7000 * sq && walk[summit.Id][m[0].Site.Id] > 60)
                    MakeLift(m[0].Site.X, m[0].Site.Y, m[0].Site.R, m[0].Site.Name, summit.X, summit.Y, summit.R, summit.Name, "Summit lift");
            }
            LayTram();
            LayMonorail();
        }
        WalkTransit = Lifts.Count > 0 || Tram != null || Monorail != null ? Travel(_links) : walk;
        for (int a = 0; a < sites.Count; a++) sites[a].NearestTransit = NearestOf(WalkTransit, a);

        // the skyway: from a facility whose walks are slow, straight up over the crest toward where people need to go,
        // down to a road on the far side
        void LaySkyway()
        {
            Site? bestSite = null;
            (int X, int Y) bestEnd = default;
            double bs = 20;
            for (int a = 0; a < sites.Count; a++)
            {
                for (int b = 0; b < sites.Count; b++)
                {
                    Site sa = sites[a], sb = sites[b];
                    double d = GenMath.Hyp(sa.X - sb.X, sa.Y - sb.Y);
                    if (a == b || d < 2000 * sq || double.IsPositiveInfinity(walk[a][b]) || walk[a][b] / (d / 1.4 / 60) < 1.5) continue;
                    double ux = (sb.X - sa.X) / d, uy = (sb.Y - sa.Y) / d, reach = Math.Min(d, 7000 * sq);
                    double crest = sa.H, crestAt = 0, endT = 0;
                    bool found = false;
                    (int X, int Y) end = default;
                    for (double t = 100; t <= reach; t += cell / 2)
                    {
                        double hh = h[Idx(sa.X + ux * t, sa.Y + uy * t)];
                        if (hh > crest) { crest = hh; crestAt = t; }
                        else if (crestAt != 0 && crest - sa.H > 200 && crest - hh > 200)
                        {
                            double ex = sa.X + ux * t, ey = sa.Y + uy * t;
                            int ei = Idx(ex, ey);
                            bool roadNear = false;
                            int ex0 = ei % nx, ey0 = ei / nx;
                            int rr2 = Math.Max(3, (int)GenMath.Round(700 / cell));
                            for (int yy = Math.Max(0, ey0 - rr2); yy <= Math.Min(ny - 1, ey0 + rr2); yy++)
                                for (int xx = Math.Max(0, ex0 - rr2); xx <= Math.Min(nx - 1, ex0 + rr2); xx++)
                                    if (RoadCell[yy * nx + xx] != 0) roadNear = true;
                            if (roadNear && Dry(ei))
                            {
                                end = ((int)GenMath.Round(ex), (int)GenMath.Round(ey)); endT = t; found = true;
                                break;
                            }
                        }
                    }
                    if (!found) continue;
                    double saved = walk[a][b] - (endT / 5 + 180) / 60 - GenMath.Hyp(sb.X - end.X, sb.Y - end.Y) / 1.1 / 60;
                    if (saved > bs) { bs = saved; bestSite = sa; bestEnd = end; }
                }
            }
            if (bestSite != null)
                MakeLift(bestSite.X, bestSite.Y, bestSite.R, bestSite.Name, bestEnd.X, bestEnd.Y, 0, "skyway station, " + Compass(bestEnd.X, bestEnd.Y), "Pali skyway");
        }

        // street tram: from the control centre through its nearest neighbours, on gentle grades, mostly in the road
        void LayTram()
        {
            if (sites.Find(t => t.Kind == "control") is not { } hub) return;
            List<Site> pool = sites.Where(t => t.Kind != "watch" && t != hub && GenMath.Hyp(t.X - hub.X, t.Y - hub.Y) < 11000 * sq)
                .OrderBy(t => GenMath.Hyp(t.X - hub.X, t.Y - hub.Y)).Take(4).ToList();
            var order = new List<Site> { hub };
            Site cur = hub;
            List<Site> left = pool;
            while (left.Count > 0)
            {
                Site from = cur;
                left = left.OrderBy(t => GenMath.Hyp(t.X - from.X, t.Y - from.Y)).ToList();
                cur = left[0];
                left.RemoveAt(0);
                order.Add(cur);
            }
            double TramCost(int a, int b, int k)
            {
                double d = GenGrid.DL[k] * cell, gr = Math.Abs(h[b] - h[a]) / d;
                double c = gr > 0.06 ? d * 40 : d * (1 + (gr / 0.03) * (gr / 0.03));
                if (_w.IsRiver[b] != 0) c += 300;
                if (PaddockCell[b] >= 0) c += d * 30;
                return RoadCell[b] != 0 ? c * 0.25 : c;
            }
            if (order.Count < 3) return;
            var xs = new List<double>();
            var ys = new List<double>();
            for (int k = 0; k + 1 < order.Count; k++)
            {
                List<int>? path = Route(Idx(order[k].X, order[k].Y), Idx(order[k + 1].X, order[k + 1].Y), TramCost);
                if (path == null) break;
                List<PolyPoint> pts = GenGrid.Chaikin(path.Select(i => new PolyPoint(CxOf(i), CyOf(i), h[i], 0)).ToList(), 2);
                double lp = 0;
                for (int m = 1; m < pts.Count; m++) lp += GenMath.Hyp(pts[m].X - pts[m - 1].X, pts[m].Y - pts[m - 1].Y);
                if (lp > 2.2 * GenMath.Hyp(order[k + 1].X - order[k].X, order[k + 1].Y - order[k].Y))
                {
                    order.RemoveRange(k + 1, order.Count - (k + 1)); // too roundabout: end the line here
                    break;
                }
                foreach (PolyPoint pt in pts) { xs.Add(pt.X); ys.Add(pt.Y); }
                Link(CoarseAt(order[k].X, order[k].Y), CoarseAt(order[k + 1].X, order[k + 1].Y), lp / 7 + 240);
            }
            if (xs.Count == 0) return; // no line at all if the first leg could not be built
            Labels.Add("Tram stop");
            int sl = Labels.Count - 1;
            var stops = new List<TramStop>();
            foreach (Site t in order)
            {
                // a platform beside the line where it leaves the compound
                int bm = 0;
                double bd = 1e30;
                for (int m = 0; m < xs.Count; m++)
                {
                    double d = Math.Abs(GenMath.Hyp(xs[m] - t.X, ys[m] - t.Y) - t.R - 25);
                    if (d < bd) { bd = d; bm = m; }
                }
                int px = (int)GenMath.Round(xs[bm]), py = (int)GenMath.Round(ys[bm]);
                Prims.Add(new Prim { Kind = PrimKind.Rect, X0 = px - 4, Y0 = py - 4, X1 = px + 4, Y1 = py + 4, Cover = Cover.Floor, Label = sl });
                stops.Add(new TramStop(t.Name, px, py));
            }
            double km = 0;
            for (int m = 1; m < xs.Count; m++) km += GenMath.Hyp(xs[m] - xs[m - 1], ys[m] - ys[m - 1]) / 1000;
            Tram = new Tram { Name = "Park tram", Xs = xs.Select(v => (float)v).ToArray(), Ys = ys.Select(v => (float)v).ToArray(), Stops = stops, Km = km };
        }

        // underground monorail: the corporate core, straight under everything
        void LayMonorail()
        {
            Site? gen = sites.Find(t => t.Kind == "genetics");
            var core = new List<Site>();
            foreach (string kind in new[] { "genetics", "control", "lab", "orgone", "nuclear" })
            {
                // of several, the one farthest from the genetics complex (stable)
                IEnumerable<Site> ofKind = sites.Where(t => t.Kind == kind);
                Site? pick;
                if (gen is { } g) pick = ofKind.OrderByDescending(t => GenMath.Hyp(t.X - g.X, t.Y - g.Y)).FirstOrDefault();
                else pick = ofKind.FirstOrDefault();
                if (pick != null) core.Add(pick);
            }
            if (core.Count > 4) core.RemoveRange(4, core.Count - 4);
            if (core.Count < 3) return;
            var order = new List<Site> { core[0] };
            List<Site> left = core.Skip(1).ToList();
            while (left.Count > 0)
            {
                Site cur = order[^1];
                left = left.OrderBy(t => GenMath.Hyp(t.X - cur.X, t.Y - cur.Y)).ToList();
                order.Add(left[0]);
                left.RemoveAt(0);
            }
            var stations = new List<MonorailStation>();
            foreach (Site t in order)
            {
                (int X, int Y) pt = StationPoint(t.X, t.Y, t.R, CenterX, CenterY);
                StationBuilding(pt, "Underground monorail", "Station at " + t.Name, ElevatorRooms);
                stations.Add(new MonorailStation { Name = t.Name, X = pt.X, Y = pt.Y, Z = t.H - 25 });
            }
            Labels.Add("Monorail ventilation shaft");
            int vl = Labels.Count - 1;
            var vents = new List<(int X, int Y)>();
            double km = 0, minCover = 1e9;
            const int NS = 80;
            var gz = new double[NS + 1];
            var zz = new double[NS + 1];
            for (int k = 0; k + 1 < stations.Count; k++)
            {
                MonorailStation a = stations[k], b = stations[k + 1];
                double len = GenMath.Hyp(b.X - a.X, b.Y - a.Y);
                km += len / 1000;
                // the bore stays 20 m under the ground at a grade of 6 % at most: the lower envelope of the ground.
                // Each station reaches it by elevator, however deep.
                for (int m = 0; m <= NS; m++)
                {
                    double f2 = m / (double)NS;
                    int gi = Idx(a.X + (b.X - a.X) * f2, a.Y + (b.Y - a.Y) * f2);
                    gz[m] = (_w.Land[gi] != 0 ? h[gi] : 0) - 20;
                }
                gz[0] = Math.Min(gz[0], a.Z); gz[NS] = Math.Min(gz[NS], b.Z);
                for (int m = 0; m <= NS; m++)
                {
                    double z = gz[m];
                    for (int q = 0; q <= NS; q++) z = Math.Min(z, gz[q] + 0.06 * Math.Abs(q - m) * len / NS);
                    zz[m] = z;
                }
                a.Z = Math.Min(a.Z, zz[0]); b.Z = Math.Min(b.Z, zz[NS]);
                for (int m = 0; m <= NS; m++) minCover = Math.Min(minCover, gz[m] + 20 - zz[m]);
                int nV = (int)Math.Floor(len / 2000);
                for (int m = 1; m <= nV; m++)
                {
                    int vx = (int)GenMath.Round(a.X + (b.X - a.X) * m / (double)(nV + 1)), vy = (int)GenMath.Round(a.Y + (b.Y - a.Y) * m / (double)(nV + 1));
                    if (!Dry(Idx(vx, vy))) continue;
                    vents.Add((vx, vy));
                    Prims.Add(new Prim { Kind = PrimKind.Rect, X0 = vx - 2, Y0 = vy - 2, X1 = vx + 2, Y1 = vy + 2, Cover = Cover.Wall, Label = vl });
                }
                Link(CoarseAt(order[k].X, order[k].Y), CoarseAt(order[k + 1].X, order[k + 1].Y), len / 18 + 150);
            }
            for (int k = 0; k < stations.Count; k++) { stations[k].Shaft = order[k].H - stations[k].Z; stations[k].Power = order[k].Name; }
            // the elevators run on the site's power and need a monorail access key: an objective, kept in a security office
            Site? keySite = gen ?? sites.Find(t => t.Kind == "control");
            if (keySite != null)
            {
                string prefix = keySite.Name + " · ";
                int keyRoom = Labels.FindIndex(l => l.StartsWith(prefix, StringComparison.Ordinal)
                    && (l.Contains("Security", StringComparison.Ordinal) || l.Contains("Offices", StringComparison.Ordinal)));
                Room? rr = null;
                if (keyRoom >= 0)
                {
                    foreach (Prim q in Prims.InOrder())
                    {
                        if (q.Kind != PrimKind.Building) continue;
                        rr = q.Rooms!.Find(r => r.Label == keyRoom);
                        if (rr != null) break;
                    }
                }
                string room = keyRoom >= 0 ? Labels[keyRoom][(Labels[keyRoom].IndexOf(" · ", StringComparison.Ordinal) + 3)..] : "the main building";
                Objectives.Add(new Objective("monorail-key", "Monorail access key", "the monorail station elevators", keySite.Name, room,
                    rr != null ? (rr.X0 + rr.X1) / 2.0 : keySite.X, rr != null ? (rr.Y0 + rr.Y1) / 2.0 : keySite.Y));
            }
            double deepest = double.NegativeInfinity;
            foreach (MonorailStation st in stations) deepest = Math.Max(deepest, st.Shaft);
            Monorail = new Monorail { Name = "Underground monorail", Stations = stations, Vents = vents, Km = km, MinCover = minCover, Deepest = deepest };
        }
    }
}
