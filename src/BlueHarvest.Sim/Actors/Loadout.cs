namespace BlueHarvest.Sim.Actors;

/// <summary>Skill levels, 0 (untrained) to 3 (expert). Point-buy comes with character creation later.</summary>
public sealed record Skills(
    int Climbing = 0,
    int Rappelling = 0,
    int Driving = 0,
    int Electronics = 0,
    int FirstAid = 0,
    int Tracking = 0,
    int Stealth = 0,
    int Throwing = 0)
{
    public static readonly Skills None = new();
}

/// <summary>Things that can be carried. Order matters for saves and UI: only append.</summary>
public enum ItemKind : byte
{
    FirstAidKit,
    Flare,
    Flashlight,

    /// <summary>Rope and harness, 20 m. Used up by a rappel: it stays tied at the top.</summary>
    Rope,
}

/// <summary>What an actor carries: a count per item kind.</summary>
public sealed class Inventory
{
    private readonly int[] _counts = new int[Enum.GetValues<ItemKind>().Length];

    public int Count(ItemKind kind) => _counts[(int)kind];

    public bool Has(ItemKind kind) => _counts[(int)kind] > 0;

    internal void Add(ItemKind kind, int count = 1) => _counts[(int)kind] = Math.Max(0, _counts[(int)kind] + count);

    internal bool Use(ItemKind kind)
    {
        if (_counts[(int)kind] <= 0)
            return false;
        _counts[(int)kind]--;
        return true;
    }
}

/// <summary>Who someone is when the run starts: skills and starting gear. Data, like Species.</summary>
public sealed record Loadout(string Name, Skills Skills, IReadOnlyList<(ItemKind Kind, int Count)> Items)
{
    /// <summary>No skills, nothing carried.</summary>
    public static readonly Loadout Empty = new("nobody in particular", Skills.None, Array.Empty<(ItemKind, int)>());

    /// <summary>The testbed's player: a little of everything; a kit, three flares, a flashlight and a rope.</summary>
    public static readonly Loadout TestbedSurvivor = new("testbed survivor",
        new Skills(Climbing: 1, Rappelling: 1, FirstAid: 1, Throwing: 1),
        new[] { (ItemKind.FirstAidKit, 1), (ItemKind.Flare, 3), (ItemKind.Flashlight, 1), (ItemKind.Rope, 1) });

    /// <summary>A doctor: good at first aid, with two kits.</summary>
    public static readonly Loadout Doctor = new("doctor", new Skills(FirstAid: 3),
        new[] { (ItemKind.FirstAidKit, 2) });

    /// <summary>A park ranger: some first aid and climbing, a kit and a flashlight.</summary>
    public static readonly Loadout Ranger = new("ranger", new Skills(Climbing: 2, FirstAid: 1, Tracking: 2),
        new[] { (ItemKind.FirstAidKit, 1), (ItemKind.Flashlight, 1) });

    /// <summary>Park staff with nothing useful on them.</summary>
    public static readonly Loadout ParkStaff = new("park staff", Skills.None, Array.Empty<(ItemKind, int)>());
}
