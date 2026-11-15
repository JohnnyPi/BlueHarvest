namespace BlueHarvest.Sim.World;

/// <summary>What water can come and go on a tile.</summary>
public enum WaterKind : byte
{
    /// <summary>Nothing: the tile's water (if any) stays as it is.</summary>
    None,
    /// <summary>The sea's edge: beaches, flats and reefs the tide covers and uncovers.</summary>
    Sea,
    /// <summary>A river's bed and banks: deeper, faster and wider when it has rained upstream.</summary>
    River,
}

/// <summary>
/// One tile of the water layer: how high its ground is against the water that can cover it, and what
/// the tile is when the water is off it.
/// </summary>
/// <param name="GroundDm">
/// The ground, in decimetres: above (positive) or below mean sea level for <see cref="WaterKind.Sea"/>,
/// above or below the river's usual surface for <see cref="WaterKind.River"/>.
/// </param>
/// <param name="DryCover">What the tile is with the water off it: sand, bare reef, or the bank's own ground.</param>
/// <param name="River">Which river (an index into the island's rivers), for river tiles; −1 otherwise.</param>
/// <param name="ShoreFacing">For the sea's edge, the way the coast looks out to sea (where surf comes from); null elsewhere.</param>
public readonly record struct WaterTile(WaterKind Kind, short GroundDm, CoverType DryCover, short River, Direction? ShoreFacing)
{
    public static readonly WaterTile None = new(WaterKind.None, 0, CoverType.Grass, -1, null);

    /// <summary>Whether this cover is one the water itself puts here: dry, shallow or deep. Anything else was done by someone.</summary>
    public bool IsWaterState(CoverType cover) =>
        Kind != WaterKind.None && (cover == DryCover || cover is CoverType.ShallowWater or CoverType.DeepWater);
}

/// <summary>
/// The water layer of a map: where tides and floods can reach and how deep they would be. The island
/// fills it (see Generation.IslandTiles); maps loaded from files have none, so their water never moves.
/// </summary>
public sealed class WaterMap
{
    private readonly WaterTile[] _tiles;

    public WaterMap(int width, int height)
    {
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width), "Must be positive.");
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height), "Must be positive.");
        Width = width;
        Height = height;
        _tiles = new WaterTile[width * height];
        Array.Fill(_tiles, WaterTile.None);
    }

    public int Width { get; }
    public int Height { get; }

    public WaterTile this[TilePos p]
    {
        get => _tiles[Index(p)];
        set => _tiles[Index(p)] = value;
    }

    /// <summary>Moves every tile by (−dx, −dy); tiles scrolled in from outside have no water layer. See WorldMap.Scroll.</summary>
    internal void Scroll(int dx, int dy)
    {
        GridScroll.Scroll(_tiles, Width, Height, dx, dy);
        // GridScroll blanks new tiles to default(WaterTile), which already reads as "no water layer"
    }

    private int Index(TilePos p)
    {
        if (p.X < 0 || p.Y < 0 || p.X >= Width || p.Y >= Height)
            throw new ArgumentOutOfRangeException(nameof(p), $"{p} is outside the {Width}x{Height} map.");
        return p.Y * Width + p.X;
    }
}
