using BlueHarvest.Sim.Injuries;

namespace BlueHarvest.Sim.Actors;

/// <summary>Numbers for gear, climbing and sprinting. First guesses for the testbed. Times in ticks, heights in dm.</summary>
public static class GearRules
{
    public const int ThrowTicks = 100;

    /// <summary>A flare burns for 90 s.</summary>
    public const int FlareBurnTicks = 9000;

    public const int FlareLoudness = 15;

    public const int FlashlightToggleTicks = 20;

    /// <summary>The rope reaches 20 m.</summary>
    public const int RopeLengthDm = 200;

    public const int RappelLoudness = 10;

    /// <summary>Climbing a step is louder than walking: scrabbling hands and feet.</summary>
    public const int ClimbLoudnessBonus = 8;

    /// <summary>Sprinting: a step takes 60% of the time (80% with a hurt torso) and is 8 louder.</summary>
    public const int SprintCostPercent = 60;
    public const int WindedSprintCostPercent = 80;
    public const int SprintLoudnessBonus = 8;

    /// <summary>Throw range in tiles: 6, plus 3 per throwing level, less for hurt arms.</summary>
    public static int ThrowRange(int skill, Health health) => (6 + 3 * Math.Clamp(skill, 0, 3)) * health.ThrowRangePercent / 100;

    /// <summary>How far a throw may land from where it was aimed, in tiles: a quarter of the distance, less per skill level.</summary>
    public static int Scatter(int distance, int skill) => Math.Max(0, distance / 4 - Math.Clamp(skill, 0, 3));

    /// <summary>Chance a rappel goes wrong and you fall part of the way: 25% per hurt arm, 15% more untrained.</summary>
    public static int RappelFailPercent(int skill, int yellowArms) => yellowArms * 25 + (skill <= 0 ? 15 : 0);

    /// <summary>Time to rappel: 0.3 s per decimeter, 20% faster per skill level, at least 1 s.</summary>
    public static int RappelTicks(int dropDm, int skill) => Math.Max(100, dropDm * 3 * (100 - 20 * Math.Clamp(skill, 0, 3)) / 100);

    /// <summary>Time to climb a step: 1.5 s plus 0.1 s per decimeter, 20% faster per skill level.</summary>
    public static int ClimbStepTicks(int riseDm, int skill) => (150 + riseDm * 10) * (100 - 20 * Math.Clamp(skill, 0, 3)) / 100;
}
