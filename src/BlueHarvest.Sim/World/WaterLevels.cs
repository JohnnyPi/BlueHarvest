using BlueHarvest.Sim.Core;

namespace BlueHarvest.Sim.World;

/// <summary>
/// A river as the water rules see it: a few places along its upper course where the rain that feeds
/// it is measured, and how far it rises (dm) when that rain has been as heavy as it gets.
/// </summary>
/// <param name="Catchment">Places along the river, in island metres, where its rain is measured.</param>
public sealed record RiverGauge(int Id, int RiseDmAtFull, IReadOnlyList<(long X, long Y)> Catchment);

/// <summary>
/// The sea's tide and swell and the rivers' rise, as whole numbers from the game minute (and, for
/// the rivers, from the weather): nothing is stored but a cache, so the same run gives the same water.
/// </summary>
public sealed class WaterLevels
{
    /// <summary>The main tide's period, game minutes: 12 h 25 min, so high tide comes about 50 minutes later each day.</summary>
    public const int TidePeriodMinutes = 745;

    /// <summary>The smaller daily tide that makes one high water of the two higher than the other.</summary>
    public const int DailyTidePeriodMinutes = 2 * TidePeriodMinutes;

    /// <summary>The main tide's half-range at neaps and at springs (new and full moon), dm, and the daily tide's.</summary>
    public const int NeapAmplitudeDm = 3, SpringAmplitudeDm = 5, DailyAmplitudeDm = 1;

    /// <summary>Water deeper than this can't be waded (1.2 m, as the island's tiles are made).</summary>
    public const int WadeDepthDm = 12;

    /// <summary>A river's rise answers to the rain of this many game hours before now, the latest counting most.</summary>
    public const int CatchmentHours = 12;

    /// <summary>How the swell grows and dies away: a new strength every this many game minutes, eased between.</summary>
    public const int SwellMinutes = WorldClock.MinutesPerDay;

    private readonly ulong _seed;
    private readonly int _tideOffset, _dailyOffset;
    private readonly Dictionary<int, RiverGauge> _rivers;
    private readonly Dictionary<(int River, long Hour), int> _wetness = new();

    /// <param name="seed">The run's seed: when the tides fall and how the swell runs.</param>
    /// <param name="swellFrom">Where the ocean swell comes from.</param>
    public WaterLevels(ulong seed, Direction swellFrom, IEnumerable<RiverGauge> rivers)
    {
        _seed = seed;
        SwellFrom = swellFrom;
        _rivers = rivers.ToDictionary(r => r.Id);
        _tideOffset = (int)(new StateHasher().Add(seed).Add("tide").Value % TidePeriodMinutes);
        _dailyOffset = (int)(new StateHasher().Add(seed).Add("tide-daily").Value % DailyTidePeriodMinutes);
    }

    public Direction SwellFrom { get; }
    public IReadOnlyCollection<RiverGauge> Rivers => _rivers.Values;

    /// <summary>The sea's height against its mean, in tenths of a decimetre (centimetres): finer than the tiles need, for telling the turn.</summary>
    public int SeaLevelCm(long minute, int moonPhase)
    {
        int lit = Sky.MoonLitPercent(moonPhase);
        int spring = Math.Abs(2 * lit - 100); // 100 at new and full moon, 0 at the quarters
        int amplitudeCm = NeapAmplitudeDm * 10 + (SpringAmplitudeDm - NeapAmplitudeDm) * 10 * spring / 100;
        long main = SinMicro(MilliDegrees(minute + _tideOffset, TidePeriodMinutes)) * amplitudeCm / 1_000_000;
        long daily = SinMicro(MilliDegrees(minute + _dailyOffset, DailyTidePeriodMinutes)) * DailyAmplitudeDm * 10 / 1_000_000;
        return (int)(main + daily);
    }

    /// <summary>
    /// Which way the sea is moving and how fast, in arbitrary units: the slope of the two tides added
    /// (the daily one has half the speed). Positive while it comes in.
    /// </summary>
    private long TideRate(long minute, int moonPhase)
    {
        int lit = Sky.MoonLitPercent(moonPhase);
        int spring = Math.Abs(2 * lit - 100);
        int amplitudeCm = NeapAmplitudeDm * 10 + (SpringAmplitudeDm - NeapAmplitudeDm) * 10 * spring / 100;
        long main = SinMicro(MilliDegrees(minute + _tideOffset, TidePeriodMinutes) + 90_000) * amplitudeCm * 2;
        long daily = SinMicro(MilliDegrees(minute + _dailyOffset, DailyTidePeriodMinutes) + 90_000) * DailyAmplitudeDm * 10;
        return main + daily;
    }

    /// <summary>The sea's height against its mean, dm (rounded to the nearest).</summary>
    public int SeaLevelDm(long minute, int moonPhase) => RoundDiv(SeaLevelCm(minute, moonPhase), 10);

    /// <summary>Whether the tide is rising at this minute.</summary>
    public bool TideRising(long minute, int moonPhase) => TideRate(minute, moonPhase) > 0;

