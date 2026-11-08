namespace BlueHarvest.Sim.Actors;

/// <summary>
/// How a species moves over terrain. Heights in decimeters, times in ticks.
/// Values are first guesses from the design doc and get tuned in the testbed.
/// </summary>
/// <param name="TurnTicks">Time to turn 45°. Only multi-tile bodies pay it; single tiles turn freely.</param>
/// <param name="BiteTicks">Time a bite takes, hit or miss.</param>
/// <param name="ClimbTicksPerDm">Time to climb a ladder, per decimeter, up or down.</param>
public sealed record MovementProfile(int StepTicks, int MaxStepUpDm, int MaxDropDm, int UphillTicksPerDm, int TurnTicks = 0,
    int BiteTicks = 100, int ClimbTicksPerDm = 3)
{
    /// <summary>Walks up 0.5 m, drops 2 m safely.</summary>
    public static readonly MovementProfile Human = new(StepTicks: 70, MaxStepUpDm: 5, MaxDropDm: 20, UphillTicksPerDm: 6);

    /// <summary>Walks up 1 m, drops 4 m. Leaping comes later.</summary>
    public static readonly MovementProfile Raptor = new(StepTicks: 30, MaxStepUpDm: 10, MaxDropDm: 40, UphillTicksPerDm: 3, BiteTicks: 40);

    /// <summary>Walks up 1 m, drops 2 m. Fast in a straight line, slow to turn.</summary>
    public static readonly MovementProfile Rex = new(StepTicks: 50, MaxStepUpDm: 10, MaxDropDm: 20, UphillTicksPerDm: 8, TurnTicks: 60,
        BiteTicks: 80);

    /// <summary>Whether a height change (positive = uphill) is passable.</summary>
    public bool CanTraverse(int stepDm) => stepDm <= MaxStepUpDm && -stepDm <= MaxDropDm;

    /// <summary>Ticks to make one step. Diagonals cost about 1.41×; uphill adds cost per decimeter.</summary>
    public int CostTicks(int stepDm, bool diagonal)
    {
        int flat = diagonal ? StepTicks * 141 / 100 : StepTicks;
        return flat + Math.Max(0, stepDm) * UphillTicksPerDm;
    }
}
