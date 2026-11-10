namespace BlueHarvest.Sim.World.Generation;

/// <summary>Ground cover of a 1 m tile. Order matches the island lab's COVER table.</summary>
public enum Cover : byte
{
    Deep, Shallow, River, Lake, Reef, Beach, Grass, Scrub, Sparse, Dense, Cloud, Rock, Lava, Mud,
    TallGrass, Fern, Reeds, Boulder, Road, Bridge, Runway, Yard, Wall, Floor, Door, Fence, Pylon, Ruin,
    Rubble, Wreck, Cave, Pier, Rack, Console, Furniture, Tank, Helipad, BigWall, Rail, Moat, Mast, Elevator,
}

/// <summary>How a cover is named and drawn: one or more glyphs to vary between, text colour and background.</summary>
public readonly record struct CoverLook(string Name, string Glyphs, string Fg, string Bg);

public static class CoverLegend
{
    /// <summary>Indexed by <see cref="Cover"/>; matches the island lab's COVER table.</summary>
    public static readonly CoverLook[] Looks =
    [
        new("Deep water", "≈", "#4f86b8", "#0f2a40"), new("Shallow water", "~", "#7fc0d8", "#1f5068"), new("River", "~", "#a8dcf2", "#2a6688"),
        new("Lake", "~", "#8fcde6", "#24607e"), new("Reef flat", "°", "#f0b8a0", "#2f6f78"), new("Beach sand", "·", "#e8d7a6", "#9c8a5c"),
        new("Grass", ".'", "#a8c46c", "#4b5e2c"), new("Dry scrub", ",;", "#c2ad6a", "#5e5432"), new("Sparse jungle", "\"τ", "#78b85a", "#2f4d24"),
        new("Dense jungle", "&♣♠", "#4fa04a", "#1d3a1c"), new("Cloud forest", "Ψ♣", "#8fc7a0", "#264438"), new("Bare rock, cliff", "^∩", "#b7b2a6", "#55524c"),
        new("Young lava", "%", "#8a7f78", "#2a2624"), new("Mud bank", "∴", "#a98e66", "#4a3c2a"), new("Tall grass", "ⁿ\"", "#c9cf72", "#55602c"),
        new("Ferns", "ψ", "#7fcf8a", "#21402a"), new("Reeds", "¦", "#9fc4a0", "#2f4a3c"), new("Boulders", "●o", "#c4beb2", "#4d4a44"),
        new("Road", "=", "#d8cfb8", "#5b5446"), new("Bridge", "═", "#e6d9b8", "#6a5a40"), new("Runway", "·", "#e8e6df", "#55575a"),
        new("Compound yard", "∙", "#b9b6ad", "#4a4946"), new("Wall", "#", "#ece6da", "#6d6a64"), new("Floor", "_", "#a7a294", "#3c3a36"),
        new("Door", "+", "#f2c46a", "#3c3a36"), new("Fence", "‡", "#e0e4ea", "#3d4a3c"), new("Power pylon", "╫", "#f0d36a", "#3f3d34"),
        new("Ruined wall", "▓", "#b8a98a", "#4e463a"), new("Rubble", "x", "#a69a84", "#3e382f"), new("Wreckage", "▒", "#c98a5a", "#4a3326"),
        new("Cave opening", "Ω", "#9b8fb8", "#121014"), new("Pier", "≡", "#d9c39a", "#5a4a34"), new("Racks and shelving", "▐", "#9fb4c8", "#3c3a36"),
        new("Consoles", "◙", "#8fe0c0", "#3c3a36"), new("Tables and bunks", "π", "#c8b48e", "#3c3a36"), new("Tanks and machinery", "Ø", "#c9ccd2", "#45474b"),
        new("Helipad", "⊕", "#f2d36b", "#4a4946"), new("Reinforced wall", "█", "#d8d4cc", "#7a766e"), new("Tram rails", "╪", "#d0c8b8", "#4c4840"),
        new("Moat (engineered drop)", "∨", "#8a7a64", "#2a241c"), new("Mast or tower", "╬", "#f0d36a", "#3f3d34"), new("Elevator", "⇕", "#f2c46a", "#2e2a24"),
    ];
}

public enum PrimKind : byte
{
    /// <summary>A building: walls round the outside, rooms, doors.</summary>
    Building,
    /// <summary>A furnishing block inside a room.</summary>
    Furniture,
    Circle,
    Rect,
    /// <summary>A mast, pylon or tower: one tile.</summary>
    Tower,
    /// <summary>A thick line: moats, later ruin walls and wreck hulls.</summary>
    Line,
    /// <summary>A reinforced enclosure (the raptor pen).</summary>
    Pen,
}

public sealed record Room(int X0, int Y0, int X1, int Y1, int Label);

/// <summary>
/// One drawing primitive of the works, in metres (world tiles). Which fields matter depends on
/// <see cref="Kind"/>: rectangles use X0..Y1 (inclusive tiles), circles and towers use X, Y (and R),
/// lines use Ax..By and W. Label indexes the works' label list (room and feature names).
/// </summary>
public sealed class Prim
{
    public PrimKind Kind { get; init; }
    public int X0 { get; init; }
    public int Y0 { get; init; }
    public int X1 { get; init; }
    public int Y1 { get; init; }
    public double X { get; init; }
    public double Y { get; init; }
    public double R { get; init; }
    public double Ax { get; init; }
    public double Ay { get; init; }
    public double Bx { get; init; }
    public double By { get; init; }
    public double W { get; init; }
    /// <summary>Depth of a moat or cut, m.</summary>
    public double Depth { get; init; }
    public Cover Cover { get; init; }
    /// <summary>Circles: only the rim is <see cref="Cover"/>, the inside is <see cref="Fill"/>.</summary>
    public bool Ring { get; init; }
    public Cover? Fill { get; init; }
    /// <summary>Circles: a door in the rim. Rects: fenced.</summary>
    public bool Door { get; init; }
    public bool Fence { get; init; }
    public int Label { get; init; }
    public List<Room>? Rooms { get; init; }
    public List<(int X, int Y)>? Doors { get; init; }
}

/// <summary>A point in a <see cref="PrimList"/>'s history.</summary>
public readonly record struct PrimMark(int Front, int Back)
{
    public int Count => Front + Back;
}

/// <summary>
/// The works' primitives in drawing order. Buildings go to the front (so furniture draws over
/// their floors), everything else to the back, exactly as the reference does with unshift/push.
/// </summary>
public sealed class PrimList
{
    private readonly List<Prim> _front = []; // stored in insertion order, drawn last-inserted first
    private readonly List<Prim> _back = [];

    public int Count => _front.Count + _back.Count;

    public void AddFront(Prim p) => _front.Add(p);

    public void Add(Prim p) => _back.Add(p);

    /// <summary>The list's current extent, to read it back later as it was now.</summary>
    public PrimMark Mark() => new(_front.Count, _back.Count);

    /// <summary>The primitives that existed at a mark, in the drawing order they had then.</summary>
    public IEnumerable<Prim> InOrder(PrimMark at)
    {
        for (int i = at.Front - 1; i >= 0; i--) yield return _front[i];
        for (int i = 0; i < at.Back; i++) yield return _back[i];
    }

    /// <summary>All primitives in drawing order.</summary>
    public IEnumerable<Prim> InOrder()
    {
        for (int i = _front.Count - 1; i >= 0; i--) yield return _front[i];
        foreach (Prim p in _back) yield return p;
    }
}
