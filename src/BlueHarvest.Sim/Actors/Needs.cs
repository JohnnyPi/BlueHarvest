using BlueHarvest.Sim.Injuries;
using BlueHarvest.Sim.World;

namespace BlueHarvest.Sim.Actors;

/// <summary>What a body needs. Order matters: stage arrays index by it. Append only (warmth and sickness come next).</summary>
public enum NeedKind : byte
{
    Hunger,
    Thirst,
    Sleep,
    Pain,
    Stamina,
}

/// <summary>
/// The effects of the needs together: the most stamina there can be (of
/// <see cref="NeedRules.MaxStamina"/>), how fast it comes back (percent), what a step costs
/// (percent), the chance of a blackout on a turn (percent), sight range (percent) and how much
/// louder sounds must be to be heard.
/// </summary>
public readonly record struct NeedEffects(int StaminaCap, int RegenPercent, int StepCostPercent, int BlackoutPercent,
    int SightPercent, int HearingPenalty)
{
    public static readonly NeedEffects None = new(NeedRules.MaxStamina, 100, 100, 0, 100, 0);
}

/// <summary>How someone starts out: a little short of food and water, and awake for a while (from 07:00 by the clock if null).</summary>
public sealed record NeedsStart(int HungerKcal = 300, int ThirstMl = 200, int? AwakeMinutes = null);

/// <summary>
/// Numbers for hunger, thirst, sleep, pain and stamina. Hunger, thirst and sleep go by game time (a
/// game minute is <c>WorldClock.TicksPerGameMinute</c> ticks); stamina and adrenaline by action time.
/// Hunger is kept in tenths of a kcal short, thirst in tenths of a ml short, sleep in minutes awake
/// (sleep pays back two for one). Stages count from 0 (nothing to say).
/// </summary>
public static class NeedRules
{
    /// <summary>About 2000 kcal and 2.4 L a day awake; less asleep.</summary>
    public const int HungerPerMinute = 14, HungerAsleepPerMinute = 10, ThirstPerMinute = 17, ThirstAsleepPerMinute = 12;

    /// <summary>A minute asleep pays back this many minutes awake.</summary>
    public const int SleepRecoveryPerMinute = 2;

    /// <summary>Hungry 1500 kcal short (about a day), weak 4000, starving 10 000; starved at 30 000.</summary>
    public static readonly IReadOnlyList<int> HungerStages = [15_000, 40_000, 100_000];
    public const int StarvedAt = 300_000;

    /// <summary>Thirsty 800 ml short (half a day), parched 2 L, dehydrated 3.5 L, dying 5 L; dead at 6 L (about two and a half days).</summary>
    public static readonly IReadOnlyList<int> ThirstStages = [8_000, 20_000, 35_000, 50_000];
    public const int DiedOfThirstAt = 60_000;

    /// <summary>Tired after 16 h awake, exhausted 24 h, dead on your feet 36 h, collapsing 48 h; dead at 96 h.</summary>
    public static readonly IReadOnlyList<int> SleepStages = [960, 1440, 2160, 2880];
    public const int DiedOfExhaustionAt = 5760;

    /// <summary>Aching, hurting, in agony: felt pain (wounds less relief).</summary>
    public static readonly IReadOnlyList<int> PainStages = [10, 30, 60];

    /// <summary>Pain from a wound: a yellow one, a red one, and more while it bleeds.</summary>
    public const int YellowPain = 15, RedPain = 35, BleedingPain = 5;

    /// <summary>Painkillers: this much relief for 4 game hours.</summary>
    public const int PainkillerRelief = 40, PainkillerTicks = 240 * WorldClock.TicksPerGameMinute;

    /// <summary>Adrenaline: pain shut out and full breath, for 30 s.</summary>
    public const int AdrenalineRelief = 100, AdrenalineTicks = 3000;

    /// <summary>Stamina: 1000 full. Winded under 300, out of breath under 100 (no sprinting).</summary>
    public const int MaxStamina = 1000, WindedBelow = 300, SprintMinimum = 100;

    /// <summary>What a sprinting step and a climbing step cost: about 20 s of sprinting on full breath.</summary>
    public const int SprintStepCost = 20, ClimbStepCost = 40;

    /// <summary>Breath comes back from nothing to full in 60 s standing still, half as fast on the move.</summary>
    public const int RegenFullTicks = 6000, MovingRegenPercent = 50;

    /// <summary>Exertion burns food and water: per point of stamina spent, a tenth of a kcal and 0.15 ml.</summary>
    public const int ExertionHunger = 1, ExertionThirstPercent = 150;

    /// <summary>A blackout from thirst or exhaustion: 4 s lost.</summary>
    public const int BlackoutTicks = 400;

    /// <summary>You can't sleep until you've been up 4 hours.</summary>
    public const int MinFatigueToSleep = 240;

    /// <summary>Asleep: blind, and only a perceived loudness of 7 or more wakes you (3 is what an awake person hears).</summary>
    public const int WakeLoudness = 7, AsleepHearingPenalty = 4;

