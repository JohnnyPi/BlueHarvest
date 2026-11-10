namespace BlueHarvest.Sim.World.Generation;

/// <summary>The twelve land biomes (and the sea). Values match the island lab's BIOMES order.</summary>
public enum Biome : byte
{
    Ocean, Strand, Marsh, DryScrub, Grassland, Mesic, Moist, Wet, Cloud, Ridge, Cliff, Lava, Lake,
}

/// <summary>The design extract's eight regions (0 = sea). Each will bias the director differently.</summary>
public enum Region : byte
{
    Sea, Windward, Ridge, Plateau, Leeward, Wetland, Vent, Coast, Crater,
}

/// <summary>Display name and map colour of a biome or region.</summary>
public readonly record struct LegendEntry(string Name, string Color);

public static class Legend
{
    public static readonly LegendEntry[] Biomes =
    [
        new("Ocean", "#14304a"), new("Beach and strand", "#e3cf98"), new("Marsh and wetland", "#5d9483"),
        new("Dry scrub", "#c2a067"), new("Grassland", "#bcc47c"), new("Open woodland", "#8cab5e"),
        new("Jungle", "#4e8f4d"), new("Rainforest", "#2d6a42"), new("Cloud forest", "#5f9a8e"),
        new("Ridge shrub", "#8f9c80"), new("Cliff and bare rock", "#9d968b"), new("Young lava", "#4b403b"),
        new("Lake", "#3f86ad"),
    ];

    public static readonly LegendEntry[] Regions =
    [
        new("Sea", "#14304a"), new("Windward valleys", "#3f8a57"), new("Ridgelines and pali", "#7d8f9c"),
        new("Central plateau", "#c9b96e"), new("Leeward dry side", "#d39a5c"), new("Wetlands and stream mouths", "#4fa39a"),
        new("The vent", "#b4523c"), new("Coast and reef", "#e8d9a8"), new("Crater basin", "#8a6fb0"),
    ];
}

/// <summary>Where a region's name goes on the map: the centre of one of its three largest patches.</summary>
public sealed record RegionLabel(Region Region, string Name, double X, double Y, double Km2);

/// <summary>
/// Stage 3 of island generation: temperature, the cloud deck, wetness and crests, then biomes and regions.
/// </summary>
public sealed class IslandClimate
{
    private IslandClimate(int n)
    {
        Temperature = new float[n]; Fog = new float[n]; Wetness = new float[n]; Crest = new float[n]; SlopeDeg = new float[n];
        NearLake = new byte[n]; Biomes = new byte[n]; Regions = new byte[n];
    }

    /// <summary>Mean air temperature, °C.</summary>
    public float[] Temperature { get; }
    /// <summary>Cloud-deck cover, 0–1.</summary>
    public float[] Fog { get; }
    /// <summary>Topographic wetness, about 0–1.7.</summary>
    public float[] Wetness { get; }
    /// <summary>How far a cell stands above its surroundings, 0–1.</summary>
    public float[] Crest { get; }
    public float[] SlopeDeg { get; }
    public byte[] NearLake { get; }
    /// <summary><see cref="Biome"/> of each cell.</summary>
    public byte[] Biomes { get; }
    /// <summary><see cref="Region"/> of each cell, smoothed into readable areas.</summary>
    public byte[] Regions { get; }
    public double[] BiomeKm2 { get; } = new double[Legend.Biomes.Length];
    public double[] RegionKm2 { get; } = new double[Legend.Regions.Length];
    public List<RegionLabel> RegionLabels { get; } = [];
    public double TempMin { get; private set; }
    public double TempMax { get; private set; }

    /// <summary>
    /// One rule for the island grid and for every 1 m tile, so the two always agree.
    /// j is a jitter in about −1..1 that makes boundaries ragged.
    /// </summary>
    public static Biome Classify(double h, double rain, double temp, double fog, double wet, double slope, double lava, double dc, bool beachy, double crest, double j)
    {
        if (lava > 0.62 + 0.08 * j) return Biome.Lava;
        if (slope > 40 + 6 * j) return Biome.Cliff;
        if (beachy && dc < 150 + 70 * j && h < 7) return Biome.Strand;
        if (wet > 0.62 - 0.1 * j && h < 40 && slope < 3) return Biome.Marsh;
        if (fog > 0.5 + 0.12 * j && rain > 2200) return Biome.Cloud;
        if (crest > 0.55 + 0.15 * j && h > 550) return Biome.Ridge;
        // aridity: rain against what warm air evaporates (less up high, where it is cooler)
        double ai = rain * (1 + 0.1 * j) / (1300 * (0.4 + 0.6 * temp / 24));
        if (ai > 2.7) return Biome.Wet;
        if (ai > 1.6) return Biome.Moist;
        if (ai > 1.0) return Biome.Mesic;
        if (ai > 0.75) return slope < 14 ? Biome.Grassland : Biome.DryScrub;
        return slope < 4 && ai > 0.62 ? Biome.Grassland : Biome.DryScrub;
    }

