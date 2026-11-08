namespace BlueHarvest.Sim.World;

/// <summary>All per-tile layers of a map. Structure layers (floors, catwalks) come later.</summary>
public sealed class WorldMap
{
    public WorldMap(int width, int height)
        : this(new HeightMap(width, height), new CoverMap(width, height))
    {
    }

    public WorldMap(HeightMap heights, CoverMap cover)
    {
        if (heights.Width != cover.Width || heights.Height != cover.Height)
            throw new ArgumentException("Height and cover layers must be the same size.");
        Heights = heights;
        Cover = cover;
    }

    public HeightMap Heights { get; }
    public CoverMap Cover { get; }
    public int Width => Heights.Width;
    public int Height => Heights.Height;

    public bool InBounds(TilePos p) => Heights.InBounds(p);
}
