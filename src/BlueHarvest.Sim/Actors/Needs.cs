using BlueHarvest.Sim.Injuries;
using BlueHarvest.Sim.World;

namespace BlueHarvest.Sim.Actors;

/// <summary>What a body needs. Order matters: stage arrays index by it. Append only.</summary>
public enum NeedKind : byte
{
    Hunger,
    Thirst,
    Sleep,
    Pain,
    Stamina,
    Warmth,
    Sickness,
}

/// <summary>
/// The effects of the needs together: the most stamina there can be (of
/// <see cref="NeedRules.MaxStamina"/>), how fast it comes back (percent), what a step costs
/// (percent), the chance of a blackout on a turn (percent), sight range (percent) and how much
/// louder sounds must be to be heard; and how long things done with the hands take (percent: using
/// items, climbing).
/// </summary>
public readonly record struct NeedEffects(int StaminaCap, int RegenPercent, int StepCostPercent, int BlackoutPercent,
    int SightPercent, int HearingPenalty, int HandsPercent = 100)
{
    public static readonly NeedEffects None = new(NeedRules.MaxStamina, 100, 100, 0, 100, 0, 100);
}

/// <summary>How someone starts out: a little short of food and water, and awake for a while (from 07:00 by the clock if null); or asleep.</summary>
public sealed record NeedsStart(int HungerKcal = 300, int ThirstMl = 200, int? AwakeMinutes = null, bool Asleep = false);