    public static IslandClimate Compute(IslandShape w)
    {
        IslandParams p = w.Params;
        IslandAnchors a = w.Anchors;
        int nx = w.Nx, ny = w.Ny, n = w.N;
        double cell = w.Cell, area = cell * cell;
        double[] h = w.Height, rain = w.Rain;
        byte[] land = w.Land;
        float[] lake = w.LakeLevel;
        DrainageTree tr = w.Tree;
        var c = new IslandClimate(n);

        var up = new double[n]; // upslope area, for the wetness index
        for (int k = 0; k < tr.StackCount; k++) up[tr.Stack[k]] = area;
        for (int k = tr.StackCount - 1; k >= 0; k--)
        {
            int i = tr.Stack[k], r = tr.Receiver[i];
            if (r >= 0) up[r] += up[i];
        }
        var blurH = new double[n];
        for (int i = 0; i < n; i++) blurH[i] = land[i] != 0 ? Math.Max(0, h[i]) : 0;
        GenGrid.BoxBlur(blurH, nx, ny, Math.Max(1, (int)GenMath.Round(450 / cell)), 2);
        for (int i = 0; i < n; i++)
        {
            if (float.IsNaN(lake[i])) continue;
            int x = i % nx, y = i / nx;
            for (int k = 0; k < 8; k++)
            {
                int xx = x + GenGrid.DX[k], yy = y + GenGrid.DY[k];
                if (xx >= 0 && yy >= 0 && xx < nx && yy < ny) c.NearLake[yy * nx + xx] = 1;
            }
        }

        double tMin = 99, tMax = -99;
        for (int i = 0; i < n; i++)
        {
            int x = i % nx, y = i / nx;
            double sx = (Math.Max(0, h[y * nx + Math.Min(x + 1, nx - 1)]) - Math.Max(0, h[y * nx + Math.Max(x - 1, 0)])) / (2 * cell);
            double sy = (Math.Max(0, h[Math.Min(y + 1, ny - 1) * nx + x]) - Math.Max(0, h[Math.Max(y - 1, 0) * nx + x])) / (2 * cell);
            double tan = GenMath.Hyp(sx, sy);
            c.SlopeDeg[i] = (float)(GenMath.Atan(tan) * 180 / GenMath.Pi);
            if (land[i] == 0) { c.Temperature[i] = (float)p.SeaTemp; continue; }
            double hh = Math.Max(0, float.IsNaN(lake[i]) ? h[i] : lake[i]);
            c.Temperature[i] = (float)(p.SeaTemp - 6.5 * hh / 1000); // lapse rate
            tMin = Math.Min(tMin, c.Temperature[i]); tMax = Math.Max(tMax, c.Temperature[i]);
            // the trade-wind cloud deck sits on wet slopes above the cloud base
            c.Fog[i] = (float)(GenMath.SmoothStep(p.CloudBase - 200, p.CloudBase + 250, hh) * GenMath.Clamp((rain[i] - 1500) / 2500, 0, 1));
            double twi = GenMath.Log(up[i] / cell / Math.Max(tan, 0.004));
            double wv = GenMath.Clamp((twi - 8) / 5, 0, 1.3) * GenMath.Clamp(rain[i] / 1500, 0.45, 1.2) * p.Wetlands;
            if (c.NearLake[i] != 0 && tan < 0.05) wv += 0.4;
            c.Wetness[i] = (float)wv;
            c.Crest[i] = (float)GenMath.Clamp((h[i] - blurH[i]) / 60, 0, 1);
        }
        c.TempMin = tMin; c.TempMax = tMax;

        LineSegment sg = a.Saddle.Segment;
        for (int i = 0; i < n; i++)
        {
            if (land[i] == 0) { c.Biomes[i] = (byte)Biome.Ocean; continue; }
            double x = (i % nx + 0.5) * cell, y = (i / nx + 0.5) * cell;
            if (!float.IsNaN(lake[i])) c.Biomes[i] = (byte)Biome.Lake;
            else
            {
                double jt = GenMath.Clamp(2 * GenNoise.Fbm(a.NBiome, x / 2500, y / 2500, 3), -1, 1);
                c.Biomes[i] = (byte)Classify(Math.Max(0, h[i]), rain[i], c.Temperature[i], c.Fog[i], c.Wetness[i], c.SlopeDeg[i],
                    w.Lava[i], w.DistLand[i], w.Coast[i] == CoastClass.Beach, c.Crest[i], jt);
            }
            c.BiomeKm2[c.Biomes[i]] += area / 1e6;
            // regions, highest priority first
            Region rg;
            double t0 = ((x - sg.Ax) * sg.Dx + (y - sg.Ay) * sg.Dy) / sg.L2;
            double perp = Math.Abs(sg.Dx * (y - sg.Ay) - sg.Dy * (x - sg.Ax)) / sg.Len;
            if (GenMath.Hyp(x - a.Caldera.X, y - a.Caldera.Y) < a.Caldera.R * 1.15) rg = Region.Crater;
            else if (GenMath.Hyp(x - a.Vent.X, y - a.Vent.Y) < a.Vent.R * 1.7 || w.Lava[i] > 0.6) rg = Region.Vent;
            else if (c.Biomes[i] == (byte)Biome.Marsh) rg = Region.Wetland;
            else if (c.Wetness[i] > 0.45 && h[i] < 25 && c.SlopeDeg[i] < 4) rg = Region.Wetland;
            else if (w.DistLand[i] < 500 && h[i] < 40) rg = Region.Coast;
            else if (h[i] > 0.55 * p.Peak || (w.PaliCut[i] > 0.3 && c.SlopeDeg[i] > 20)) rg = Region.Ridge;
            else if (t0 > 0.12 && t0 < 0.88 && perp < a.Saddle.W * 0.8 && h[i] < p.Saddle + 300) rg = Region.Plateau;
            else if (rain[i] > 1800) rg = Region.Windward;
            else rg = Region.Leeward;
            c.Regions[i] = (byte)rg;
        }

        // smooth regions into readable areas (majority vote), keeping the crater and the vent
        {
            byte[] region = c.Regions;
            int rr = Math.Max(1, (int)GenMath.Round(256 / cell));
            var votes = new int[Legend.Regions.Length];
            var tmp = new byte[n];
            for (int pass = 0; pass < 2; pass++)
            {
                for (int i = 0; i < n; i++)
                {
                    if (land[i] == 0 || region[i] == (byte)Region.Crater || region[i] == (byte)Region.Vent) { tmp[i] = region[i]; continue; }
                    Array.Clear(votes);
                    int x = i % nx, y = i / nx;
                    for (int yy = Math.Max(0, y - rr); yy <= Math.Min(ny - 1, y + rr); yy++)
                        for (int xx = Math.Max(0, x - rr); xx <= Math.Min(nx - 1, x + rr); xx++)
                            votes[region[yy * nx + xx]]++;
                    votes[0] = 0;
                    int best = region[i];
                    for (int k = 1; k < votes.Length; k++) if (votes[k] > votes[best]) best = k;
                    tmp[i] = (byte)best;
                }
                Array.Copy(tmp, region, n);
            }
        }

        // region areas and label points
        {
            var lab2 = new int[n];
            for (int r = 1; r < Legend.Regions.Length; r++)
            {
                var mask = new byte[n];
                bool any = false;
                for (int i = 0; i < n; i++)
                {
                    if (c.Regions[i] != r) continue;
                    mask[i] = 1; any = true; c.RegionKm2[r] += area / 1e6;
                }
                if (!any) continue;
                List<int> sizes = GenGrid.Components(mask, nx, ny, lab2);
                // largest three patches; stable, so equal sizes keep label order
                int[] order = Enumerable.Range(0, sizes.Count).OrderByDescending(k => sizes[k]).Take(3).ToArray();
                var sx = new double[sizes.Count];
                var sy = new double[sizes.Count];
                for (int i = 0; i < n; i++)
                {
                    int l = lab2[i];
                    if (l >= 0) { sx[l] += i % nx; sy[l] += i / nx; }
                }
                for (int idx = 0; idx < order.Length; idx++)
                {
                    int k = order[idx], sz = sizes[k];
                    double km2 = sz * area / 1e6;
                    if (idx == 0 ? km2 >= 1 : km2 >= 40)
                        c.RegionLabels.Add(new RegionLabel((Region)r, Legend.Regions[r].Name, (sx[k] / sz + 0.5) * cell, (sy[k] / sz + 0.5) * cell, km2));
                }
            }
        }
        return c;
    }
}
