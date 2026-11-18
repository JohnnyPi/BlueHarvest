namespace BlueHarvest.Sim.World.Generation;

/// <summary>A whole generated island on the coarse grid: shape, climate and the works of people.</summary>
public sealed class IslandWorld
{
    private IslandWorld(IslandShape shape, IslandClimate climate, IslandWorks works)
    {
        Shape = shape; Climate = climate; Works = works;
    }

    public IslandParams Params => Shape.Params;
    public IslandShape Shape { get; }
    public IslandClimate Climate { get; }
    public IslandWorks Works { get; }

    /// <summary>Island width and height in metres.</summary>
    public double WidthM => Shape.Nx * Shape.Cell;
    public double HeightM => Shape.Ny * Shape.Cell;

    private readonly object _checkGate = new();
    private IslandCheckContext? _measures;
    private IslandValidation? _validation;
    private FacilityAreas? _areas;

    /// <summary>The island divided into facility areas (step 25b-2), worked out on first use and then kept. Thread-safe.</summary>
    public FacilityAreas Areas()
    {
        lock (_checkGate)
        {
            _areas ??= FacilityAreas.Build(this);
            return _areas;
        }
    }

    /// <summary>
    /// The island's measures and key-site routes, worked out on first use and then kept (seed validation,
    /// step 24). Thread-safe. Measuring only reads the island.
    /// </summary>
    public IslandCheckContext Measures()
    {
        lock (_checkGate)
        {
            _measures ??= new IslandCheckContext(IslandStats.Measure(this), Works.Reachability(IslandReach.AnchorKinds));
            return _measures;
        }
    }

    /// <summary>Runs <see cref="IslandChecks.All"/> on the island, once; later calls return the same result.</summary>
    public IslandValidation Validate()
    {
        IslandCheckContext measures = Measures();
        lock (_checkGate)
        {
            _validation ??= IslandChecks.Run(measures);
            return _validation;
        }
    }

    /// <summary>
    /// Generates the island for these settings. The same settings always give the same island.
    /// progress, if given, is told what stage is starting (it may be called from a worker thread).
    /// </summary>
    public static IslandWorld Generate(IslandParams p, Action<string>? progress = null)
    {
        progress?.Invoke("Raising the island");
        IslandShape shape = IslandShape.Generate(p);
        progress?.Invoke("Weather and biomes");
        IslandClimate climate = IslandClimate.Compute(shape);
        progress?.Invoke("Facilities, roads and wildlife");
        IslandWorks works = IslandWorks.Compute(shape, climate);
        return new IslandWorld(shape, climate, works);
    }
}

/// <summary>What a map picture of the island shows.</summary>
public enum MapLayer
{
    /// <summary>Only the island's outline: what a survivor knows on arrival.</summary>
    Outline,
    Biomes,
    Height,
    Rain,
    Regions,
    /// <summary>Each facility's area in its own colour, its ten park-map sheets in two shades, edges darker (step 25b-3).</summary>
    Areas,
}

/// <summary>
/// Pictures of the island grid, one pixel per cell, as RGBA bytes (row-major, 4 bytes per cell).
/// Kept in the simulation library so any front end can show them.
/// </summary>
public static class IslandMapImage
{
    private static readonly (byte R, byte G, byte B) DeepSea = (0x0F, 0x2A, 0x40), ShelfSea = (0x17, 0x3D, 0x57), ReefSea = (0x2F, 0x6F, 0x78),
        LagoonSea = (0x2A, 0x62, 0x7A), LakeBlue = (0x3F, 0x86, 0xAD), OutlineLand = (0x2A, 0x33, 0x2C), OutlineCoast = (0x8F, 0x9C, 0x80), OutlineSea = (0x0B, 0x14, 0x1C);

    private static (byte R, byte G, byte B) Hex(string color) =>
        (Convert.ToByte(color.Substring(1, 2), 16), Convert.ToByte(color.Substring(3, 2), 16), Convert.ToByte(color.Substring(5, 2), 16));

    private static readonly (byte R, byte G, byte B)[] BiomeColors = Legend.Biomes.Select(b => Hex(b.Color)).ToArray();
    private static readonly (byte R, byte G, byte B)[] RegionColors = Legend.Regions.Select(b => Hex(b.Color)).ToArray();

    private static (byte R, byte G, byte B) Ramp((double At, (byte R, byte G, byte B) C)[] stops, double v)
    {
        if (v <= stops[0].At) return stops[0].C;
        for (int k = 1; k < stops.Length; k++)
        {
            if (v > stops[k].At) continue;
            double t = (v - stops[k - 1].At) / (stops[k].At - stops[k - 1].At);
            (byte R, byte G, byte B) a = stops[k - 1].C, b = stops[k].C;
            return ((byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));
        }
        return stops[^1].C;
    }

