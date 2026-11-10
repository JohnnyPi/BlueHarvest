namespace BlueHarvest.Sim.World.Generation;

/// <summary>One straight piece of a polyline, with its start distance along the line.</summary>
public readonly record struct LineSegment(double Ax, double Ay, double Dx, double Dy, double L2, double Len, double C0);

/// <summary>A polyline cut into segments, for nearest-point queries.</summary>
public sealed class Polyline
{
    public Polyline(double[] xs, double[] ys)
    {
        Xs = xs; Ys = ys;
        var segs = new LineSegment[xs.Length - 1];
        double c0 = 0;
        for (int k = 0; k + 1 < xs.Length; k++)
        {
            double dx = xs[k + 1] - xs[k], dy = ys[k + 1] - ys[k], l2 = dx * dx + dy * dy, len = Math.Sqrt(l2);
            segs[k] = new LineSegment(xs[k], ys[k], dx, dy, l2, len, c0);
            c0 += len;
        }
        Segments = segs;
        Total = c0;
    }

    public double[] Xs { get; }
    public double[] Ys { get; }
    public LineSegment[] Segments { get; }
    public double Total { get; }

    /// <summary>The point a fraction f of the way along the line.</summary>
    public (double X, double Y) PointAlong(double f)
    {
        double target = f * Total;
        foreach (LineSegment g in Segments)
        {
            if (target <= g.Len)
            {
                double t = target / g.Len;
                return (g.Ax + g.Dx * t, g.Ay + g.Dy * t);
            }
            target -= g.Len;
        }
        return (Xs[^1], Ys[^1]);
    }
}

/// <summary>A volcanic shield: a ridge line with a crest profile, flanks and an optional pali on the windward side.</summary>
public sealed class Shield
{
    public required string Name { get; init; }
    public required Polyline Ridge { get; init; }
    public required double Peak { get; init; }
    /// <summary>Flank half-width on the NE (+) and SW (−) sides of the ridge.</summary>
    public required double WPos { get; init; }
    public required double WNeg { get; init; }
    public required bool Old { get; init; }
    public required double S { get; init; }
    /// <summary>+1 if the pali is on the + side of the ridge, else −1.</summary>
    public required double PaliSign { get; init; }
    public required double PaliStrength { get; init; }
    /// <summary>Where the wall stands, as a fraction of the flank width from the crest.</summary>
    public required double PaliAt { get; init; }
    public required uint PaliNoise { get; init; }
}

public sealed record Saddle(LineSegment Segment, double W, double Crest);

public sealed record Caldera(double X, double Y, double R, double Depth, double Rim);

public sealed record Vent(double X, double Y, double H, double R);

public sealed record Islet(double X, double Y, double H, double R);

/// <summary>Elevation and weights at one point of the base field.</summary>
public struct LandSample
{
    /// <summary>Base elevation in metres (negative offshore).</summary>
    public double E;
    /// <summary>How much the pali cut lowered this point, 0–1.</summary>
    public double Pali;
    /// <summary>Weight of the older shield, 0–1.</summary>
    public double Old;
    /// <summary>Weight of young lava from the vent, 0–1.</summary>
    public double Lava;
}

/// <summary>
/// The global anchors of an island, drawn once per seed: two shields, the saddle, caldera, vent,
/// islets and the noise streams. The base elevation at any point is a pure function of these.
/// </summary>
public sealed class IslandAnchors
{
    public const double OahuW = 86000, OahuH = 62000;

    private IslandAnchors() { }

    public double S { get; private init; }
    public double W { get; private init; }
    public double H { get; private init; }
    /// <summary>Unit vector along the shields (NW → SE) and across them (SW → NE).</summary>
    public (double X, double Y) Along { get; private init; }
    public (double X, double Y) Across { get; private init; }
    public Shield OldShield { get; private init; } = null!;
    public Shield YoungShield { get; private init; } = null!;
    public Saddle Saddle { get; private init; } = null!;
    public Caldera Caldera { get; private init; } = null!;
    public Vent Vent { get; private init; } = null!;
    public List<Islet> Islets { get; } = [];
    public double Peak { get; private init; }
    public double Rough { get; private init; }
    public double Warp { get; private init; }

    public uint NWarpX { get; private init; }
    public uint NWarpY { get; private init; }
    public uint NRidge { get; private init; }
    public uint NDetail { get; private init; }
    public uint NCoast { get; private init; }
    public uint NLava { get; private init; }
    public uint NErod { get; private init; }
    public uint NCliff { get; private init; }
    public uint NBiome { get; private init; }
    public uint NReef { get; private init; }
    public uint NTile { get; private init; }
    public uint NTile2 { get; private init; }
    public uint NCover { get; private init; }

