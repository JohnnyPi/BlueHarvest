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
