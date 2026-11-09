namespace BlueHarvest.Sim.Ai;

/// <summary>
/// How a creature's mind weighs what it perceives: how fast memories fade, what draws it and how
/// strongly. Data, like Species and SenseProfile. Values are first guesses for the testbed.
/// Confidences and scores are 0-100.
/// </summary>
/// <param name="PreyMaxBodyHeightDm">A detected single-tile actor this tall or shorter is prey.</param>
/// <param name="ConfidenceDecayPerSecond">Confidence lost per second without a fresh cue.</param>
/// <param name="UncertaintyGrowthTicks">An unrefreshed estimate spreads by one tile per this many ticks.</param>
/// <param name="GlimpseConfidenceCap">Most confidence a sighting below the detect threshold can give.</param>
/// <param name="HeardConfidencePerPoint">Confidence per point of perceived loudness.</param>
/// <param name="HeardConfidenceCap">Most confidence a noise alone gives (screams excepted).</param>
/// <param name="LightInterestThreshold">Light score needed for a light to draw attention.</param>
/// <param name="HuntWeight">Score weight, in percent of confidence, for prey.</param>
/// <param name="InvestigateWeight">Score weight for unidentified actors and sounds.</param>
/// <param name="LightWeight">Score weight for unidentified lights.</param>
/// <param name="MovingPreyBonus">Added for prey last seen moving.</param>
/// <param name="FocusBonus">Added for the current focus, so attention doesn't flicker between near-equal contacts.</param>
/// <param name="DistancePenaltyPer10Tiles">Subtracted per 10 tiles between the body and a contact.</param>
/// <param name="WanderScore">The best contact must beat this, or the creature wanders.</param>
/// <param name="SearchedConfidenceCap">Confidence left after looking (or biting) where a contact was and finding nothing.</param>
/// <param name="StuckTurnsBeforeGivingUp">Turns without progress toward a contact before it counts as searched.</param>
/// <param name="MaxContacts">Memory size, not counting things it has identified as uninteresting.</param>
/// <param name="ThreatMinForce">A detected actor of another species this strong or stronger is a threat.</param>
/// <param name="ThreatNoiseLoudness">An actor heard making a noise this loud (at the source) is a threat: a heavy tread, crashing.</param>
/// <param name="ScentConfidencePerPoint">Confidence per point of scent strength at the nose.</param>
/// <param name="ScentConfidenceCap">Most confidence a trail gives: it says someone passed, not that they're still there.</param>
/// <param name="FeltConfidencePerPoint">Confidence per point of vibration felt.</param>
/// <param name="FeltConfidenceCap">Most confidence a shake of the ground gives.</param>
/// <param name="ThreatVibrationStrength">An actor whose step shakes the ground this hard (where it lands) is a threat.</param>
/// <param name="ChargeSightTicks">Charges prey it saw within this long (its movement profile must have a charge); 0 never charges.</param>
public sealed record AiProfile(
    int PreyMaxBodyHeightDm,
    int ConfidenceDecayPerSecond,
    int UncertaintyGrowthTicks,
    int GlimpseConfidenceCap,
    int HeardConfidencePerPoint,
    int HeardConfidenceCap,
    int LightInterestThreshold,
    int HuntWeight,
    int InvestigateWeight,
    int LightWeight,
    int MovingPreyBonus,
    int FocusBonus,
    int DistancePenaltyPer10Tiles,
    int WanderScore,
    int SearchedConfidenceCap,
    int StuckTurnsBeforeGivingUp,
    int MaxContacts,
    int ThreatMinForce = int.MaxValue,
    int ThreatNoiseLoudness = int.MaxValue,
    int ScentConfidencePerPoint = 3,
    int ScentConfidenceCap = 60,
    int FeltConfidencePerPoint = 3,
    int FeltConfidenceCap = 40,
    int ThreatVibrationStrength = int.MaxValue,
    int ChargeSightTicks = 0)
{
    /// <summary>
    /// Fixates on moving prey, follows noises, drawn to lights it doesn't know and test-bites them.
    /// Unseen prey fades in about 12 s.
    /// </summary>
    public static readonly AiProfile Rex = new(
        PreyMaxBodyHeightDm: 30,
        ConfidenceDecayPerSecond: 8,
        UncertaintyGrowthTicks: 100,
        GlimpseConfidenceCap: 30,
        HeardConfidencePerPoint: 4,
        HeardConfidenceCap: 60,
        LightInterestThreshold: 30,
        HuntWeight: 100,
        InvestigateWeight: 80,
        LightWeight: 60,
        MovingPreyBonus: 10,
        FocusBonus: 15,
        DistancePenaltyPer10Tiles: 3,
        WanderScore: 8,
        SearchedConfidenceCap: 20,
        StuckTurnsBeforeGivingUp: 8,
        MaxContacts: 16,
        ChargeSightTicks: 100);

    /// <summary>
    /// People: nothing is prey and lights don't draw them; anything strong (a raptor or bigger) is a
    /// threat, and so is anything heard stomping or crashing. Threats stay in mind longer than the Rex
    /// keeps prey: about 20 s unheard.
    /// </summary>
    public static readonly AiProfile Human = new(
        PreyMaxBodyHeightDm: 0,
        ConfidenceDecayPerSecond: 4,
        UncertaintyGrowthTicks: 100,
        GlimpseConfidenceCap: 30,
        HeardConfidencePerPoint: 4,
        HeardConfidenceCap: 80,
        LightInterestThreshold: 101,
        HuntWeight: 0,
        InvestigateWeight: 0,
        LightWeight: 0,
        MovingPreyBonus: 0,
        FocusBonus: 0,
        DistancePenaltyPer10Tiles: 0,
        WanderScore: 0,
        SearchedConfidenceCap: 20,
        StuckTurnsBeforeGivingUp: 8,
        MaxContacts: 16,
        ThreatMinForce: 5,
        ThreatNoiseLoudness: 40,
        ThreatVibrationStrength: 30);
}
