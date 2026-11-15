namespace BlueHarvest.Sim.World;

/// <summary>
/// How a cover burns. All first guesses for tuning.
/// </summary>
/// <param name="Burnable">How readily it catches, 0–100, bone dry: 0 never burns (metal, stone, water, sand).</param>
/// <param name="SoakPercent">How much of the wet it takes up: grass soaks and dries fast; furniture indoors barely at all.</param>
/// <param name="FuelSteps">How long it burns, in fire steps (2 s each): grass seconds, timber minutes.</param>
/// <param name="MaxIntensity">How hot it gets, 0–100.</param>
/// <param name="BurnsInto">What is left when it has burnt; null for what stays as it is (a burnt-out vehicle).</param>
public sealed record Material(int Burnable, int SoakPercent, int FuelSteps, int MaxIntensity, CoverType? BurnsInto);

/// <summary>
/// The rules of fire, in action time: what burns and how long, what it takes to light, and how heat
/// carries. Fire stays local because of these, not because of a cap: most of the island is damp
/// jungle that won't carry a fire, so a fire dies at the edge of whatever was dry or fuelled.
/// </summary>
public static class FireRules
{
    /// <summary>A fire step: every 2 action seconds the fires grow, spread and burn down.</summary>
    public const int StepTicks = 200;

    /// <summary>A tile catches when heat plus its burnable value passes this, give or take <see cref="IgnitionRoll"/>.</summary>
    public const int IgnitionThreshold = 100, IgnitionRoll = 15;

    /// <summary>How hot each source is.</summary>
    public const int SparkHeat = 30, FlareHeat = 50, HeldFlareHeat = 60, LightningHeat = 100;

    /// <summary>Fuel on the ground makes anything at least this burnable, whatever it is or how wet.</summary>
    public const int FuelBurnable = 90;

    /// <summary>Spilled fuel lasts this long before it has evaporated (three game hours), less in rain.</summary>
    public const int FuelLastsTicks = 3 * WorldClock.TicksPerGameHour;

    /// <summary>Fuel burns this many steps on top of what is under it, at full heat.</summary>
    public const int FuelSteps = 5;

    /// <summary>A new fire starts this hot, and grows this much a step while it has fuel; once out of fuel it dies down this much a step.</summary>
    public const int StartIntensity = 25, GrowthPerStep = 15, DeclinePerStep = 25;

    /// <summary>A burning tile passes on this percentage of its intensity as heat to each side neighbour (diagonals 70% of that).</summary>
    public const int HeatPercent = 50;

    /// <summary>Each burning neighbour past the hottest adds this much heat: a front carries a fire better than a spark.</summary>
    public const int ExtraSourceHeat = 10;

    /// <summary>Ground beside water is this much damper.</summary>
    public const int WaterSideDamp = 20;

    private static readonly Material None = new(0, 0, 0, 0, null);

    private static readonly Material[] ByCover = Build();

    private static Material[] Build()
    {
        var table = new Material[Enum.GetValues<CoverType>().Length];
        Array.Fill(table, None);
        // short green grass won't take from a flare (it can carry a fire on a wind); tall, dry grass goes up
        table[(int)CoverType.Grass] = new(30, 90, 8, 50, CoverType.Ash);
        table[(int)CoverType.TallGrass] = new(70, 90, 10, 70, CoverType.Ash);
        table[(int)CoverType.Scrub] = new(60, 70, 25, 70, CoverType.Ash);
        table[(int)CoverType.Ferns] = new(35, 100, 10, 55, CoverType.Ash);
        table[(int)CoverType.Reeds] = new(40, 100, 10, 60, CoverType.Mud);
        table[(int)CoverType.SparseJungle] = new(35, 80, 50, 80, CoverType.Ash);
        table[(int)CoverType.DenseJungle] = new(20, 80, 75, 85, CoverType.Ash);
        // timber posts; furniture, racks and cloth indoors; a vehicle's seats, tyres and tank
        table[(int)CoverType.Support] = new(45, 30, 90, 70, CoverType.Rubble);
        table[(int)CoverType.Equipment] = new(45, 0, 60, 80, CoverType.Rubble);
        table[(int)CoverType.Vehicle] = new(35, 0, 120, 90, null);
        return table;
    }

    public static Material Of(CoverType cover) => ByCover[(int)cover];

    /// <summary>Whether anything on this cover can burn at all (with fuel poured on it, anything not wet through can).</summary>
    public static bool CanBurn(CoverType cover) => cover is not (CoverType.ShallowWater or CoverType.DeepWater);
}