    private static readonly (double, (byte, byte, byte))[] HeightRamp =
    [
        (0, (0x4E, 0x7A, 0x4A)), (150, (0x86, 0xA0, 0x5A)), (400, (0xC2, 0xB2, 0x6E)), (700, (0xA8, 0x86, 0x5C)), (1000, (0x8C, 0x7A, 0x70)), (1300, (0xE6, 0xE2, 0xDA)),
    ];

    private static readonly (double, (byte, byte, byte))[] RainRamp =
    [
        (400, (0xD9, 0xC0, 0x8A)), (1200, (0xB9, 0xC9, 0x8C)), (2200, (0x6F, 0xB3, 0x9A)), (3500, (0x3E, 0x86, 0xB0)), (6000, (0x2A, 0x3F, 0x8F)),
    ];

    // background, and a brighter "ink" colour for built things so walls and doors read at a glance
    private static readonly (byte R, byte G, byte B)[] CoverBg = CoverLegend.Looks.Select(l => Hex(l.Bg)).ToArray();
    private static readonly (byte R, byte G, byte B)[] CoverFg = CoverLegend.Looks.Select(l => Hex(l.Fg)).ToArray();

    private static bool DrawnInInk(Cover c) => c is Cover.Wall or Cover.BigWall or Cover.Door or Cover.Fence or Cover.Pylon or Cover.Mast or Cover.Ruin
        or Cover.Rack or Cover.Console or Cover.Furniture or Cover.Tank or Cover.Rail or Cover.Elevator or Cover.Wreck;

    /// <summary>A chunk as a picture, one pixel per 1 m tile: each tile's cover colour, shaded by the lie of the land.</summary>
    public static byte[] RenderChunk(IslandChunk chunk)
    {
        int n = chunk.Size * chunk.Size;
        var px = new byte[n * 4];
        for (int i = 0; i < n; i++)
        {
            var cover = (Cover)chunk.Cover[i];
            (byte R, byte G, byte B) col = DrawnInInk(cover) ? CoverFg[(int)cover] : CoverBg[(int)cover];
            double shade = cover <= Cover.Reef || DrawnInInk(cover) ? 1 : chunk.Shade[i];
            px[4 * i] = (byte)Math.Min(255, col.R * shade); px[4 * i + 1] = (byte)Math.Min(255, col.G * shade);
            px[4 * i + 2] = (byte)Math.Min(255, col.B * shade); px[4 * i + 3] = 255;
        }
        return px;
    }

    /// <summary>The picture for a layer. Every layer but the outline and the areas is shaded by the lie of the land.</summary>
    public static byte[] Render(IslandWorld world, MapLayer layer)
    {
        if (layer == MapLayer.Areas)
            return RenderAreas(world);
        IslandShape s = world.Shape;
        IslandClimate c = world.Climate;
        int nx = s.Nx, ny = s.Ny, n = s.N;
        var px = new byte[n * 4];
        for (int i = 0; i < n; i++)
        {
            (byte R, byte G, byte B) col;
            bool land = s.Land[i] != 0;
            if (layer == MapLayer.Outline)
            {
                col = !land ? OutlineSea : s.Coast[i] != CoastClass.None ? OutlineCoast : OutlineLand;
            }
            else if (!land)
            {
                col = s.Sea[i] switch { SeaBand.Reef => ReefSea, SeaBand.Lagoon => LagoonSea, SeaBand.Shelf => ShelfSea, _ => DeepSea };
            }
            else if (!float.IsNaN(s.LakeLevel[i]))
            {
                col = LakeBlue;
            }
            else
            {
                col = layer switch
                {
                    MapLayer.Height => Ramp(HeightRamp, s.Height[i]),
                    MapLayer.Rain => Ramp(RainRamp, s.Rain[i]),
                    MapLayer.Regions => RegionColors[c.Regions[i]],
                    _ => BiomeColors[c.Biomes[i]],
                };
                // light from the north-west
                int x = i % nx, y = i / nx;
                double hx = Math.Max(0, s.Height[y * nx + Math.Min(x + 1, nx - 1)]) - Math.Max(0, s.Height[y * nx + Math.Max(x - 1, 0)]);
                double hy = Math.Max(0, s.Height[Math.Min(y + 1, ny - 1) * nx + x]) - Math.Max(0, s.Height[Math.Max(y - 1, 0) * nx + x]);
                double shade = Math.Clamp(1 - (hx + hy) / (2 * s.Cell) * 1.1, 0.55, 1.35);
                col = ((byte)Math.Min(255, col.R * shade), (byte)Math.Min(255, col.G * shade), (byte)Math.Min(255, col.B * shade));
            }
            px[4 * i] = col.R; px[4 * i + 1] = col.G; px[4 * i + 2] = col.B; px[4 * i + 3] = 255;
        }
        return px;
    }

