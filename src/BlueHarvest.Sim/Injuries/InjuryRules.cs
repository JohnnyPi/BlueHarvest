namespace BlueHarvest.Sim.Injuries;

/// <summary>
/// Numbers for wounds, bleeding, healing and first aid. First guesses, tuned in the testbed like the
/// rest. Times in ticks (1/100 s), heights in decimeters.
/// </summary>
public static class InjuryRules
{
    /// <summary>Blood a body can lose: 90 s with one bleeding wound, 45 s with two.</summary>
    public const int BleedOutTicks = 9000;

    /// <summary>A yellow wound heals to green after 3 minutes without being hurt again.</summary>
    public const int YellowHealTicks = 18000;

    /// <summary>A concussed actor blacks out on this percentage of its turns, losing 3 s.</summary>
    public const int BlackoutPercent = 5;
    public const int BlackoutTicks = 300;

    public const int ConcussionHearingPenalty = 4;

    /// <summary>Chance a tail blow breaks something (red) rather than bruising it (yellow).</summary>
    public const int TailBlowRedPercent = 25;

    public const int TreatLoudness = 6;

    /// <summary>Most blood spots remembered; older ones dry out.</summary>
    public const int MaxBloodSpots = 500;

    /// <summary>Where a blow lands, by weight out of 100: mostly torso and legs, rarely the head.</summary>
    public static readonly IReadOnlyList<(BodySection Section, int Weight)> HitWeights = new[]
    {
        (BodySection.Head, 5),
        (BodySection.Torso, 30),
        (BodySection.LeftArm, 15),
        (BodySection.RightArm, 15),
        (BodySection.LeftLeg, 18),
        (BodySection.RightLeg, 17),
    };

    /// <summary>Time to give first aid: 8 s untrained, 1.5 s faster per skill level, at least 3.5 s.</summary>
    public static int TreatTicks(int skill) => Math.Max(350, 800 - 150 * skill);

    /// <summary>Chance first aid works: 50% untrained, then 80%, 95%, certain. A failed attempt keeps the kit.</summary>
    public static int TreatSuccessPercent(int skill) => skill switch
    {
        <= 0 => 50,
        1 => 80,
        2 => 95,
        _ => 100,
    };

    /// <summary>
    /// Wounds from a fall, by how far it went past the safe drop: up to 2 m more a yellow leg, up to
    /// 4 m more a red leg, beyond that a red leg and a yellow torso, and past 8 m a red torso too.
    /// </summary>
    public static IReadOnlyList<(BodySection Section, Severity Severity)> FallWounds(int excessDm, BodySection leg)
    {
        if (excessDm <= 0)
            return Array.Empty<(BodySection, Severity)>();
        if (excessDm <= 20)
            return new[] { (leg, Severity.Yellow) };
        if (excessDm <= 40)
            return new[] { (leg, Severity.Red) };
        if (excessDm <= 80)
            return new[] { (leg, Severity.Red), (BodySection.Torso, Severity.Yellow) };
        return new[] { (leg, Severity.Red), (BodySection.Torso, Severity.Red) };
    }

    public static string Name(BodySection section) => section switch
    {
        BodySection.Head => "head",
        BodySection.Torso => "torso",
        BodySection.LeftArm => "left arm",
        BodySection.RightArm => "right arm",
        BodySection.LeftLeg => "left leg",
        _ => "right leg",
    };
}