    public static IslandAnchors Create(IslandParams p)
    {
        double s = p.Scale / 100;
        var r = new GenRng(p.Seed, "worldgen.anchors");
        double w = OahuW * s, h = OahuH * s;
        double th = (38 + (r.Next() - 0.5) * 24) * GenMath.Pi / 180;
        double dX = GenMath.Cos(th), dY = GenMath.Sin(th);
        double nX = GenMath.Sin(th), nY = -GenMath.Cos(th);
        double cX = w * (0.5 + (r.Next() - 0.5) * 0.05);
        double cY = h * (0.5 + (r.Next() - 0.5) * 0.05);
        double sep = (20000 + r.Next() * 6000) * s;

        Polyline Ridge(double cx, double cy, double len, double bend, int count)
        {
            var xs = new double[count];
            var ys = new double[count];
            for (int k = 0; k < count; k++)
            {
                double t = (double)k / (count - 1) - 0.5;
                double off = bend * (1 - 4 * t * t) + (r.Next() - 0.5) * 2400 * s;
                xs[k] = cx + dX * t * len + nX * off;
                ys[k] = cy + dY * t * len + nY * off;
            }
            return new Polyline(xs, ys);
        }

        double windA = p.Wind * GenMath.Pi / 180, wfX = GenMath.Sin(windA), wfY = -GenMath.Cos(windA);
        double wn = wfX * nX + wfY * nY;

        Shield MakeShield(string name, double cx, double cy, double len, double bend, int count, double peak, double wPos, double wNeg, bool old)
        {
            Polyline ridge = Ridge(cx, cy, len, bend, count);
            return new Shield
            {
                Name = name, Ridge = ridge, Peak = peak, WPos = wPos, WNeg = wNeg, Old = old, S = s,
                PaliSign = wn >= 0 ? 1 : -1,
                PaliStrength = p.Pali * GenMath.Clamp(Math.Abs(wn) * 1.4, 0, 1) * (old ? 0.55 : 1),
                PaliAt = old ? 0.2 : 0.08,
                PaliNoise = GenHash.Stream(p.Seed, "worldgen.pali." + name),
            };
        }

        double oa = ((r.Next() - 0.5) * 7000 - 3500) * s, ya = ((r.Next() - 0.5) * 7000 + 2500) * s;
        double oldCX = cX - nX * sep / 2 + dX * oa, oldCY = cY - nY * sep / 2 + dY * oa;
        double youngCX = cX + nX * sep / 2 + dX * ya, youngCY = cY + nY * sep / 2 + dY * ya;
        // Argument order matters: each r.Next() is drawn in the order the reference draws it.
        Shield oldShield = MakeShield("older shield", oldCX, oldCY, (27000 + r.Next() * 8000) * s, (r.Next() - 0.5) * 5000 * s, 4,
            p.Peak * (0.8 + r.Next() * 0.1), (6000 + r.Next() * 2000) * s, (8000 + r.Next() * 2000) * s, true);
        Shield youngShield = MakeShield("younger shield", youngCX, youngCY, (40000 + r.Next() * 10000) * s, (r.Next() - 0.5) * 6000 * s, 5,
            p.Peak, (5500 + r.Next() * 2000) * s, (7500 + r.Next() * 2000) * s, false);

        var saddleLine = new Polyline([oldCX, youngCX], [oldCY, youngCY]);
        var saddle = new Saddle(saddleLine.Segments[0], (6500 + r.Next() * 2500) * s, p.Saddle);
        (double cpX, double cpY) = oldShield.Ridge.PointAlong(0.35 + r.Next() * 0.3);
        double calR = (1700 + r.Next() * 1000) * s;
        var caldera = new Caldera(cpX, cpY, calR, oldShield.Peak * (0.2 + r.Next() * 0.08), 45);
        int yl = youngShield.Ridge.Xs.Length - 1;
        double vo = (2000 + r.Next() * 3000) * s;
        double ventX = youngShield.Ridge.Xs[yl] + dX * 1500 * s - nX * vo;
        double ventY = youngShield.Ridge.Ys[yl] + dY * 1500 * s - nY * vo;
        double ventH = 220 + r.Next() * 220;
        var vent = new Vent(ventX, ventY, ventH, (2400 + r.Next() * 1400) * s);

        uint seed = p.Seed;
        var a = new IslandAnchors
        {
            S = s, W = w, H = h, Along = (dX, dY), Across = (nX, nY),
            OldShield = oldShield, YoungShield = youngShield, Saddle = saddle, Caldera = caldera, Vent = vent,
            Peak = p.Peak, Rough = p.Rough, Warp = p.Warp * 1000 * s,
            NWarpX = GenHash.Stream(seed, "worldgen.warpx"), NWarpY = GenHash.Stream(seed, "worldgen.warpy"),
            NRidge = GenHash.Stream(seed, "worldgen.ridge"), NDetail = GenHash.Stream(seed, "worldgen.detail"),
            NCoast = GenHash.Stream(seed, "worldgen.coast"), NLava = GenHash.Stream(seed, "worldgen.lava"),
            NErod = GenHash.Stream(seed, "worldgen.erod"), NCliff = GenHash.Stream(seed, "worldgen.cliff"),
            NBiome = GenHash.Stream(seed, "worldgen.biome"), NReef = GenHash.Stream(seed, "worldgen.reef"),
            NTile = GenHash.Stream(seed, "chunk.detail"), NTile2 = GenHash.Stream(seed, "chunk.fine"),
            NCover = GenHash.Stream(seed, "chunk.cover"),
        };

        // Islets: rejection sampling on the base field (offshore, not too far out).
        // Each accepted islet already shapes the field the next candidate is tested on.
        var ri = new GenRng(seed, "worldgen.islets");
        int count = 3 + (int)Math.Floor(ri.Next() * 6);
        double margin = 9000 * s;
        LandSample o = default;
        for (int k = 0, tries = 0; k < count && tries < 600; tries++)
        {
            double x = margin + ri.Next() * (w - 2 * margin), y = margin + ri.Next() * (h - 2 * margin);
            a.Sample(x, y, ref o);
            if (o.E > -40 || o.E < -380) continue;
            bool ok = true;
            foreach (Islet q in a.Islets)
                if (GenMath.Hyp(q.X - x, q.Y - y) < 3500 * s) ok = false;
            if (!ok) continue;
            double ih = 25 + ri.Next() * 140;
            a.Islets.Add(new Islet(x, y, ih, (500 + ri.Next() * 1300) * s));
            k++;
        }
        return a;
    }