    // effects by stage, indexed by NeedKind and then stage
    private static readonly int[][] Cap =
    [
        [1000, 1000, 700, 400], [1000, 1000, 750, 500, 250], [1000, 1000, 700, 600, 500], [1000, 1000, 1000, 600], [1000, 1000, 1000],
    ];
    private static readonly int[][] Regen =
    [
        [100, 75, 60, 40], [100, 100, 75, 50, 30], [100, 75, 60, 50, 40], [100, 100, 50, 30], [100, 100, 100],
    ];
    private static readonly int[][] StepExtra =
    [
        [0, 0, 10, 25], [0, 0, 0, 15, 30], [0, 0, 0, 10, 20], [0, 0, 0, 10], [0, 0, 0],
    ];
    private static readonly int[][] Blackout =
    [
        [0, 0, 0, 0], [0, 0, 0, 2, 5], [0, 0, 0, 3, 8], [0, 0, 0, 0], [0, 0, 0],
    ];
    private static readonly int[][] Sight =
    [
        [100, 100, 100, 100], [100, 100, 100, 100, 80], [100, 100, 100, 85, 70], [100, 100, 100, 100], [100, 100, 100],
    ];
    private static readonly int[][] Hearing =
    [
        [0, 0, 0, 0], [0, 0, 0, 0, 0], [0, 0, 1, 2, 3], [0, 0, 0, 2], [0, 0, 0],
    ];

    /// <summary>The effects of one need at one stage.</summary>
    public static NeedEffects Of(NeedKind kind, int stage)
    {
        int k = (int)kind;
        int s = Math.Clamp(stage, 0, Cap[k].Length - 1);
        return new NeedEffects(Cap[k][s], Regen[k][s], 100 + StepExtra[k][s], Blackout[k][s], Sight[k][s], Hearing[k][s]);
    }

    /// <summary>The most a need's stage can be.</summary>
    public static int TopStage(NeedKind kind) => Cap[(int)kind].Length - 1;

    /// <summary>How many of <paramref name="thresholds"/> <paramref name="value"/> has reached.</summary>
    public static int StageOf(int value, IReadOnlyList<int> thresholds)
    {
        int stage = 0;
        while (stage < thresholds.Count && value >= thresholds[stage])
            stage++;
        return stage;
    }

    /// <summary>The word for a need at a stage ("Thirsty"), or "" at 0.</summary>
    public static string Word(NeedKind kind, int stage) => (kind, stage) switch
    {
        (_, <= 0) => "",
        (NeedKind.Hunger, 1) => "Hungry",
        (NeedKind.Hunger, 2) => "Weak with hunger",
        (NeedKind.Hunger, _) => "Starving",
        (NeedKind.Thirst, 1) => "Thirsty",
        (NeedKind.Thirst, 2) => "Parched",
        (NeedKind.Thirst, 3) => "Dehydrated",
        (NeedKind.Thirst, _) => "Dying of thirst",
        (NeedKind.Sleep, 1) => "Tired",
        (NeedKind.Sleep, 2) => "Exhausted",
        (NeedKind.Sleep, 3) => "Dead on your feet",
        (NeedKind.Sleep, _) => "Collapsing",
        (NeedKind.Pain, 1) => "Aching",
        (NeedKind.Pain, 2) => "Hurting",
        (NeedKind.Pain, _) => "In agony",
        (NeedKind.Stamina, 1) => "Winded",
        _ => "Out of breath",
    };
}

/// <summary>
/// Food and drink: what one of a food gives when it's eaten, and what a drink gives by the ml. Water
/// counts whatever it comes in; coffee and soda keep you awake a while.
/// </summary>
public static class Nutrition
{
    /// <summary>One eaten: kcal, ml of water, and minutes of tiredness it takes off.</summary>
    public static (int Kcal, int WaterMl, int AlertMinutes) Food(ItemKind kind) => kind switch
    {
        ItemKind.EnergyBar => (250, 0, 0),
        ItemKind.CannedFood => (400, 150, 0),
        ItemKind.Mre => (1200, 0, 0),
        ItemKind.Crackers => (450, 0, 0),
        ItemKind.Jerky => (80, 0, 0),
        ItemKind.DriedFruit => (350, 0, 0),
        ItemKind.Chocolate => (500, 0, 20),
        ItemKind.Noodles => (380, 0, 0),
        ItemKind.CannedPeaches => (300, 300, 0),
        ItemKind.Coconut => (350, 300, 0),
        _ => (0, 0, 0),
    };

    /// <summary><paramref name="ml"/> drunk: kcal, ml of water, and minutes of tiredness it takes off.</summary>
    public static (int Kcal, int WaterMl, int AlertMinutes) Drink(ItemKind kind, int ml) => kind switch
    {
        ItemKind.Soda => (ml * 140 / 330, ml * 9 / 10, ml * 30 / 330),
        ItemKind.Thermos => (0, ml, ml * 120 / 250),
        _ => (0, ml, 0),
    };
}

