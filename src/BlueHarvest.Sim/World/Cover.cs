namespace BlueHarvest.Sim.World;

/// <summary>What covers a tile. Order matters: map files and the editor palette index by it.</summary>
public enum CoverType : byte
{
    Grass,
    Scrub,
    SparseJungle,
    DenseJungle,
    Track,
    Floor,
    Wall,
    Door,
    Fence,
    Rock,
    Vehicle,
    BrokenFence,
    Support,
    Rubble,
}

/// <summary>
/// Rules for one cover type. All values are first guesses for tuning.
/// </summary>
/// <param name="FileChar">Character in map files.</param>
/// <param name="MoveCostPercent">Step cost as a percentage of a normal step.</param>
/// <param name="SightHeightDm">How tall the cover stands. Sight lines below this pass through it.</param>
/// <param name="SightOpacityPercent">How much of a sight line passing through it is blocked (100 = solid).</param>
/// <param name="NoiseModifier">Added to footstep loudness when stepping onto it.</param>
/// <param name="BlocksLargeBodies">Stops multi-tile cores even though small actors pass (trunks, doorways, vehicles).</param>
/// <param name="BreakStrength">Force needed to smash or trample it. Only matters with <paramref name="BrokenInto"/> set.</param>
/// <param name="BrokenInto">What it becomes when smashed or trampled; null means it can't be broken.</param>
/// <param name="BiteSound">Noise kind when jaws close on it (vehicles); null means a bite passes through it.</param>
public sealed record CoverInfo(
    CoverType Type,
    char FileChar,
    string Name,
    int MoveCostPercent,
    bool BlocksMovement,
    int SightHeightDm = 0,
    int SightOpacityPercent = 0,
    int NoiseModifier = 0,
    bool BlocksLargeBodies = false,
    int BreakStrength = 0,
    CoverType? BrokenInto = null,
    string? BiteSound = null)
{
    /// <summary>Whether a body of this force can't pass. Small bodies use BlocksMovement only.</summary>
    public bool StopsLargeBody(int force) =>
        (BlocksMovement || BlocksLargeBodies) && (BrokenInto is null || BreakStrength > force);

    /// <summary>Whether a large body entering it smashes it (as opposed to trampling or nothing).</summary>
    public bool IsSmashedBy(int force) =>
        (BlocksMovement || BlocksLargeBodies) && BrokenInto is not null && BreakStrength <= force;
}

public static class Covers
{
    public static readonly IReadOnlyList<CoverInfo> All = new[]
    {
        new CoverInfo(CoverType.Grass, '.', "grass", 100, false),
        new CoverInfo(CoverType.Scrub, ',', "scrub", 115, false, SightHeightDm: 10, SightOpacityPercent: 25, NoiseModifier: 2,
            BreakStrength: 5, BrokenInto: CoverType.Grass),
        new CoverInfo(CoverType.SparseJungle, '"', "sparse jungle", 130, false, SightHeightDm: 60, SightOpacityPercent: 20, NoiseModifier: 4,
            BlocksLargeBodies: true, BreakStrength: 30, BrokenInto: CoverType.Scrub),
        new CoverInfo(CoverType.DenseJungle, '&', "dense jungle", 180, false, SightHeightDm: 80, SightOpacityPercent: 45, NoiseModifier: 7,
            BlocksLargeBodies: true, BreakStrength: 90, BrokenInto: CoverType.SparseJungle),
        new CoverInfo(CoverType.Track, '=', "the track", 90, false, NoiseModifier: -2),
        new CoverInfo(CoverType.Floor, '_', "a floor", 100, false),
        new CoverInfo(CoverType.Wall, '#', "a wall", 100, true, SightHeightDm: 30, SightOpacityPercent: 100),
        new CoverInfo(CoverType.Door, '+', "a doorway", 110, false, NoiseModifier: 2, BlocksLargeBodies: true),
        new CoverInfo(CoverType.Fence, 'F', "the electric fence", 100, true, SightHeightDm: 40, SightOpacityPercent: 10,
            BreakStrength: 70, BrokenInto: CoverType.BrokenFence),
        new CoverInfo(CoverType.Rock, 'r', "a boulder", 100, true, SightHeightDm: 20, SightOpacityPercent: 100),
        new CoverInfo(CoverType.Vehicle, 'V', "a vehicle", 150, false, SightHeightDm: 18, SightOpacityPercent: 60, NoiseModifier: 5,
            BlocksLargeBodies: true, BiteSound: Perception.NoiseKinds.MetalCrunch),
        new CoverInfo(CoverType.BrokenFence, 'f', "a broken fence", 120, false, SightHeightDm: 10, SightOpacityPercent: 5, NoiseModifier: 3),
        new CoverInfo(CoverType.Support, '|', "a support post", 100, true, SightHeightDm: 60, SightOpacityPercent: 30,
            BlocksLargeBodies: true, BreakStrength: 60, BrokenInto: CoverType.Rubble),
        new CoverInfo(CoverType.Rubble, 'x', "rubble", 150, false, SightHeightDm: 5, SightOpacityPercent: 10, NoiseModifier: 4),
    };

    public static CoverInfo Get(CoverType type) => All[(int)type];

    /// <summary>A doorway whose door is shut: like a wall, but humans can open it (WorldMap.CoverAt).</summary>
    public static readonly CoverInfo ClosedDoor = new(CoverType.Door, '+', "a closed door", 100, true,
        SightHeightDm: 30, SightOpacityPercent: 100, NoiseModifier: 2, BlocksLargeBodies: true);

    /// <summary>What walking on a deck is like (the cover beneath doesn't matter up there): footsteps ring out.</summary>
    public static readonly CoverInfo Deck = new(CoverType.Floor, '=', "a platform", 100, false, NoiseModifier: 5);

    /// <summary>A doorway whose door is locked: a wall until someone unlocks it.</summary>
    public static readonly CoverInfo LockedDoor = ClosedDoor with { Name = "a locked door" };

    /// <summary>Cover for a map-file character, or null if none uses it.</summary>
    public static CoverInfo? FromFileChar(char c)
    {
        foreach (var info in All)
        {
            if (info.FileChar == c)
                return info;
        }
        return null;
    }
}

/// <summary>Cover type per tile.</summary>
public sealed class CoverMap
{
    private readonly CoverType[] _cover;

    public CoverMap(int width, int height)
    {
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width), "Must be positive.");
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height), "Must be positive.");
        Width = width;
        Height = height;
        _cover = new CoverType[width * height];
    }

    public int Width { get; }
    public int Height { get; }

    public CoverType this[TilePos p]
    {
        get => _cover[Index(p)];
        set => _cover[Index(p)] = value;
    }

    private int Index(TilePos p)
    {
        if (p.X < 0 || p.Y < 0 || p.X >= Width || p.Y >= Height)
            throw new ArgumentOutOfRangeException(nameof(p), $"{p} is outside the {Width}x{Height} map.");
        return p.Y * Width + p.X;
    }
}
