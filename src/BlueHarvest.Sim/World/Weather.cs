using BlueHarvest.Sim.Core;

namespace BlueHarvest.Sim.World;

/// <summary>
/// The weather a scenario asks for. All values are first guesses for testbed tuning.
/// </summary>
/// <param name="RainPercent">0 dry, 100 a downpour. Masks sound, washes scent out, shortens sight, softens ground.</param>
/// <param name="WindFrom">Where the wind blows from (an east wind blows from the east, carrying scent west).</param>
/// <param name="WindSpeed">Metres per second, 0 calm to 20 a gale. Carries scent downwind; gusts mask quiet sounds.</param>
/// <param name="FogPercent">0 clear, 100 thick fog. Shortens sight; sound is unaffected.</param>
/// <param name="Storm">Lightning and thunder: flashes light everything for a moment, thunder masks even loud noises.</param>
public sealed record WeatherSettings(
    int RainPercent = 0,
    Direction WindFrom = Direction.East,
    int WindSpeed = 0,
    int FogPercent = 0,
    bool Storm = false)
{
    public const int MaxWindSpeed = 20;

    /// <summary>No rain, no wind, no fog, no storm: the senses work as they did before weather existed.</summary>
    public static readonly WeatherSettings Clear = new();

    /// <summary>The testbed's night: heavy rain on a gusting easterly, with a thunderstorm overhead.</summary>
    public static readonly WeatherSettings TestbedStorm = new(RainPercent: 70, WindFrom: Direction.East, WindSpeed: 8, Storm: true);

    /// <summary>Values forced into range.</summary>
    public WeatherSettings Clamped() => this with
    {
        RainPercent = Math.Clamp(RainPercent, 0, 100),
        WindSpeed = Math.Clamp(WindSpeed, 0, MaxWindSpeed),
        FogPercent = Math.Clamp(FogPercent, 0, 100),
    };

    /// <summary>Plain words for the HUD and the runner: "heavy rain, wind from E 8 m/s, fog 0%, thunderstorm".</summary>
    public string Describe()
    {
        var parts = new List<string>
        {
            RainPercent switch
            {
                0 => "dry",
                < 30 => $"drizzle ({RainPercent}%)",
                < 70 => $"rain ({RainPercent}%)",
                _ => $"heavy rain ({RainPercent}%)",
            },
            WindSpeed == 0 ? "calm" : $"wind from {WindFrom.Compass()} {WindSpeed} m/s",
        };
        if (FogPercent > 0)
            parts.Add($"fog {FogPercent}%");
        if (Storm)
            parts.Add("thunderstorm");
        return string.Join(", ", parts);
    }
}

/// <summary>One lightning strike: when it flashes, and how long until its thunder arrives.</summary>
public readonly record struct LightningStrike(long Tick, int ThunderDelayTicks)
{
    public long ThunderTick => Tick + ThunderDelayTicks;
}

/// <summary>
/// The weather now, as the senses feel it. Settings change only when a scenario or debug key sets
/// them. Gusts and lightning use addressed randomness: a hash of (seed, stream, time window), so
/// they depend on the tick alone and never on the order things were asked, and nothing about them
/// needs saving beyond the seed and the settings.
/// </summary>
public sealed class Weather
{
    /// <summary>A gust lasts this long; the next window rolls a new strength.</summary>
    public const int GustWindowTicks = 200;

    /// <summary>A storm strikes at most once per window of this length.</summary>
    public const int StrikeWindowTicks = 1500;

    /// <summary>Chance a storm window has a strike in it.</summary>
    public const int StrikeChancePercent = 45;

    /// <summary>How long a flash lights everything up.</summary>
    public const int FlashTicks = 25;

    /// <summary>Outdoor light during a flash (indoor tiles get a third, as with any ambient light).</summary>
    public const int FlashAmbientPercent = 80;

    /// <summary>Thunder arrives this long after its flash, somewhere in this range (the storm is 0.3-1 km off).</summary>
    public const int ThunderDelayMinTicks = 100;
    public const int ThunderDelayMaxTicks = 300;

    /// <summary>How long thunder rolls, and how much it takes off every sound heard meanwhile.</summary>
    public const int ThunderTicks = 250;
    public const int ThunderMask = 35;

    /// <summary>Rain takes this much off every sound per 100% of rain (heavy rain, 70%: 14).</summary>
    public const int RainMaskAtFull = 20;

    /// <summary>How fast the scent clock runs in rain: 100% plus this much per 1% of rain (a downpour: ×5).</summary>
    public const int RainWashPerPercent = 4;

    private readonly ulong _seed;
    private long _washBase;
    private long _washSince;

    /// <param name="seed">The weather's own seed (the simulation forks "weather" from its RNG).</param>
    public Weather(ulong seed)
    {
        _seed = seed;
    }

    public WeatherSettings Settings { get; private set; } = WeatherSettings.Clear;

    /// <summary>Changes the weather from <paramref name="now"/> on. Scent laid before keeps the wash it had.</summary>
    internal void Set(WeatherSettings settings, long now)
    {
        _washBase = Wash(now);
        _washSince = now;
        Settings = settings.Clamped();
    }

    /// <summary>
    /// The scent clock, in hundredths of a tick: it runs at normal speed in the dry and faster in rain,
    /// so a trail's effective age is the clock's change since it was laid. Never runs backwards.
    /// </summary>
    public long Wash(long tick) =>
        _washBase + Math.Max(0, tick - _washSince) * (100 + Settings.RainPercent * RainWashPerPercent);

