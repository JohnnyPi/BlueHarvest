namespace BlueHarvest.Sim.Perception;

/// <summary>
/// How a species perceives. Heights in decimeters, ranges in tiles, the rest in percent.
/// Values are design choices for play, not paleontology, and get tuned in the testbed.
/// </summary>
/// <param name="EyeHeightDm">Eye height above the ground; also used as ear height.</param>
/// <param name="SightRangeTiles">Nothing farther than this is seen.</param>
/// <param name="FovCosPermille">
/// Cosine of half the field of view, ×1000. 360° → -1000, 220° → -342, 180° → 0, 120° → 500.
/// </param>
/// <param name="NightVisionPercent">Light level the eyes behave as if they always have, at minimum.</param>
/// <param name="MotionBiasPercent">How much harder still targets are to see. 85 means a still target scores 15%.</param>
/// <param name="DetectThreshold">Sight score needed to notice something.</param>
/// <param name="HearingThreshold">Perceived loudness needed to hear something.</param>
/// <param name="ScentRangeTiles">Nothing farther than this is smelled; 0 if it can't follow scent at all.</param>
/// <param name="ScentThreshold">Scent strength, after distance, wind and rain, needed to smell something.</param>
/// <param name="VibrationThreshold">Ground vibration, after distance, needed to feel it underfoot.</param>
public sealed record SenseProfile(
    int EyeHeightDm,
    int SightRangeTiles,
    int FovCosPermille,
    int NightVisionPercent,
    int MotionBiasPercent,
    int DetectThreshold,
    int HearingThreshold,
    int ScentRangeTiles = 0,
    int ScentThreshold = 10,
    int VibrationThreshold = int.MaxValue)
{
    /// <summary>
    /// All-round awareness (a gameplay choice), poor night vision, mild motion bias. No useful nose;
    /// feels the heavy tread of something big coming (the ripple in the cup), but not each other.
    /// </summary>
    public static readonly SenseProfile Human = new(
        EyeHeightDm: 16, SightRangeTiles: 30, FovCosPermille: -1000,
        NightVisionPercent: 10, MotionBiasPercent: 30, DetectThreshold: 12, HearingThreshold: 3,
        VibrationThreshold: 10);

    /// <summary>Wide field of view, good night vision, sharp hearing, a keen nose, feels running feet close by.</summary>
    public static readonly SenseProfile Raptor = new(
        EyeHeightDm: 12, SightRangeTiles: 30, FovCosPermille: -342,
        NightVisionPercent: 40, MotionBiasPercent: 40, DetectThreshold: 10, HearingThreshold: 2,
        ScentRangeTiles: 50, ScentThreshold: 8, VibrationThreshold: 8);

    /// <summary>
    /// High eyes, 120° forward view, strongly tuned to motion. A good nose (blood carries far downwind);
    /// feels only heavy footfalls, not a person's.
    /// </summary>
    public static readonly SenseProfile Rex = new(
        EyeHeightDm: 45, SightRangeTiles: 40, FovCosPermille: 500,
        NightVisionPercent: 15, MotionBiasPercent: 85, DetectThreshold: 10, HearingThreshold: 3,
        ScentRangeTiles: 40, ScentThreshold: 10, VibrationThreshold: 20);
}
