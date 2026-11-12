namespace BlueHarvest.Sim.World;

public enum PartOfDay
{
    Night,
    /// <summary>First light to full daylight.</summary>
    Dawn,
    Day,
    /// <summary>The sun going down, to full dark.</summary>
    Dusk,
}

/// <summary>
/// How much light the sky gives, from the time of day, the moon and the weather. All of it is whole
/// numbers worked out from the clock, with nothing stored: the island sits near the equator, so the
/// sun is up from six to six all year, with a short twilight.
/// </summary>
public static class Sky
{
    // minutes since midnight
    public const int FirstLight = 5 * 60 + 30;
    public const int Sunrise = 6 * 60;
    public const int FullDay = 7 * 60 + 30;
    public const int DuskStart = 16 * 60 + 30;
    public const int Sunset = 18 * 60;
    public const int FullDark = 18 * 60 + 30;

    /// <summary>Light with the sun on the horizon.</summary>
    public const int HorizonPercent = 25;

    /// <summary>A clear night with no moon.</summary>
    public const int StarlightPercent = 3;

    /// <summary>A clear night under a full moon.</summary>
    public const int FullMoonPercent = 12;

    /// <summary>The darkest it gets outdoors, under storm cloud at night.</summary>
    public const int DarkestPercent = 2;

    /// <summary>The moon's cycle counted in half days: 59 of them, 29.5 days.</summary>
    public const int MoonCycle = 59;

    /// <summary>Daylight on a clear day, 0–100: none at night, a quarter at sunrise and sunset, full from 07:30 to 16:30.</summary>
    public static int SunPercent(int minuteOfDay)
    {
        int m = minuteOfDay;
        if (m < FirstLight || m >= FullDark) return 0;
        if (m < Sunrise) return Ramp(m, FirstLight, Sunrise, 0, HorizonPercent);
        if (m < FullDay) return Ramp(m, Sunrise, FullDay, HorizonPercent, 100);
        if (m < DuskStart) return 100;
        if (m < Sunset) return Ramp(m, DuskStart, Sunset, 100, HorizonPercent);
        return Ramp(m, Sunset, FullDark, HorizonPercent, 0);
    }

    public static PartOfDay PartOf(int minuteOfDay) =>
        minuteOfDay < FirstLight || minuteOfDay >= FullDark ? PartOfDay.Night
        : minuteOfDay < FullDay ? PartOfDay.Dawn
        : minuteOfDay < DuskStart ? PartOfDay.Day
        : PartOfDay.Dusk;

    /// <summary>
    /// Where the moon is in its cycle, 0 to <see cref="MoonCycle"/> − 1: 0 is new, the middle is full.
    /// It moves on one step every half day.
    /// </summary>
    /// <param name="totalMinutes">Game minutes since the start of day 1 (WorldClock.TotalMinutes).</param>
    /// <param name="startPhase">The phase at the start of day 1: different for every seed.</param>
    public static int MoonPhase(long totalMinutes, int startPhase) =>
        (int)((startPhase + totalMinutes / (WorldClock.MinutesPerDay / 2)) % MoonCycle);

    /// <summary>How much of the moon's face is lit, 0 (new) to about 100 (full).</summary>
    public static int MoonLitPercent(int phase) => 100 - Math.Abs(2 * phase - MoonCycle) * 100 / MoonCycle;

    /// <summary>
    /// Whether the moon is above the horizon. It rises about 24 minutes later every half day: a new
    /// moon rises with the sun (and is no use at night), a full moon rises at sunset and is up all night.
    /// </summary>
    public static bool MoonUp(int phase, int minuteOfDay)
    {
        int rise = (Sunrise + phase * WorldClock.MinutesPerDay / MoonCycle) % WorldClock.MinutesPerDay;
        int sinceRise = (minuteOfDay - rise + WorldClock.MinutesPerDay) % WorldClock.MinutesPerDay;
        return sinceRise < WorldClock.MinutesPerDay / 2;
    }

    /// <summary>Light on a clear night: starlight, plus the moon by how full it is while it's up.</summary>
    public static int NightPercent(int phase, int minuteOfDay) =>
        StarlightPercent + (MoonUp(phase, minuteOfDay)
            ? ((FullMoonPercent - StarlightPercent) * MoonLitPercent(phase) + 50) / 100
            : 0);

    /// <summary>How much of the sky's light the weather takes away, 0–60: heavy rain about 40, thick fog 50, a storm 60.</summary>
    public static int CloudPercent(WeatherSettings weather) =>
        Math.Clamp(Math.Max(Math.Max(weather.RainPercent * 6 / 10, weather.FogPercent / 2), weather.Storm ? 60 : 0), 0, 60);

    /// <summary>
    /// Light outdoors before any lamps: the sun or the night sky, whichever is brighter, dimmed by the
    /// weather. 100 is a clear day; a clear moonless night is 3; it never goes below 2.
    /// </summary>
    public static int AmbientPercent(int minuteOfDay, int moonPhase, WeatherSettings weather)
    {
        int clear = Math.Max(SunPercent(minuteOfDay), NightPercent(moonPhase, minuteOfDay));
        return Math.Max(DarkestPercent, clear * (100 - CloudPercent(weather)) / 100);
    }

    /// <summary>"new moon", "crescent moon", "half moon", "gibbous moon" or "full moon".</summary>
    public static string MoonName(int phase) => MoonLitPercent(phase) switch
    {
        < 8 => "new moon",
        < 38 => "crescent moon",
        < 68 => "half moon",
        < 92 => "gibbous moon",
        _ => "full moon",
    };

    /// <summary>The value at <paramref name="m"/> on the straight line from (m0, v0) to (m1, v1).</summary>
    private static int Ramp(int m, int m0, int m1, int v0, int v1) => v0 + (v1 - v0) * (m - m0) / (m1 - m0);
}
