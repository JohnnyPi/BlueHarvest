namespace BlueHarvest.Sim.Perception;

/// <summary>
/// Numbers for scent and ground vibration. First guesses for testbed tuning.
/// </summary>
public static class ScentRules
{
    /// <summary>A body-scent mark fades to nothing over 3 minutes of dry weather.</summary>
    public const int BodyLifeTicks = 18_000;

    /// <summary>Blood lasts 10 minutes in the dry.</summary>
    public const int BloodLifeTicks = 60_000;

    /// <summary>How strong fresh blood smells (a person's body scent is 25).</summary>
    public const int BloodStrength = 100;

    /// <summary>Someone keeping still still marks the spot this often (20 s), so a hiding place carries scent.</summary>
    public const int IdleMarkTicks = 2_000;

    /// <summary>Marks kept; the oldest go first (in batches of <see cref="TrimBatch"/>).</summary>
    public const int MaxMarks = 4_000;

    public const int TrimBatch = 500;

    /// <summary>What 10 tiles of still air take off a scent.</summary>
    public const int CalmCostPer10Tiles = 30;

    /// <summary>
    /// Wind lowers the cost downwind and raises it upwind by up to this much per 10 tiles, reached at
    /// 10 m/s (2 per m/s): 1.0 per tile straight downwind in a strong wind, 5.0 straight upwind.
    /// </summary>
    public const int MaxWindEffectPer10Tiles = 20;

    public const int WindEffectPerMetrePerSecond = 2;

    /// <summary>Rain takes this much off every scent at 100% (heavy rain, 70%: 14), on top of washing marks out faster.</summary>
    public const int RainPenaltyAtFull = 20;

    /// <summary>Each wall or other solid tile between the nose and the scent takes this much off.</summary>
    public const int SolidTilePenalty = 10;

    /// <summary>Scent from under a roof, smelled from outside it (or the reverse), loses this much.</summary>
    public const int RoofPenalty = 15;

    /// <summary>Scent lies this high above the ground, for sight-line traces from the nose.</summary>
    public const int ScentHeightDm = 5;

    /// <summary>A footfall loses this much per tile it travels through the ground.</summary>
    public const int VibrationFalloffPerTile = 2;

    /// <summary>Sprinting hits the ground this much harder (percent of a walking step).</summary>
    public const int SprintVibrationPercent = 150;

    /// <summary>A large body turning on the spot shakes the ground this much (percent of a step).</summary>
    public const int TurnVibrationPercent = 50;

    /// <summary>Vibration from crashing through cover, shoving a vehicle, and a structure coming down.</summary>
    public const int SmashVibration = 40;
    public const int ShoveVibration = 50;
    public const int CollapseVibration = 80;
}
