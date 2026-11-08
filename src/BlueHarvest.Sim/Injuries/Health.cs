using BlueHarvest.Sim.Perception;

namespace BlueHarvest.Sim.Injuries;

/// <summary>The six parts of a body that take wounds. Order matters: wound tables index by it.</summary>
public enum BodySection : byte
{
    Head,
    Torso,
    LeftArm,
    RightArm,
    LeftLeg,
    RightLeg,
}

/// <summary>How hurt a section is.</summary>
public enum Severity : byte
{
    /// <summary>Healthy.</summary>
    Green,

    /// <summary>Minor: a cut, a sprain. Heals with rest.</summary>
    Yellow,

    /// <summary>Major: a break or a deep wound. Needs first aid; a second major wound here ends the run.</summary>
    Red,
}

/// <summary>A section's state: how bad, whether it bleeds, and since when (for healing).</summary>
public readonly record struct Wound(Severity Severity, bool Bleeding, long SinceTick)
{
    public static readonly Wound None = new(Severity.Green, false, 0);
}

/// <summary>
/// An actor's body: six sections, each green, yellow or red; blood lost so far; and whether it's
/// dead and why. The effects of wounds on movement, climbing and the senses are read from here.
/// The simulation changes it (Simulation.Injure, first aid, bleeding over time).
/// </summary>
public sealed class Health
{
    private readonly Wound[] _sections = new Wound[6];

    public Wound this[BodySection section] => _sections[(int)section];

    /// <summary>Bleeding so far, in ticks of one bleeding wound. At InjuryRules.BleedOutTicks it kills.</summary>
    public long BloodLossTicks { get; internal set; }

    /// <summary>When bleeding and healing were last brought up to date.</summary>
    public long LastUpdatedTick { get; internal set; }

    public string? DeathCause { get; internal set; }

    public long DiedTick { get; internal set; }

    public bool Dead => DeathCause is not null;

    public int BleedingWounds => _sections.Count(w => w.Bleeding);

    /// <summary>Seconds of bleeding left at <paramref name="now"/> before bleeding out, or null if not bleeding.</summary>
    public long? BleedTicksLeft(long now)
    {
        int bleeding = BleedingWounds;
        if (bleeding == 0)
            return null;
        long lost = BloodLossTicks + Math.Max(0, now - LastUpdatedTick) * bleeding;
        return Math.Max(0, (InjuryRules.BleedOutTicks - lost) / bleeding);
    }

    /// <summary>The red section first aid should see to: bleeding ones first, head to toe.</summary>
    public BodySection? WorstUntreated()
    {
        for (int pass = 0; pass < 2; pass++)
        {
            for (int i = 0; i < _sections.Length; i++)
            {
                if (_sections[i].Severity == Severity.Red && (pass == 1 || _sections[i].Bleeding))
                    return (BodySection)i;
            }
        }
        return null;
    }

    private Severity Leg(BodySection leg) => _sections[(int)leg].Severity;

    /// <summary>Step time in percent: +30 per yellow leg, +100 per red leg (a limp; both red is a crawl).</summary>
    public int StepCostPercent =>
        100 + LegPenalty(Leg(BodySection.LeftLeg), 30, 100) + LegPenalty(Leg(BodySection.RightLeg), 30, 100);

    /// <summary>Extra footstep loudness: +3 per yellow leg, +6 per red leg.</summary>
    public int FootstepBonus =>
        LegPenalty(Leg(BodySection.LeftLeg), 3, 6) + LegPenalty(Leg(BodySection.RightLeg), 3, 6);

    /// <summary>Climbing is out with a red torso, arm or leg.</summary>
    public bool CanClimb =>
        this[BodySection.Torso].Severity != Severity.Red
        && this[BodySection.LeftArm].Severity != Severity.Red && this[BodySection.RightArm].Severity != Severity.Red
        && this[BodySection.LeftLeg].Severity != Severity.Red && this[BodySection.RightLeg].Severity != Severity.Red;

    /// <summary>Climb time in percent: +50 per yellow arm.</summary>
    public int ClimbCostPercent =>
        100 + LegPenalty(this[BodySection.LeftArm].Severity, 50, 0) + LegPenalty(this[BodySection.RightArm].Severity, 50, 0);

    /// <summary>Rappelling is out with a broken (red) arm.</summary>
    public bool CanRappel =>
        this[BodySection.LeftArm].Severity != Severity.Red && this[BodySection.RightArm].Severity != Severity.Red;

    /// <summary>Hurt (yellow) arms: each makes a rappel a gamble and shortens throws.</summary>
    public int YellowArms =>
        (this[BodySection.LeftArm].Severity == Severity.Yellow ? 1 : 0)
        + (this[BodySection.RightArm].Severity == Severity.Yellow ? 1 : 0);

    /// <summary>Throw range in percent: −25% per yellow arm; 0 with both arms red.</summary>
    public int ThrowRangePercent => CanTreat ? 100 - 25 * YellowArms : 0;

    /// <summary>Sprinting is out with a red leg or torso.</summary>
    public bool CanSprint =>
        this[BodySection.Torso].Severity != Severity.Red
        && this[BodySection.LeftLeg].Severity != Severity.Red && this[BodySection.RightLeg].Severity != Severity.Red;

    /// <summary>A hurt (yellow) torso makes sprinting cost more.</summary>
    public bool SprintWinded => this[BodySection.Torso].Severity == Severity.Yellow;

    /// <summary>First aid needs at least one working arm.</summary>
    public bool CanTreat =>
        this[BodySection.LeftArm].Severity != Severity.Red || this[BodySection.RightArm].Severity != Severity.Red;

    /// <summary>A red head: concussed, may black out.</summary>
    public bool Concussed => this[BodySection.Head].Severity == Severity.Red;

    /// <summary>
    /// Senses as the wounds leave them: a yellow head blurs the edges of vision (three quarters of the
    /// sight range); a red head halves it and dulls hearing.
    /// </summary>
    public SenseProfile Adjust(SenseProfile senses) => this[BodySection.Head].Severity switch
    {
        Severity.Yellow => senses with { SightRangeTiles = senses.SightRangeTiles * 3 / 4 },
        Severity.Red => senses with
        {
            SightRangeTiles = senses.SightRangeTiles / 2,
            HearingThreshold = senses.HearingThreshold + InjuryRules.ConcussionHearingPenalty,
        },
        _ => senses,
    };

    internal void Set(BodySection section, Wound wound) => _sections[(int)section] = wound;

    private static int LegPenalty(Severity severity, int yellow, int red) => severity switch
    {
        Severity.Yellow => yellow,
        Severity.Red => red,
        _ => 0,
    };
}
