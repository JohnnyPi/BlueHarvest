using BlueHarvest.Content;
using BlueHarvest.Sim.ContentIntegration;

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

/// <summary>Who someone is when the run starts: skills and starting gear. Data, like Species.</summary>
public sealed record Loadout(string Name, Skills Skills, IReadOnlyList<(ContentId Kind, int Count)> Items)
{
    /// <summary>No skills, nothing carried.</summary>
    public static readonly Loadout Empty = new("nobody in particular", Skills.None, Array.Empty<(ContentId, int)>());

    /// <summary>The testbed's player: a little of everything; a daypack with a kit and a rope in it, three flares and a flashlight.</summary>
    public static readonly Loadout TestbedSurvivor = new("testbed survivor",
        new Skills(Climbing: 1, Rappelling: 1, FirstAid: 1, Throwing: 1),
        new[] { (CoreItems.Daypack, 1), (CoreItems.FirstAidKit, 1), (CoreItems.Flare, 3), (CoreItems.Flashlight, 1), (CoreItems.Rope, 1) });

    /// <summary>A doctor: good at first aid, with two kits.</summary>
    public static readonly Loadout Doctor = new("doctor", new Skills(FirstAid: 3),
        new[] { (CoreItems.FirstAidKit, 2) });

    /// <summary>A park ranger: some first aid and climbing, a kit and a flashlight.</summary>
    public static readonly Loadout Ranger = new("ranger", new Skills(Climbing: 2, FirstAid: 1, Tracking: 2),
        new[] { (CoreItems.FirstAidKit, 1), (CoreItems.Flashlight, 1) });

    /// <summary>Park staff with nothing useful on them.</summary>
    public static readonly Loadout ParkStaff = new("park staff", Skills.None, Array.Empty<(ContentId, int)>());
}
