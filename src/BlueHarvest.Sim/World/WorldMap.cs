namespace BlueHarvest.Sim.World;

/// <summary>All per-tile layers of a map: terrain heights, ground cover and structures.</summary>
public sealed class WorldMap
{
    public WorldMap(int width, int height)
        : this(new HeightMap(width, height), new CoverMap(width, height))
    {
    }

    public WorldMap(HeightMap heights, CoverMap cover, StructureMap? structures = null)
    {
        if (heights.Width != cover.Width || heights.Height != cover.Height)
            throw new ArgumentException("Height and cover layers must be the same size.");
        structures ??= new StructureMap(heights.Width, heights.Height);
        if (structures.Width != heights.Width || structures.Height != heights.Height)
            throw new ArgumentException("The structure layer must be the same size as the map.");
        Heights = heights;
        Cover = cover;
        Structures = structures;
    }

    public HeightMap Heights { get; }
    public CoverMap Cover { get; }
    public StructureMap Structures { get; }
    public int Width => Heights.Width;
    public int Height => Heights.Height;

    public bool InBounds(TilePos p) => Heights.InBounds(p);

    /// <summary>
    /// Scrolls every layer: what was at (x, y) is now at (x − dx, y − dy), and the tiles that scroll in
    /// from outside are blank (height 0, grass, no structures) until something fills them.
    /// A streamed map uses this to follow the player; see Generation.IslandWindow.
    /// </summary>
    public void Scroll(int dx, int dy)
    {
        Heights.Scroll(dx, dy);
        Cover.Scroll(dx, dy);
        Structures.Scroll(dx, dy);
    }

    /// <summary>Absolute height of the deck surface on a tile (ground plus deck), in decimeters.</summary>
    public int DeckTop(TilePos p) => Heights[p] + Structures.DeckDm(p);

    /// <summary>Absolute height of a tile's ground or, with <paramref name="onDeck"/>, its deck.</summary>
    public int SurfaceHeight(TilePos p, bool onDeck) => onDeck ? DeckTop(p) : Heights[p];

    /// <summary>Under a roof.</summary>
    public bool IsIndoor(TilePos p) => Structures.IsRoofed(p);

    /// <summary>
    /// The rules that apply on a tile now: its cover, except that a doorway with a closed or locked
    /// door behaves like part of the wall. Use this, not Covers.Get(Cover[p]), for anything that moves,
    /// sees or hears.
    /// </summary>
    public CoverInfo CoverAt(TilePos p)
    {
        var type = Cover[p];
        if (type == CoverType.Door)
        {
            return Structures.DoorAt(p) switch
            {
                DoorState.Closed => Covers.ClosedDoor,
                DoorState.Locked => Covers.LockedDoor,
                _ => Covers.Get(type),
            };
        }
        return Covers.Get(type);
    }
}

/// <summary>Scrolls a row-major grid in place.</summary>
internal static class GridScroll
{
    /// <summary>After the call, cell (x, y) holds what was at (x + dx, y + dy), or the default value if that was outside.</summary>
    public static void Scroll<T>(T[] cells, int width, int height, int dx, int dy)
    {
        if (dx == 0 && dy == 0) return;
        if (Math.Abs(dx) >= width || Math.Abs(dy) >= height)
        {
            Array.Clear(cells);
            return;
        }
        // walk so that every source cell is read before it is overwritten
        int yStart = dy >= 0 ? 0 : height - 1, yStep = dy >= 0 ? 1 : -1;
        int xStart = dx >= 0 ? 0 : width - 1, xStep = dx >= 0 ? 1 : -1;
        for (int n = 0, y = yStart; n < height; n++, y += yStep)
        {
            int sy = y + dy;
            for (int m = 0, x = xStart; m < width; m++, x += xStep)
            {
                int sx = x + dx;
                cells[y * width + x] = sx >= 0 && sy >= 0 && sx < width && sy < height ? cells[sy * width + sx] : default!;
            }
        }
    }
}
