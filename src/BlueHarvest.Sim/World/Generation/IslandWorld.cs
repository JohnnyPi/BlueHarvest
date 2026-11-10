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

    /// <summary>The picture for a layer. Every layer but the outline is shaded by the lie of the land.</summary>
    public static byte[] Render(IslandWorld world, MapLayer layer)
    {
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
}