    /// <summary>Height of a shield at a point, and how much its pali cut lowered it there.</summary>
    public static double ShieldAt(Shield sh, double x, double y, out double paliCut)
    {
        double best = 1e30, sd = 0, tpos = 0;
        foreach (LineSegment g in sh.Ridge.Segments)
        {
            double t = ((x - g.Ax) * g.Dx + (y - g.Ay) * g.Dy) / g.L2;
            t = t < 0 ? 0 : t > 1 ? 1 : t;
            double px = g.Ax + g.Dx * t - x, py = g.Ay + g.Dy * t - y, d2 = px * px + py * py;
            if (d2 < best)
            {
                best = d2;
                sd = -(g.Dx * (y - g.Ay) - g.Dy * (x - g.Ax)) / g.Len; // + on the NE side
                tpos = (g.C0 + t * g.Len) / sh.Ridge.Total - 0.5;
            }
        }
        double dist = Math.Sqrt(best);
        double f = 0.5 + 0.5 * GenMath.Tanh(sd / 2500);
        double w = sh.WNeg + (sh.WPos - sh.WNeg) * f;
        double crest = sh.Peak * (1 - 1.5 * tpos * tpos);
        double xr = dist / w;
        paliCut = 0;
        if (xr >= 1) return -(xr - 1) * crest * 0.35;
        double e = crest * GenMath.Pow(1 - xr, 1.5);
        if (sh.PaliStrength > 0)
        {
            double fw = 0.5 + 0.5 * GenMath.Tanh(sh.PaliSign * sd / 500); // how much this point is on the windward flank
            if (fw > 0.02)
            {
                // the wall line wanders and bites back in amphitheatre-headed scallops
                double along = (tpos + 0.5) * sh.Ridge.Total;
                double sc = GenNoise.Noise(sh.PaliNoise, along / (2600 * sh.S), 3.7) * 0.55
                    + Math.Abs(GenNoise.Noise(sh.PaliNoise ^ 0x77u, along / (900 * sh.S), 1.3)) * 0.45;
                double xc = sh.PaliAt + 0.12 * sc, wallN = 420 * Math.Sqrt(sh.S) / w;
                double m = GenMath.SmoothStep(xc, xc + wallN, xr);
                if (m > 0)
                {
                    double top = crest * GenMath.Pow(1 - xc, 1.5), u = (xr - xc) / (1 - xc);
                    double apron = top * 0.13 * GenMath.Pow(Math.Max(0, 1 - u), 1.6) + e * 0.12; // talus apron and a low coastal plain
                    if (apron < e)
                    {
                        double k = m * sh.PaliStrength * fw;
                        e -= (e - apron) * k;
                        paliCut = k;
                    }
                }
            }
        }
        return e;
    }

