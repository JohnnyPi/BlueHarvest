namespace BlueHarvest.Sim.World.Generation;

/// <summary>Ground cover of a 1 m tile. Order matches the island lab's COVER table.</summary>
public enum Cover : byte
{
    Deep, Shallow, River, Lake, Reef, Beach, Grass, Scrub, Sparse, Dense, Cloud, Rock, Lava, Mud,
    TallGrass, Fern, Reeds, Boulder, Road, Bridge, Runway, Yard, Wall, Floor, Door, Fence, Pylon, Ruin,
    Rubble, Wreck, Cave, Pier, Rack, Console, Furniture, Tank, Helipad, BigWall, Rail, Moat, Mast, Elevator,
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

    /// <summary>All primitives in drawing order.</summary>
    public IEnumerable<Prim> InOrder()
    {
        for (int i = _front.Count - 1; i >= 0; i--) yield return _front[i];
        foreach (Prim p in _back) yield return p;
    }
}