/// <summary>
/// Someone's hunger, thirst, tiredness, breath and painkillers (pain itself is read from their
/// wounds). The simulation keeps it up to date (Simulation.UpdateNeeds); only actors given needs
/// (Simulation.UseNeeds) have one.
/// </summary>
public sealed class Needs
{
    internal Needs(int hunger, int thirst, int fatigue, long now)
    {
        Hunger = hunger;
        Thirst = thirst;
        Fatigue = fatigue;
        Minute = now / WorldClock.TicksPerGameMinute;
        StaminaTick = now;
        WakeCheckTick = now;
    }

    /// <summary>Tenths of a kcal short.</summary>
    public int Hunger { get; internal set; }

    /// <summary>Tenths of a ml of water short.</summary>
    public int Thirst { get; internal set; }

    /// <summary>Minutes awake, as far as the body is concerned (sleep pays them back two for one).</summary>
    public int Fatigue { get; internal set; }

    /// <summary>Breath, 0 to <see cref="NeedRules.MaxStamina"/>.</summary>
    public int Stamina { get; internal set; } = NeedRules.MaxStamina;

    public bool Asleep { get; internal set; }

    /// <summary>Until when painkillers are working.</summary>
    public long PainkillersUntilTick { get; internal set; } = long.MinValue;

    /// <summary>Until when adrenaline is working.</summary>
    public long AdrenalineUntilTick { get; internal set; } = long.MinValue;

    public int HungerKcal => Hunger / 10;

    public int ThirstMl => Thirst / 10;

    /// <summary>The game minute counted up to.</summary>
    internal long Minute { get; set; }

    /// <summary>Breath comes back from here on (a sprint or a climb in progress pushes it on).</summary>
    internal long StaminaTick { get; set; }

    internal long StaminaRemainder { get; set; }

    /// <summary>When they last woke by themselves.</summary>
    internal long WokeTick { get; set; } = long.MinValue;

    /// <summary>Noises before this have been listened for while asleep.</summary>
    internal long WakeCheckTick { get; set; }

    /// <summary>The stage of each need when last reported.</summary>
    internal int[] Reported { get; } = new int[Enum.GetValues<NeedKind>().Length];

    public bool OnPainkillers(long now) => PainkillersUntilTick > now;

    public bool OnAdrenaline(long now) => AdrenalineUntilTick > now;

    /// <summary>Pain from wounds: each yellow one, each red one, more while bleeding.</summary>
    public static int WoundPain(Health health)
    {
        int pain = 0;
        foreach (var section in Enum.GetValues<BodySection>())
        {
            var wound = health[section];
            pain += wound.Severity switch
            {
                Severity.Yellow => NeedRules.YellowPain,
                Severity.Red => NeedRules.RedPain,
                _ => 0,
            };
            if (wound.Bleeding)
                pain += NeedRules.BleedingPain;
        }
        return pain;
    }

    /// <summary>How much pain is held off now.</summary>
    public int Relief(long now) => Math.Max(OnAdrenaline(now) ? NeedRules.AdrenalineRelief : 0, OnPainkillers(now) ? NeedRules.PainkillerRelief : 0);

    /// <summary>The pain felt: the wounds' less the relief.</summary>
    public int Pain(Health health, long now) => Math.Max(0, WoundPain(health) - Relief(now));

    /// <summary>A need's stage now.</summary>
    public int Stage(NeedKind kind, Health health, long now) => kind switch
    {
        NeedKind.Hunger => NeedRules.StageOf(Hunger, NeedRules.HungerStages),
        NeedKind.Thirst => NeedRules.StageOf(Thirst, NeedRules.ThirstStages),
        NeedKind.Sleep => NeedRules.StageOf(Fatigue, NeedRules.SleepStages),
        NeedKind.Pain => NeedRules.StageOf(Pain(health, now), NeedRules.PainStages),
        _ => Stamina < NeedRules.SprintMinimum ? 2 : Stamina < NeedRules.WindedBelow ? 1 : 0,
    };

    /// <summary>
    /// What the needs do together: the lowest stamina cap, the regeneration rates multiplied, the step
    /// penalties added, the worst blackout chance and sight, the most hearing lost. Adrenaline lifts
    /// the cap. Breath itself (stamina) only gates sprinting.
    /// </summary>
    public NeedEffects Effects(Health health, long now)
    {
        int cap = NeedRules.MaxStamina, regen = 100, step = 100, blackout = 0, sight = 100, hearing = 0;
        foreach (var kind in new[] { NeedKind.Hunger, NeedKind.Thirst, NeedKind.Sleep, NeedKind.Pain })
        {
            var e = NeedRules.Of(kind, Stage(kind, health, now));
            cap = Math.Min(cap, e.StaminaCap);
            regen = regen * e.RegenPercent / 100;
            step += e.StepCostPercent - 100;
            blackout = Math.Max(blackout, e.BlackoutPercent);
            sight = Math.Min(sight, e.SightPercent);
            hearing = Math.Max(hearing, e.HearingPenalty);
        }
        if (OnAdrenaline(now))
            cap = NeedRules.MaxStamina;
        return new NeedEffects(cap, regen, step, blackout, sight, hearing);
    }
}