    public static double SaddleAt(Saddle sa, double x, double y)
    {
        LineSegment g = sa.Segment;
        double t0 = ((x - g.Ax) * g.Dx + (y - g.Ay) * g.Dy) / g.L2, t = t0 < 0 ? 0 : t0 > 1 ? 1 : t0;
        // the saddle only fills between the shields: past either centre it falls away four times faster
        double over = (t0 < 0 ? -t0 : t0 > 1 ? t0 - 1 : 0) * g.Len * 3;
        double perp = Math.Abs(g.Dx * (y - g.Ay) - g.Dy * (x - g.Ax)) / g.Len;
        double xr = GenMath.Hyp(perp, over + GenMath.Hyp(g.Ax + g.Dx * t - x, g.Ay + g.Dy * t - y) * 0) / sa.W;
        return xr < 1 ? sa.Crest * GenMath.Pow(1 - xr, 0.9) : -(xr - 1) * sa.Crest * 0.5;
    }

    /// <summary>Base height (m), pali cut, older-shield weight and young-lava weight at a point (metres).</summary>
    public void Sample(double x, double y, ref LandSample result)
    {
        double s = S, lw = 16000 * s;
        double wx = x + Warp * GenNoise.Fbm(NWarpX, x / lw, y / lw, 3);
        double wy = y + Warp * GenNoise.Fbm(NWarpY, x / lw, y / lw, 3);
        double eo = ShieldAt(OldShield, wx, wy, out double paliOld), ey = ShieldAt(YoungShield, wx, wy, out double paliYoung);
        double e = GenMath.SMax(eo, ey, 400);
        e = GenMath.SMax(e, SaddleAt(Saddle, wx, wy), 250);
        Caldera c = Caldera;
        double rr = GenMath.Hyp(wx - c.X, wy - c.Y) / c.R;
        if (rr < 2)
        {
            if (rr < 1) e -= c.Depth * (1 - rr * rr);
            double q = (rr - 1) / 0.3;
            e += c.Rim * GenMath.Exp(-q * q);
        }
        Vent v = Vent;
        rr = GenMath.Hyp(wx - v.X, wy - v.Y) / v.R;
        double lava = GenMath.Clamp(1.15 - rr / 2.2 + 0.35 * GenNoise.Fbm(NLava, x / (1800 * s), y / (1800 * s), 3), 0, 1);
        if (rr < 1)
        {
            e += v.H * GenMath.Pow(1 - rr, 1.6);
            double rc = rr / 0.16;
            if (rc < 1) e -= v.H * 0.3 * (1 - rc * rc);
        }
        double mount = GenMath.Clamp(e / Peak, 0, 1);
        e += Peak * 0.16 * mount * (GenNoise.Ridged(NRidge, wx / (6500 * s), wy / (6500 * s), 4) - 0.35);
        e += 18 * GenNoise.Fbm(NDetail, x / (1400 * s), y / (1400 * s), 3) * (0.3 + mount);
        e += Rough * 220 * GenNoise.Fbm(NCoast, wx / (9000 * s), wy / (9000 * s), 4);
        e += Rough * 70 * GenNoise.Fbm(NCoast ^ 0x2c1bu, x / (2600 * s), y / (2600 * s), 3);
        foreach (Islet isl in Islets)
        {
            double ri = GenMath.Hyp(x - isl.X, y - isl.Y) / isl.R;
            if (ri < 4) e = GenMath.SMax(e, ri < 1 ? isl.H * GenMath.Pow(1 - ri, 1.3) : -(ri - 1) * 180, 120);
        }
        double de = Math.Min(Math.Min(Math.Min(x, y), W - x), H - y), m = 7000 * s;
        if (de < m)
        {
            double q = 1 - de / m;
            e -= 3500 * q * q;
        }
        result.E = e;
        result.Pali = eo > ey ? paliOld : paliYoung;
        result.Old = 1 / (1 + GenMath.Exp(-(eo - ey) / 150));
        result.Lava = lava;
    }
}