/// <summary>
/// How a kind of body has its needs (step 23b): how fast it hungers and thirsts compared with a
/// person (hunger and thirst are counted in a person's kcal and ml, so the stages read the same), when
/// it sleeps (game minutes of the day, wrapping past midnight), what a kill gives it, and whether cold
/// and bad water touch it. A big predator goes days between meals and gorges.
/// </summary>
public sealed record NeedProfile(
    int HungerPercent = 100,
    int ThirstPercent = 100,
    int SleepFromMinute = 23 * 60,
    int SleepToMinute = 7 * 60,
    int MealKcal = 0,
    bool FeelsCold = true,
    bool CatchesIllness = true)
{
    /// <summary>People: the rates the player has.</summary>
    public static readonly NeedProfile Human = new();

    /// <summary>The Rex: hungry about five days after a meal, thirsty in about 20 hours; sleeps 21:00 to 05:00; a kill fills it.</summary>
    public static readonly NeedProfile Rex = new(HungerPercent: 15, ThirstPercent: 40, SleepFromMinute: 21 * 60, SleepToMinute: 5 * 60,
        MealKcal: 20_000, FeelsCold: false, CatchesIllness: false);

    /// <summary>Raptors: hungry in a day and a half, thirsty in about 13 hours; hunt by night and sleep 09:00 to 16:00.</summary>
    public static readonly NeedProfile Raptor = new(HungerPercent: 50, ThirstPercent: 60, SleepFromMinute: 9 * 60, SleepToMinute: 16 * 60,
        MealKcal: 8_000, FeelsCold: false, CatchesIllness: false);

    public static NeedProfile For(Species species) =>
        species == Species.Rex ? Rex : species == Species.Raptor ? Raptor : Human;

    /// <summary>Whether a minute of the day falls in its sleeping hours.</summary>
    public bool SleepTime(int minuteOfDay) =>
        SleepFromMinute <= SleepToMinute
            ? minuteOfDay >= SleepFromMinute && minuteOfDay < SleepToMinute
            : minuteOfDay >= SleepFromMinute || minuteOfDay < SleepToMinute;

    /// <summary>Minutes since it last woke at its usual time, for a minute of the day it's awake at.</summary>
    public int MinutesSinceWaking(int minuteOfDay) => (minuteOfDay - SleepToMinute + 24 * 60) % (24 * 60);
}

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

    // ---------- warmth (22b) ----------

    /// <summary>
    /// Air temperature, in tenths of a degree: the place's mean (26 °C unless set), 4 °C colder at 05:00
    /// and warmer at 15:00, 6.5 °C colder a kilometre up, 2 °C colder in rain, 2 °C warmer in the sun.
    /// </summary>
    public const int DefaultBaseTemperature = 260, DaySwing = 40, RainCooling = 20, SunWarming = 20;

    /// <summary>
    /// What changes how warm it feels, in tenths of a degree: each m/s of wind outdoors (half under a
    /// poncho), being soaked (scaled by how wet), clothes, moving, being asleep, a fire close by.
    /// </summary>
    public const int WindChillPerMs = 5, SoakedChill = 80, MovingWarmth = 30, AsleepChill = 20, FireCloseWarmth = 120, FireNearWarmth = 50;

    /// <summary>Feeling 18 °C or more you warm up; below it you lose heat in proportion.</summary>
    public const int ComfortTemperature = 180;

    /// <summary>Chilly, cold, shivering, hypothermic: heat lost (a minute at 10 °C below comfort adds 10); dead of cold at 4800.</summary>
    public static readonly IReadOnlyList<int> WarmthStages = [240, 720, 1440, 2880];
    public const int DiedOfColdAt = 4800, MostWarmingPerMinute = 40;

    /// <summary>Above 30 °C (felt) you sweat: thirst grows 15% faster for each degree over.</summary>
    public const int SweatAbove = 300, SweatPercentPerDegree = 15;

    /// <summary>Wet 0–100: soaked is 80+. Wading leaves you at least 60.</summary>
    public const int WadingWet = 60, PonchoRainPercent = 25;

    // ---------- sickness (22b) ----------

    /// <summary>Queasy, sick, fevered, gravely ill (sickness 0–1500); infection kills at 1500.</summary>
    public static readonly IReadOnlyList<int> SicknessStages = [100, 300, 600, 900];
    public const int DiedOfInfectionAt = 1500;

    /// <summary>A drink of untreated water: one in five makes you ill, 6 hours later, for a day and a half.</summary>
    public const int BadWaterPercent = 20, GutIncubationTicks = 6 * 60 * WorldClock.TicksPerGameMinute,
        GutIllnessTicks = 36 * 60 * WorldClock.TicksPerGameMinute;

    /// <summary>A gut infection: queasy the first 4 hours and the last 8, sick in between (it never gets worse than sick).</summary>
    public const int GutQueasy = 150, GutSick = 400, GutRampTicks = 4 * 60 * WorldClock.TicksPerGameMinute,
        GutEaseTicks = 8 * 60 * WorldClock.TicksPerGameMinute;

    /// <summary>
    /// A red wound left uncleaned for 4 hours starts to go bad: a quarter point of sickness a minute for
    /// each such wound (sick in about 20 h, fevered 40 h, dead 100 h). Antiseptic, stitches, burn gel or a
    /// first-aid kit clean it.
    /// </summary>
    public const int InfectionGraceTicks = 4 * 60 * WorldClock.TicksPerGameMinute, InfectionQuarterPerWound = 1;

    /// <summary>A course of antibiotics: 12 hours, beating an infection back by 1.5 a minute; it cuts a gut infection short to 6 hours.</summary>
    public const int AntibioticTicks = 12 * 60 * WorldClock.TicksPerGameMinute, AntibioticQuarterCure = 6,
        AntibioticGutTicks = 6 * 60 * WorldClock.TicksPerGameMinute;

    /// <summary>A drink straight from a stream or pool: half a litre, 4 s.</summary>
    public const int DrinkFromWaterMl = 500, DrinkFromWaterTicks = 400, FillTicks = 300;

    /// <summary>A blackout from thirst or exhaustion: 4 s lost.</summary>
    public const int BlackoutTicks = 400;

    /// <summary>You can't sleep until you've been up 4 hours.</summary>
    public const int MinFatigueToSleep = 240;

    /// <summary>Asleep: blind, and only a perceived loudness of 7 or more wakes you (3 is what an awake person hears).</summary>
    public const int WakeLoudness = 7, AsleepHearingPenalty = 4;

    // effects by stage, indexed by NeedKind and then stage
    // rows: hunger, thirst, sleep, pain, stamina, warmth, sickness
    private static readonly int[][] Cap =
    [
        [1000, 1000, 700, 400], [1000, 1000, 750, 500, 250], [1000, 1000, 700, 600, 500], [1000, 1000, 1000, 600], [1000, 1000, 1000],
        [1000, 1000, 1000, 800, 500], [1000, 1000, 800, 600, 400],
    ];
    private static readonly int[][] Regen =
    [
        [100, 75, 60, 40], [100, 100, 75, 50, 30], [100, 75, 60, 50, 40], [100, 100, 50, 30], [100, 100, 100],
        [100, 100, 90, 70, 40], [100, 90, 70, 50, 30],
    ];
    private static readonly int[][] StepExtra =
    [
        [0, 0, 10, 25], [0, 0, 0, 15, 30], [0, 0, 0, 10, 20], [0, 0, 0, 10], [0, 0, 0],
        [0, 0, 0, 10, 25], [0, 0, 0, 10, 20],
    ];
    private static readonly int[][] Blackout =
    [
        [0, 0, 0, 0], [0, 0, 0, 2, 5], [0, 0, 0, 3, 8], [0, 0, 0, 0], [0, 0, 0],
        [0, 0, 0, 0, 5], [0, 0, 0, 0, 3],
    ];
    private static readonly int[][] Sight =
    [
        [100, 100, 100, 100], [100, 100, 100, 100, 80], [100, 100, 100, 85, 70], [100, 100, 100, 100], [100, 100, 100],
        [100, 100, 100, 100, 85], [100, 100, 100, 100, 85],
    ];
    private static readonly int[][] Hearing =
    [
        [0, 0, 0, 0], [0, 0, 0, 0, 0], [0, 0, 1, 2, 3], [0, 0, 0, 2], [0, 0, 0],
        [0, 0, 0, 0, 1], [0, 0, 0, 1, 2],
    ];
    private static readonly int[][] Hands =
    [
        [100, 100, 100, 100], [100, 100, 100, 100, 100], [100, 100, 100, 100, 100], [100, 100, 100, 100], [100, 100, 100],
        [100, 100, 110, 130, 160], [100, 100, 100, 110, 120],
    ];

    /// <summary>The effects of one need at one stage.</summary>
    public static NeedEffects Of(NeedKind kind, int stage)
    {
        int k = (int)kind;
        int s = Math.Clamp(stage, 0, Cap[k].Length - 1);
        return new NeedEffects(Cap[k][s], Regen[k][s], 100 + StepExtra[k][s], Blackout[k][s], Sight[k][s], Hearing[k][s], Hands[k][s]);
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
        (NeedKind.Stamina, _) => "Out of breath",
        (NeedKind.Warmth, 1) => "Chilly",
        (NeedKind.Warmth, 2) => "Cold",
        (NeedKind.Warmth, 3) => "Shivering",
        (NeedKind.Warmth, _) => "Hypothermic",
        (NeedKind.Sickness, 1) => "Queasy",
        (NeedKind.Sickness, 2) => "Sick",
        (NeedKind.Sickness, 3) => "Fevered",
        _ => "Gravely ill",
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
    internal Needs(int hunger, int thirst, int fatigue, long now, NeedProfile? profile = null)
    {
        Profile = profile ?? NeedProfile.Human;
        Hunger = hunger;
        Thirst = thirst;
        Fatigue = fatigue;
        Minute = now / WorldClock.TicksPerGameMinute;
        StaminaTick = now;
        WakeCheckTick = now;
    }

    /// <summary>How this body has its needs (rates, sleeping hours, what a kill gives).</summary>
    public NeedProfile Profile { get; }

    /// <summary>When it last ate a kill (long.MinValue: never).</summary>
    public long FedTick { get; internal set; } = long.MinValue;

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

    /// <summary>Heat lost (0 warm; see <see cref="NeedRules.WarmthStages"/>).</summary>
    public int Chill { get; internal set; }

    /// <summary>How wet: 0 dry, 100 soaked through.</summary>
    public int Wet { get; internal set; }

    /// <summary>A gut infection from bad water: when it comes on and when it's over (long.MinValue: none).</summary>
    public long GutOnsetTick { get; internal set; } = long.MinValue;

    public long GutEndTick { get; internal set; } = long.MinValue;

    /// <summary>Infection from dirty wounds, in quarter points of sickness.</summary>
    public int Infection { get; internal set; }

    /// <summary>Until when a course of antibiotics is working.</summary>
    public long AntibioticsUntilTick { get; internal set; } = long.MinValue;

    /// <summary>When each section's wounds were last cleaned (a red wound since then is dirty).</summary>
    internal long[] CleanedTick { get; } = Enumerable.Repeat(long.MinValue, 6).ToArray();

    public bool OnAntibiotics(long now) => AntibioticsUntilTick > now;

    /// <summary>Whether a gut infection is coming or running.</summary>
    public bool GutInfected(long now) => GutEndTick > now;

    /// <summary>The gut infection's sickness now: 0 before it comes on, queasy at the start and the end, sick between.</summary>
    public int GutSickness(long now)
    {
        if (now < GutOnsetTick || now >= GutEndTick)
            return 0;
        return now - GutOnsetTick < NeedRules.GutRampTicks || GutEndTick - now <= NeedRules.GutEaseTicks ? NeedRules.GutQueasy : NeedRules.GutSick;
    }

    /// <summary>Sickness now: the worse of a gut infection and a wound infection.</summary>
    public int Sickness(long now) => Math.Max(GutSickness(now), Infection / 4);

    /// <summary>Red wounds that have gone uncleaned past the grace time: each feeds an infection.</summary>
    public int DirtyWounds(Health health, long now)
    {
        int dirty = 0;
        foreach (var section in Enum.GetValues<BodySection>())
        {
            var wound = health[section];
            if (wound.Severity == Severity.Red && CleanedTick[(int)section] < wound.SinceTick && now - wound.SinceTick >= NeedRules.InfectionGraceTicks)
                dirty++;
        }
        return dirty;
    }

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
        NeedKind.Warmth => NeedRules.StageOf(Chill, NeedRules.WarmthStages),
        NeedKind.Sickness => NeedRules.StageOf(Sickness(now), NeedRules.SicknessStages),
        _ => Stamina < NeedRules.SprintMinimum ? 2 : Stamina < NeedRules.WindedBelow ? 1 : 0,
    };

    /// <summary>
    /// What the needs do together: the lowest stamina cap, the regeneration rates multiplied, the step
    /// penalties added, the worst blackout chance and sight, the most hearing lost. Adrenaline lifts
    /// the cap. Breath itself (stamina) only gates sprinting.
    /// </summary>
    public NeedEffects Effects(Health health, long now)
    {
        int cap = NeedRules.MaxStamina, regen = 100, step = 100, blackout = 0, sight = 100, hearing = 0, hands = 100;
        foreach (var kind in new[] { NeedKind.Hunger, NeedKind.Thirst, NeedKind.Sleep, NeedKind.Pain, NeedKind.Warmth, NeedKind.Sickness })
        {
            var e = NeedRules.Of(kind, Stage(kind, health, now));
            cap = Math.Min(cap, e.StaminaCap);
            regen = regen * e.RegenPercent / 100;
            step += e.StepCostPercent - 100;
            blackout = Math.Max(blackout, e.BlackoutPercent);
            sight = Math.Min(sight, e.SightPercent);
            hearing = Math.Max(hearing, e.HearingPenalty);
            hands = Math.Max(hands, e.HandsPercent);
        }
        if (OnAdrenaline(now))
            cap = NeedRules.MaxStamina;
        return new NeedEffects(cap, regen, step, blackout, sight, hearing, hands);
    }
}
