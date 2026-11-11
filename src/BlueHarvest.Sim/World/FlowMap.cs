namespace BlueHarvest.Sim.World;

/// <summary>
/// Which way the water on a tile runs and how fast: east and south components in decimetres per
/// second. <see cref="None"/> on dry ground and on still water (lakes, the sea).
/// </summary>
public readonly record struct WaterFlow(sbyte X, sbyte Y)
{
    public static readonly WaterFlow None = default;

    public bool IsNone => X == 0 && Y == 0;

    /// <summary>Speed in decimetres per second, rounded down.</summary>
    public int SpeedDm => (int)Math.Sqrt(X * X + Y * Y);
}

/// <summary>
/// The current on every tile of a map. The island fills it from its rivers (see
/// Generation.IslandTiles); maps loaded from files have still water. Nothing in the rules reads it
/// yet: for now it tells the front end which way to draw the water moving.
/// </summary>
public sealed class FlowMap
{
    private readonly WaterFlow[] _flow;

    public FlowMap(int width, int height)
    {
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width), "Must be positive.");
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height), "Must be positive.");
        Width = width;
        Height = height;
        _flow = new WaterFlow[width * height];
    }

    public int Width { get; }
    public int Height { get; }

    public WaterFlow this[TilePos p]
    {
        get => _flow[Index(p)];
        set => _flow[Index(p)] = value;
    }

    /// <summary>Moves every tile by (−dx, −dy); tiles scrolled in from outside have no current. See WorldMap.Scroll.</summary>
    internal void Scroll(int dx, int dy) => GridScroll.Scroll(_flow, Width, Height, dx, dy);

    private int Index(TilePos p)
    {
        if (p.X < 0 || p.Y < 0 || p.X >= Width || p.Y >= Height)
            throw new ArgumentOutOfRangeException(nameof(p), $"{p} is outside the {Width}x{Height} map.");
        return p.Y * Width + p.X;
    }
}
