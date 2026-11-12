namespace BlueHarvest.Sim.World;

/// <summary>
/// The time of day and the day count, worked out from the tick. Calendar time runs ten times faster
/// than action time: sixty seconds of actions are ten game minutes, so a game hour is 360 action
/// seconds and a full day 144 action minutes. Bodies and physics stay on ticks; the sky, tides and
/// weather fronts go by this clock. It has no state of its own, so the same tick always reads the
/// same time.
/// </summary>
/// <param name="StartMinute">The clock's reading at tick 0, in game minutes since midnight at the start of day 1.</param>
public sealed record WorldClock(long StartMinute)
{
    /// <summary>The one number that sets how fast the day goes: 600 ticks (six action seconds) to a game minute.</summary>
    public const int TicksPerGameMinute = 600;

    public const int MinutesPerDay = 24 * 60;
    public const long TicksPerGameHour = 60L * TicksPerGameMinute;
    public const long TicksPerGameDay = (long)MinutesPerDay * TicksPerGameMinute;

    /// <summary>A clock reading this time of day (on day 1) at tick 0.</summary>
    public static WorldClock StartingAt(int hour, int minute = 0) => new(MinuteOfDayFor(hour, minute));

    /// <summary>Game minutes since midnight at the start of day 1.</summary>
    public long TotalMinutes(long tick) => StartMinute + tick / TicksPerGameMinute;

    /// <summary>Minutes since midnight, 0–1439.</summary>
    public int MinuteOfDay(long tick) => (int)(TotalMinutes(tick) % MinutesPerDay);

    public int Hour(long tick) => MinuteOfDay(tick) / 60;

    public int Minute(long tick) => MinuteOfDay(tick) % 60;

    /// <summary>The day number, starting at 1.</summary>
    public int Day(long tick) => (int)(TotalMinutes(tick) / MinutesPerDay) + 1;

    /// <summary>"Day 2, 14:30". Minutes are shown in steps (ten by default), as a watch you glance at.</summary>
    public string Describe(long tick, int stepMinutes = 10)
    {
        int step = Math.Max(1, stepMinutes);
        return $"Day {Day(tick)}, {Hour(tick):D2}:{Minute(tick) / step * step:D2}";
    }

    /// <summary>How many ticks from now until the clock next reads this time; a whole day if it reads it now.</summary>
    public long TicksUntil(long tick, int hour, int minute = 0)
    {
        long now = (StartMinute * TicksPerGameMinute + tick) % TicksPerGameDay;
        long target = (long)MinuteOfDayFor(hour, minute) * TicksPerGameMinute;
        long wait = (target - now + TicksPerGameDay) % TicksPerGameDay;
        return wait == 0 ? TicksPerGameDay : wait;
    }

    private static int MinuteOfDayFor(int hour, int minute)
    {
        if (hour < 0 || hour > 23) throw new ArgumentOutOfRangeException(nameof(hour), "Hours run 0 to 23.");
        if (minute < 0 || minute > 59) throw new ArgumentOutOfRangeException(nameof(minute), "Minutes run 0 to 59.");
        return hour * 60 + minute;
    }
}