    /// <summary>Gust strength 0-100 at a tick; 0 in calm air.</summary>
    public int Gust(long tick) =>
        Settings.WindSpeed == 0 ? 0 : (int)(Roll("gust", Math.Max(0, tick) / GustWindowTicks) % 101);

    /// <summary>How much wind takes off sounds right now: the steady wind plus its gust.</summary>
    public int WindMask(long tick) => Settings.WindSpeed * (50 + Gust(tick)) / 200;

    /// <summary>
    /// How much every sound heard at this tick loses to the weather: rain hiss, wind and gusts, and
    /// thunder while it rolls. 0 in clear weather.
    /// </summary>
    public int HearingMask(long tick) =>
        Settings.RainPercent * RainMaskAtFull / 100 + WindMask(tick) + (Thunder(tick) ? ThunderMask : 0);

    /// <summary>
    /// Sight range left in this weather, in percent: rain halves it at a downpour, thick fog cuts it to
    /// a quarter, and both together multiply.
    /// </summary>
    public int SightRangePercent =>
        (100 - Settings.RainPercent / 2) * (100 - Settings.FogPercent * 3 / 4) / 100;

    /// <summary>
    /// How well ground of this cover carries a footfall, in percent: rock and decks on piles carry it,
    /// soft ground damps it, and rain turns soft ground outdoors to mud that damps it further.
    /// </summary>
    public int VibrationPercent(CoverInfo cover, bool outdoors) =>
        cover.SoftGround && outdoors
            ? cover.VibrationPercent * (100 - Settings.RainPercent / 2) / 100
            : cover.VibrationPercent;

    /// <summary>Where the wind blows to, as a vector ×1000 (diagonals 707); (0,0) in calm air.</summary>
    public (int X, int Y) Downwind
    {
        get
        {
            if (Settings.WindSpeed == 0)
                return (0, 0);
            var (dx, dy) = Settings.WindFrom.Opposite().Delta();
            int unit = dx != 0 && dy != 0 ? 707 : 1000;
            return (dx * unit, dy * unit);
        }
    }

    /// <summary>The strike in a storm window, if it has one.</summary>
    public LightningStrike? StrikeIn(long window)
    {
        if (!Settings.Storm || window < 0)
            return null;
        ulong roll = Roll("strike", window);
        if (roll % 100 >= StrikeChancePercent)
            return null;
        long tick = window * StrikeWindowTicks + (long)((roll >> 8) % StrikeWindowTicks);
        int delay = ThunderDelayMinTicks + (int)((roll >> 24) % (ThunderDelayMaxTicks - ThunderDelayMinTicks + 1));
        return new LightningStrike(tick, delay);
    }

    /// <summary>Strikes after <paramref name="fromExclusive"/> up to and including <paramref name="toInclusive"/>, in order.</summary>
    public IEnumerable<LightningStrike> StrikesBetween(long fromExclusive, long toInclusive)
    {
        if (!Settings.Storm || toInclusive <= fromExclusive)
            yield break;
        for (long w = Math.Max(0, fromExclusive) / StrikeWindowTicks; w <= toInclusive / StrikeWindowTicks; w++)
        {
            if (StrikeIn(w) is { } strike && strike.Tick > fromExclusive && strike.Tick <= toInclusive)
                yield return strike;
        }
    }

    /// <summary>The latest strike at or before a tick whose flash or thunder could still be going on.</summary>
    public LightningStrike? LastStrike(long tick)
    {
        if (!Settings.Storm || tick < 0)
            return null;
        long window = tick / StrikeWindowTicks;
        if (StrikeIn(window) is { } here && here.Tick <= tick)
            return here;
        // Flash plus thunder last at most 550 ticks, less than a window, so one window back is enough.
        return StrikeIn(window - 1);
    }

    /// <summary>Whether lightning is lighting everything up at this tick.</summary>
    public bool Flash(long tick) =>
        LastStrike(tick) is { } strike && tick - strike.Tick < FlashTicks;

    /// <summary>Whether thunder is rolling at this tick.</summary>
    public bool Thunder(long tick)
    {
        if (!Settings.Storm || tick < 0)
            return false;
        long window = tick / StrikeWindowTicks;
        for (long w = window; w >= window - 1; w--)
        {
            if (StrikeIn(w) is { } strike && tick >= strike.ThunderTick && tick < strike.ThunderTick + ThunderTicks)
                return true;
        }
        return false;
    }

    /// <summary>Fingerprint of the settings and the scent clock, for the simulation's state hash.</summary>
    internal void AddTo(StateHasher hasher) =>
        hasher.Add(Settings.RainPercent).Add((int)Settings.WindFrom).Add(Settings.WindSpeed)
            .Add(Settings.FogPercent).Add(Settings.Storm ? 1 : 0).Add(_washBase).Add(_washSince);

    /// <summary>A well-mixed 64-bit roll for one stream and one time window.</summary>
    private ulong Roll(string stream, long window)
    {
        ulong z = new StateHasher().Add(_seed).Add(stream).Add(window).Value;
        unchecked
        {
            z ^= z >> 30;
            z *= 0xbf58476d1ce4e5b9UL;
            z ^= z >> 27;
            z *= 0x94d049bb133111ebUL;
            return z ^ (z >> 31);
        }
    }
}