    /// <summary>High water (1), low water (−1), or neither (0): the minute the tide turns.</summary>
    public int TideTurn(long minute, int moonPhase)
    {
        // the minute the sea's slope changes sign, with the moon held: it moves on in half-day steps
        long before = TideRate(minute - 1, moonPhase), now = TideRate(minute, moonPhase);
        if (before > 0 && now <= 0) return 1;
        if (before < 0 && now >= 0) return -1;
        return 0;
    }

    /// <summary>How big the ocean swell is running, 20–100: it builds and dies away over days.</summary>
    public int SwellPercent(long minute)
    {
        long step = Math.Max(0, minute) / SwellMinutes;
        int into = (int)(Math.Max(0, minute) % SwellMinutes);
        int a = SwellAt(step), b = SwellAt(step + 1);
        return a + (b - a) * into / SwellMinutes;
    }

    private int SwellAt(long step) => 20 + (int)(new StateHasher().Add(_seed).Add("swell").Add(step).Value % 81);

    /// <summary>
    /// How much rain a river has had over its catchment, 0–100: rain percent over the last
    /// <see cref="CatchmentHours"/> game hours (the latest hour weighs most), at its gauge points.
    /// </summary>
    public int Wetness(int river, long minute, IWeatherSource weather)
    {
        if (!_rivers.TryGetValue(river, out RiverGauge? gauge) || gauge.Catchment.Count == 0)
            return 0;
        long hour = Math.Max(0, minute) / 60;
        if (_wetness.TryGetValue((river, hour), out int known))
            return known;
        long sum = 0, weights = 0;
        for (int back = 1; back <= CatchmentHours; back++)
        {
            long h = hour - back;
            int weight = CatchmentHours + 1 - back;
            int rain = 0;
            if (h >= 0) // before the run began counts as dry
                foreach ((long x, long y) in gauge.Catchment)
                    rain += weather.At(x, y, h * 60 + 30).RainPercent;
            sum += (long)weight * rain / gauge.Catchment.Count;
            weights += weight;
        }
        int wetness = (int)Math.Clamp(sum / weights, 0, 100);
        if (_wetness.Count > 8192)
            _wetness.Clear();
        _wetness[(river, hour)] = wetness;
        return wetness;
    }

    /// <summary>How far a river stands above its usual surface now, dm.</summary>
    public int RiverRiseDm(int river, long minute, IWeatherSource? weather)
    {
        if (weather is null || !_rivers.TryGetValue(river, out RiverGauge? gauge))
            return 0;
        return gauge.RiseDmAtFull * Wetness(river, minute, weather) / 100;
    }

    /// <summary>How fast a river runs against usual, in percent: up to twice as fast in a flood.</summary>
    public int RiverSpeedPercent(int river, long minute, IWeatherSource? weather) =>
        weather is null ? 100 : 100 + Wetness(river, minute, weather);

    /// <summary>
    /// How big the surf is on a coast looking out toward <paramref name="facing"/>, 0–100: the wind's
    /// own waves if it blows onshore, or the ocean swell if the coast faces it, whichever is bigger.
    /// </summary>
    public int SurfPercent(Direction facing, WeatherSettings weather, long minute)
    {
        int wind = weather.WindSpeed * 5 * Math.Max(0, CosPermille(facing, weather.WindFrom)) / 1000;
        int swell = SwellPercent(minute) * 65 / 100 * Math.Max(0, CosPermille(facing, SwellFrom)) / 1000;
        return Math.Clamp(Math.Max(10, Math.Max(wind, swell)), 0, 100);
    }

    /// <summary>The cosine of the angle between two of the eight directions, in thousandths.</summary>
    public static int CosPermille(Direction a, Direction b)
    {
        int d = Math.Abs((int)a - (int)b) % 8;
        if (d > 4) d = 8 - d;
        return d switch { 0 => 1000, 1 => 707, 2 => 0, 3 => -707, _ => -1000 };
    }

    /// <summary>Sine of a whole number of degrees in thousandths (see <see cref="SinMicro"/>).</summary>
    public static int SinPermille(int degrees) => (int)(SinMicro(degrees * 1000L) / 1000);

    /// <summary>
    /// Sine of an angle in thousandths of a degree, in millionths, by Bhaskara's rule (within about
    /// 0.002): whole-number arithmetic, so it comes out the same on every machine.
    /// </summary>
    public static long SinMicro(long milliDegrees)
    {
        long x = ((milliDegrees % 360_000) + 360_000) % 360_000;
        long sign = 1;
        if (x >= 180_000)
        {
            x -= 180_000;
            sign = -1;
        }
        long p = x * (180_000 - x);
        return sign * (4_000_000 * (p / 1000) / (40_500_000 - p / 1000));
    }

    private static long MilliDegrees(long minute, int period) => ((minute % period) + period) % period * 360_000 / period;

    private static int RoundDiv(int value, int by) => value >= 0 ? (value + by / 2) / by : -((-value + by / 2) / by);
}
