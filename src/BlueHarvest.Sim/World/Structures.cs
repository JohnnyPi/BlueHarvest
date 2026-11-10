namespace BlueHarvest.Sim.World;

/// <summary>A door's state. Order matters: the map editor's door palette indexes by it.</summary>
public enum DoorState : byte
{
    Open,
    Closed,
    Locked,
}

/// <summary>
/// The structure layer over the ground: roofs, door states, and decks. A roofed tile is indoors:
/// sight, light and sound only cross into it below the roof line (through doorways and gaps in walls).
/// A deck is a walkable surface above the ground (a catwalk, tower top or upper floor); each tile has
/// at most one. Stairs are decks stepping down to the ground; ladders join a deck tile to the ground
/// beneath it. Decks stand on support posts (cover), and fall when too many are smashed.
/// </summary>
public sealed class StructureMap
{
    /// <summary>Default roof height for buildings, and for floors in old v1 maps: 3 m.</summary>
    public const short DefaultRoofDm = 30;

    private readonly short[] _roofDm;
    private readonly DoorState[] _doors;
    private readonly short[] _deckDm;
    private readonly bool[] _ladders;

    public StructureMap(int width, int height)
    {
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width), "Must be positive.");
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height), "Must be positive.");
        Width = width;
        Height = height;
        _roofDm = new short[width * height];
        _doors = new DoorState[width * height];
        _deckDm = new short[width * height];
        _ladders = new bool[width * height];
    }

    public int Width { get; }
    public int Height { get; }

    /// <summary>Height of the roof above the ground at this tile, in decimeters; 0 for open sky.</summary>
    public int RoofDm(TilePos p) => _roofDm[Index(p)];

    public void SetRoof(TilePos p, int heightDm)
    {
        if (heightDm < 0 || heightDm > short.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(heightDm), "Roof height must be 0 or more decimeters.");
        _roofDm[Index(p)] = (short)heightDm;
    }

    public bool IsRoofed(TilePos p) => _roofDm[Index(p)] > 0;

    /// <summary>Whether any tile has a roof.</summary>
    public bool AnyRoofs => _roofDm.Any(r => r > 0);

    /// <summary>Height of the deck above the ground at this tile, in decimeters; 0 for none.</summary>
    public int DeckDm(TilePos p) => _deckDm[Index(p)];

    public bool HasDeck(TilePos p) => _deckDm[Index(p)] > 0;

    public void SetDeck(TilePos p, int heightDm)
    {
        if (heightDm < 0 || heightDm > short.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(heightDm), "Deck height must be 0 or more decimeters.");
        _deckDm[Index(p)] = (short)heightDm;
    }

    public bool AnyDecks => _deckDm.Any(d => d > 0);

    /// <summary>A ladder from the ground up to the deck on this tile.</summary>
    public bool HasLadder(TilePos p) => _ladders[Index(p)];

    public void SetLadder(TilePos p, bool ladder) => _ladders[Index(p)] = ladder;

    public bool AnyLadders => _ladders.Any(l => l);

    /// <summary>The state of a door on this tile. Only meaningful where the cover is a doorway.</summary>
    public DoorState DoorAt(TilePos p) => _doors[Index(p)];

    public void SetDoor(TilePos p, DoorState state) => _doors[Index(p)] = state;

    /// <summary>Moves roofs, doors, decks and ladders by (−dx, −dy) tiles; what scrolls in is empty. See WorldMap.Scroll.</summary>
    internal void Scroll(int dx, int dy)
    {
        GridScroll.Scroll(_roofDm, Width, Height, dx, dy);
        GridScroll.Scroll(_doors, Width, Height, dx, dy);
        GridScroll.Scroll(_deckDm, Width, Height, dx, dy);
        GridScroll.Scroll(_ladders, Width, Height, dx, dy);
    }

    private int Index(TilePos p)
    {
        if (p.X < 0 || p.Y < 0 || p.X >= Width || p.Y >= Height)
            throw new ArgumentOutOfRangeException(nameof(p), $"{p} is outside the {Width}x{Height} map.");
        return p.Y * Width + p.X;
    }
}
