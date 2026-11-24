namespace BlueHarvest.Sim.Campaigns;

/// <summary>
/// How long the island waits between expeditions (D-7, F-5): 28 days after a death and 14 after an extraction. Only an
/// extraction's own intermission currency buys days off, at one per day, down to a floor of three; a death buys none.
/// </summary>
public static class IntermissionRules
{
    public const int DaysAfterDeath = 28;
    public const int DaysAfterExtraction = 14;

    /// <summary>The fewest days an intermission can be bought down to.</summary>
    public const int FloorDays = 3;

    /// <summary>What a day off costs in intermission currency.</summary>
    public const int CurrencyPerDay = 1;

    /// <summary>The days an expedition's end leaves before the next arrival, before any are bought off.</summary>
    public static int BaseDays(ExpeditionOutcome outcome) => outcome switch
    {
        ExpeditionOutcome.Died => DaysAfterDeath,
        ExpeditionOutcome.Extracted => DaysAfterExtraction,
        _ => throw new ArgumentOutOfRangeException(nameof(outcome), "Only an ended expedition has an intermission."),
    };

    /// <summary>The most days this end and this much currency can buy off.</summary>
    public static int MostDaysBought(ExpeditionOutcome outcome, int currency) => outcome == ExpeditionOutcome.Extracted
        ? Math.Clamp(currency / CurrencyPerDay, 0, DaysAfterExtraction - FloorDays)
        : 0;
}

/// <summary>
/// One intermission (P3c): the days it lasts, where the clock started and must reach, and how far it has got. The
/// campaign saves it, so an intermission interrupted part way resumes from where it was and comes out the same.
/// What the hour-by-hour passing skipped or summed up is counted here too.
/// </summary>
public sealed class IntermissionState
{
    internal IntermissionState(int number, string expeditionId, ExpeditionOutcome outcome, int daysBought, long startTick)
    {
        Number = number;
        ExpeditionId = expeditionId;
        Outcome = outcome;
        BaseDays = IntermissionRules.BaseDays(outcome);
        DaysBought = daysBought;
        CurrencySpent = daysBought * IntermissionRules.CurrencyPerDay;
        StartTick = startTick;
    }

    /// <summary>1 for the campaign's first intermission, and so on.</summary>
    public int Number { get; }

    /// <summary>The expedition whose end began it.</summary>
    public string ExpeditionId { get; }

    public ExpeditionOutcome Outcome { get; }

    public int BaseDays { get; }

    public int DaysBought { get; }

    /// <summary>The days that pass.</summary>
    public int Days => BaseDays - DaysBought;

    /// <summary>The intermission currency the bought days cost (the rest of it was lost).</summary>
    public int CurrencySpent { get; }

    public long StartTick { get; }

    /// <summary>The tick the clock reaches when it's over.</summary>
    public long TargetTick => StartTick + Days * 24L * Simulation.IntermissionStepTicks;

    /// <summary>The hour-long steps taken so far.</summary>
    public int HoursPassed { get; internal set; }

    /// <summary>Lightning that would have struck, counted rather than struck.</summary>
    public int Strikes { get; internal set; }

    /// <summary>Hours that began with a storm overhead.</summary>
    public int StormHours { get; internal set; }

    /// <summary>Fires that were burning when it began and burnt out where they stood.</summary>
    public int FiresBurntOut { get; internal set; }

    /// <summary>Changes the power network went through (trips, generators starting and running dry, and the like).</summary>
    public int PowerEvents { get; internal set; }

    /// <summary>Whether all its days have passed.</summary>
    public bool Done { get; internal set; }

    internal void Add(IntermissionHour hour)
    {
        HoursPassed++;
        Strikes += hour.Strikes;
        StormHours += hour.Storm ? 1 : 0;
        FiresBurntOut += hour.FiresBurntOut;
        PowerEvents += hour.PowerEvents;
    }
}