    /// <summary>
    /// The cells a survivor has on paper: every land cell of every park-map sheet they've charted
    /// (step 25b-3). Sheets that don't exist on this island are ignored.
    /// </summary>
    public static bool[] ChartedCells(IslandWorld world, MapKnowledge known)
    {
        var charted = new bool[world.Shape.N];
        if (known.Sheets.Count == 0)
            return charted;
        FacilityAreas areas = world.Areas();
        foreach (ChartedSheet sheet in known.Sheets)
        {
            if (sheet.Area < 0 || sheet.Area >= areas.Count || sheet.Sheet < 0 || sheet.Sheet >= FacilityAreas.SheetCount)
                continue;
            foreach (int cell in areas.SheetCells(sheet.Area, sheet.Sheet))
                charted[cell] = true;
        }
        return charted;
    }

    /// <summary>
    /// A survivor's island map (step 25b-3): the outline everywhere, as seen from the boat; the land as it
    /// looks (the biome picture) where they have seen it; and a muted version of that where they only have
    /// it from a park map.
    /// </summary>
    public static byte[] RenderKnown(IslandWorld world, MapKnowledge known)
    {
        IslandShape s = world.Shape;
        if (known.Nx != s.Nx || known.Ny != s.Ny)
            throw new ArgumentException("The map knowledge is for an island of another size.", nameof(known));
        byte[] px = Render(world, MapLayer.Outline);
        byte[] land = Render(world, MapLayer.Biomes);
        bool[] charted = ChartedCells(world, known);
        for (int i = 0; i < s.N; i++)
        {
            int o = 4 * i;
            if (known.HasSeen(i))
            {
                px[o] = land[o]; px[o + 1] = land[o + 1]; px[o + 2] = land[o + 2];
            }
            else if (charted[i])
            {
                (px[o], px[o + 1], px[o + 2]) = Muted(land[o], land[o + 1], land[o + 2]);
            }
        }
        return px;
    }

    /// <summary>A colour as a printed map would have it: greyer and darker than the land itself.</summary>
    private static (byte R, byte G, byte B) Muted(byte r, byte g, byte b)
    {
        double grey = (r + g + b) / 3.0;
        static byte Mix(byte c, double m) => (byte)Math.Clamp((c * 0.45 + m * 0.55) * 0.72, 0, 255);
        return (Mix(r, grey), Mix(g, grey), Mix(b, grey));
    }

    /// <summary>A facility area's colour: hues spread by the golden ratio, so neighbours differ; odd sheets a shade darker.</summary>
    private static (byte R, byte G, byte B) AreaColor(int area, int sheet)
    {
        double h = area * 0.6180339887498949 % 1.0 * 6.0, sat = 0.55, val = sheet % 2 == 0 ? 0.88 : 0.70;
        int k = (int)Math.Floor(h);
        double f = h - k, p = val * (1 - sat), q = val * (1 - sat * f), t = val * (1 - sat * (1 - f));
        (double r, double g, double b) = k switch
        {
            0 => (val, t, p),
            1 => (q, val, p),
            2 => (p, val, t),
            3 => (p, q, val),
            4 => (t, p, val),
            _ => (val, p, q),
        };
        return ((byte)Math.Round(r * 255), (byte)Math.Round(g * 255), (byte)Math.Round(b * 255));
    }

    /// <summary>
    /// The facility areas (step 25b-2) as a picture: each area in its colour, its sheets alternately
    /// lighter and darker; cells on the edge of an area much darker, on the edge of a sheet a little.
    /// The sea is the outline's sea.
    /// </summary>
    public static byte[] RenderAreas(IslandWorld world)
    {
        IslandShape s = world.Shape;
        FacilityAreas areas = world.Areas();
        int nx = s.Nx, ny = s.Ny, n = s.N;
        var px = new byte[n * 4];
        for (int i = 0; i < n; i++)
        {
            int a = areas.AreaOf(i);
            (byte R, byte G, byte B) col = OutlineSea;
            if (a >= 0)
            {
                int sheet = areas.SheetOf(i);
                col = AreaColor(a, sheet);
                int x = i % nx, y = i / nx;
                bool areaEdge = false, sheetEdge = false;
                foreach ((int dx, int dy) in Neighbours)
                {
                    int ex = x + dx, ey = y + dy;
                    if (ex < 0 || ey < 0 || ex >= nx || ey >= ny)
                        continue;
                    int j = ey * nx + ex, b = areas.AreaOf(j);
                    if (b >= 0 && b != a) areaEdge = true;
                    else if (b == a && areas.SheetOf(j) != sheet) sheetEdge = true;
                }
                double shade = areaEdge ? 0.45 : sheetEdge ? 0.8 : 1.0;
                col = ((byte)(col.R * shade), (byte)(col.G * shade), (byte)(col.B * shade));
            }
            px[4 * i] = col.R; px[4 * i + 1] = col.G; px[4 * i + 2] = col.B; px[4 * i + 3] = 255;
        }
        return px;
    }

    private static readonly (int Dx, int Dy)[] Neighbours = [(1, 0), (-1, 0), (0, 1), (0, -1)];
}
