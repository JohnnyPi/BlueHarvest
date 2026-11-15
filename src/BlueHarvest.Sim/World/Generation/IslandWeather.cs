using BlueHarvest.Sim.Core;

namespace BlueHarvest.Sim.World.Generation;

/// <summary>
/// The island's weather at any place and game minute, worked out on the spot with nothing stored:
/// <list type="bullet">
/// <item>Shower fronts: a field of "storminess" that drifts downwind on the trade wind and slowly
/// changes shape. Where it passes a place's threshold it rains there.</item>
/// <item>The threshold comes from the place's annual rain in the island's climate, so windward valleys
/// rain often and the leeward side rarely. Cloud builds a little before the rain does.</item>
/// <item>Kona storms: a rare, island-wide event lasting one to three days, about four times a year. The
/// trades die away, the wind comes from the other side, it rains everywhere, and the strongest bring thunder.</item>
/// <item>Fog: the cloud deck on the heights, thicker when it's stormy, lower in rain and Kona storms.</item>
/// <item>Wind: the trade wind's direction, a strength that wanders over days, more on the ridges and in showers.</item>
/// </list>
/// Everything after reading the climate is whole-number arithmetic on hashed lattice noise, so the
/// same island, run seed, place and minute always give the same weather on any machine.
/// The numbers are first guesses, chosen so a place with 600 mm of rain a year is wet about 2% of the
/// time, 2500 mm about 12% and 6000 mm about 27%, and a shower lasts 20–40 game minutes.
/// </summary>
public sealed class IslandWeather : IWeatherSource
{
    /// <summary>How far the shower field drifts downwind per game minute, in metres (3.6 km a game hour).</summary>
    public const int DriftPerMinute = 60;

    // the two layers of the shower field: lattice spacing in metres and minutes
    public const int FrontSize = 4000, FrontMinutes = 180;
    public const int ShowerSize = 1600, ShowerMinutes = 60;

    /// <summary>Rain goes from nothing to a downpour over this much storminess past the threshold.</summary>
    public const int RainSpan = 200;

    /// <summary>Cloud starts building this much storminess below the threshold.</summary>
    public const int CloudLead = 220;

    /// <summary>Trade showers this strong come with thunder.</summary>
    public const int ThunderRain = 90;

    // Kona storms: time is cut into blocks of days; each block may hold one storm, its start, length
    // and strength rolled from the seed. The same everywhere on the island.
    public const int KonaBlockMinutes = 10 * WorldClock.MinutesPerDay;
    public const int KonaChancePercent = 12;
    public const int KonaShortestMinutes = 24 * 60, KonaLongestMinutes = 72 * 60;
    public const int KonaWeakest = 400;

    /// <summary>Past this Kona strength (of 1000) the trades have died and the wind comes from the other side.</summary>
    public const int KonaTakeover = 200;

    /// <summary>Kona storms this strong (of 1000) bring thunder.</summary>
    public const int KonaThunder = 500;

    // the trade wind's strength wanders over days, 3–9 m/s
    public const int WindSize = 50_000, WindMinutes = WorldClock.MinutesPerDay;
    public const int TradeMin = 3, TradeRange = 6;

    private const int StreamFront = 1, StreamShower = 2, StreamKona = 3, StreamWind = 4;
    private const int KonaRollChance = 0, KonaRollLength = 1, KonaRollStart = 2, KonaRollPeak = 3;
    private const int Unit = 65536;

    /// <summary>Annual rain (mm) and the storminess (of 1000) past which it rains there; in between, straight lines.</summary>
    private static readonly (int RainMm, int Threshold)[] Thresholds =
    {
        (0, 800), (500, 775), (1000, 728), (2000, 682), (3000, 647), (5000, 600), (8000, 555),
    };

    private readonly ulong _seed;
    private readonly int _nx, _ny, _cell;
    private readonly int[] _rainMm, _heightM, _crestPercent;
    private readonly int _cloudBaseM;
    private readonly int _downX, _downY;

    /// <summary>
    /// Weather over a grid of climate cells. Cell (i, j) covers the square from (i · cell, j · cell)
    /// metres; values are taken at cell centres and blended between them.
    /// </summary>
    /// <param name="seed">Different for every run, so each run on the same island has its own weather.</param>
    /// <param name="rainMm">Mean annual rain per cell, row-major.</param>
    /// <param name="heightM">Ground height per cell (negative at sea).</param>
    /// <param name="crestPercent">How far each cell stands above its surroundings, 0–100: ridges get more wind.</param>
    /// <param name="cloudBaseM">Height of the trade-wind cloud deck.</param>
    /// <param name="tradeFromDegrees">Where the trade wind comes from, degrees clockwise from north (60 = from the ENE).</param>
    public IslandWeather(ulong seed, int nx, int ny, int cell, int[] rainMm, int[] heightM, int[] crestPercent, int cloudBaseM, int tradeFromDegrees)
    {
        if (nx < 2 || ny < 2) throw new ArgumentOutOfRangeException(nameof(nx), "The grid needs at least 2 × 2 cells.");
        if (cell <= 0) throw new ArgumentOutOfRangeException(nameof(cell), "Must be positive.");
        int n = nx * ny;
        if (rainMm.Length != n || heightM.Length != n || crestPercent.Length != n)
            throw new ArgumentException($"Every grid needs {n} cells.");
        _seed = seed;
        _nx = nx; _ny = ny; _cell = cell;
        _rainMm = rainMm; _heightM = heightM; _crestPercent = crestPercent;
        _cloudBaseM = cloudBaseM;
        int degrees = ((tradeFromDegrees % 360) + 360) % 360;
        TradeWind = (Direction)((degrees + 22) / 45 % 8);
        // the shower field drifts the way the wind blows: away from where it comes from (y points south)
        double rad = degrees * Math.PI / 180;
        _downX = (int)Math.Round(-Math.Sin(rad) * 1000);
        _downY = (int)Math.Round(Math.Cos(rad) * 1000);
    }

    /// <summary>The weather of a generated island for one run.</summary>
    public static IslandWeather For(IslandWorld world, ulong runSeed)
    {
        IslandShape s = world.Shape;
        var rain = new int[s.N];
        var height = new int[s.N];
        var crest = new int[s.N];
        for (int i = 0; i < s.N; i++)
        {
            rain[i] = (int)Math.Round(s.Rain[i]);
            height[i] = (int)Math.Round(s.Height[i]);
            crest[i] = (int)Math.Round(Math.Clamp(world.Climate.Crest[i], 0f, 1f) * 100);
        }
        ulong seed = new StateHasher().Add((ulong)world.Params.Seed).Add("weather").Add(runSeed).Value;
        return new IslandWeather(seed, s.Nx, s.Ny, (int)Math.Round(s.Cell), rain, height, crest,
            (int)Math.Round(world.Params.CloudBase), (int)Math.Round(world.Params.Wind));
    }

    /// <summary>Where the trade wind comes from.</summary>
    public Direction TradeWind { get; }

    /// <summary>The weather at a place (metres) at a game minute.</summary>
    public WeatherSettings At(long x, long y, long gameMinute)
    {
        int rainMm = Sample(_rainMm, x, y);
        int height = Sample(_heightM, x, y);
        int crest = Sample(_crestPercent, x, y);
        int storm = Storminess(x, y, gameMinute);
        int threshold = RainThreshold(rainMm);
        int rain = Math.Clamp((storm - threshold) * 100 / RainSpan, 0, 100);
        int cloud = Math.Clamp((storm - (threshold - CloudLead)) * 100 / CloudLead, 0, 100);
        int kona = Kona(gameMinute);

        // the trade wind, dying away as a Kona storm comes on; past the takeover the wind turns round
        Direction from = TradeWind;
        int speed = TradeMin + Noise(StreamWind, x, y, gameMinute, WindSize, WindMinutes) * TradeRange / (Unit - 1);
        if (kona > 0)
            speed = speed * Math.Max(0, KonaTakeover - kona) / KonaTakeover;
        if (kona >= KonaTakeover)
        {
            from = TradeWind.Opposite();
            speed = 4 + kona * 10 / 1000;
        }
        if (kona > 0)
        {
            rain = Math.Max(rain, kona / 10);
            cloud = Math.Max(cloud, Math.Min(100, 50 + kona / 4));
        }
        speed += crest * 5 / 100 + rain / 25;
        bool thunder = kona >= KonaThunder || rain >= ThunderRain;

        // the cloud deck sits on the heights; showers and Kona storms bring it lower
        int cloudBase = _cloudBaseM - rain - kona / 4;
        int deck = SmoothPercent(height - (cloudBase - 200), 450);
        int fog = deck * (30 + Math.Clamp(storm - 450, 0, 300) * 70 / 300) / 100;

        return new WeatherSettings(rain, from, speed, fog, thunder, cloud).Clamped();
    }

    /// <summary>
    /// The shower field at a place, 0–1000 (about 500 on average, above 800 rarely): two layers of noise,
    /// fronts and showers, carried downwind by <see cref="DriftPerMinute"/>.
    /// </summary>
    public int Storminess(long x, long y, long gameMinute)
    {
        long sx = x - FloorDiv(DriftPerMinute * gameMinute * _downX, 1000);
        long sy = y - FloorDiv(DriftPerMinute * gameMinute * _downY, 1000);
        long front = Noise(StreamFront, sx, sy, gameMinute, FrontSize, FrontMinutes);
        long shower = Noise(StreamShower, sx, sy, gameMinute, ShowerSize, ShowerMinutes);
        return (int)((2 * front + shower) * 1000 / (3 * (Unit - 1)));
    }

    /// <summary>
    /// How strong a Kona storm is at a game minute, 0 (none: most of the time) to 1000, the same all over
    /// the island. A storm builds over the first quarter of its length and eases off over the last.
    /// </summary>
    public int Kona(long gameMinute)
    {
        long block = FloorDiv(gameMinute, KonaBlockMinutes);
        if (Hash16(StreamKona, block, KonaRollChance, 0) * 100 / Unit >= KonaChancePercent)
            return 0;
        long length = KonaShortestMinutes + Hash16(StreamKona, block, KonaRollLength, 0) * (KonaLongestMinutes - KonaShortestMinutes) / (Unit - 1);
        long start = Hash16(StreamKona, block, KonaRollStart, 0) * (KonaBlockMinutes - length) / (Unit - 1);
        long peak = KonaWeakest + Hash16(StreamKona, block, KonaRollPeak, 0) * (1000 - KonaWeakest) / (Unit - 1);
        long into = gameMinute - block * KonaBlockMinutes - start;
        if (into < 0 || into >= length)
            return 0;
        long ramp = length / 4;
        long rise = Math.Min(Math.Min(into, length - 1 - into), ramp);
        return (int)(peak * rise / ramp);
    }

    /// <summary>The Kona storms that start in this stretch of game minutes, with their peak strength (of 1000): for tests and forecasts.</summary>
    public IEnumerable<(long StartMinute, long Minutes, int Peak)> KonaStorms(long fromMinute, long toMinute)
    {
        for (long block = FloorDiv(fromMinute, KonaBlockMinutes); block * KonaBlockMinutes <= toMinute; block++)
        {
            if (Hash16(StreamKona, block, KonaRollChance, 0) * 100 / Unit >= KonaChancePercent)
                continue;
            long length = KonaShortestMinutes + Hash16(StreamKona, block, KonaRollLength, 0) * (KonaLongestMinutes - KonaShortestMinutes) / (Unit - 1);
            long start = block * KonaBlockMinutes + Hash16(StreamKona, block, KonaRollStart, 0) * (KonaBlockMinutes - length) / (Unit - 1);
            long peak = KonaWeakest + Hash16(StreamKona, block, KonaRollPeak, 0) * (1000 - KonaWeakest) / (Unit - 1);
            if (start >= fromMinute && start <= toMinute)
                yield return (start, length, (int)peak);
        }
    }

    /// <summary>Mean annual rain at a place, mm, blended between cell centres.</summary>
    public int AnnualRainAt(long x, long y) => Sample(_rainMm, x, y);

    /// <summary>The storminess past which it rains at a place with this much rain a year.</summary>
    public static int RainThreshold(int annualRainMm)
    {
        if (annualRainMm <= Thresholds[0].RainMm) return Thresholds[0].Threshold;
        for (int k = 1; k < Thresholds.Length; k++)
        {
            (int r0, int t0) = Thresholds[k - 1];
            (int r1, int t1) = Thresholds[k];
            if (annualRainMm <= r1)
                return t0 - (t0 - t1) * (annualRainMm - r0) / (r1 - r0);
        }
        return Thresholds[^1].Threshold;
    }

    /// <summary>A grid value at a place, blended between the four nearest cell centres (fixed point, 1/65536).</summary>
    private int Sample(int[] grid, long x, long y)
    {
        Locate(x, _cell, _nx, out int ix, out long fx);
        Locate(y, _cell, _ny, out int iy, out long fy);
        int i = iy * _nx + ix;
        long top = Lerp(grid[i], grid[i + 1], fx);
        long bottom = Lerp(grid[i + _nx], grid[i + _nx + 1], fx);
        return (int)Lerp(top, bottom, fy);
    }

    /// <summary>The cell whose centre is at or before v, clamped to the grid, and how far on to the next (0–65536).</summary>
    private static void Locate(long v, int cell, int n, out int index, out long fraction)
    {
        long g = FloorDiv(v * Unit, cell) - Unit / 2;
        long i = FloorDiv(g, Unit);
        fraction = g - i * Unit;
        if (i < 0) { index = 0; fraction = 0; }
        else if (i > n - 2) { index = n - 2; fraction = Unit; }
        else index = (int)i;
    }

    /// <summary>
    /// Value noise in space and time, 0–65535: a hashed value at every lattice point (size metres
    /// apart, minutes apart), blended smoothly between them.
    /// </summary>
    private int Noise(int stream, long x, long y, long t, int size, int minutes)
    {
        long ix = FloorDiv(x, size), iy = FloorDiv(y, size), it = FloorDiv(t, minutes);
        long fx = Fade((x - ix * size) * Unit / size);
        long fy = Fade((y - iy * size) * Unit / size);
        long ft = Fade((t - it * minutes) * Unit / minutes);
        long Layer(long dt)
        {
            long a = Lerp(Hash16(stream, ix, iy, it + dt), Hash16(stream, ix + 1, iy, it + dt), fx);
            long b = Lerp(Hash16(stream, ix, iy + 1, it + dt), Hash16(stream, ix + 1, iy + 1, it + dt), fx);
            return Lerp(a, b, fy);
        }
        return (int)Lerp(Layer(0), Layer(1), ft);
    }

    /// <summary>A hashed value 0–65535 for one stream and lattice point.</summary>
    private long Hash16(int stream, long ix, long iy, long it)
    {
        ulong z = _seed;
        unchecked
        {
            z = Mix((z ^ (ulong)stream) + 0x9e3779b97f4a7c15UL);
            z = Mix((z ^ (ulong)ix) + 0x9e3779b97f4a7c15UL);
            z = Mix((z ^ (ulong)iy) + 0x9e3779b97f4a7c15UL);
            z = Mix((z ^ (ulong)it) + 0x9e3779b97f4a7c15UL);
        }
        return (long)(z & 0xFFFF);
    }

    /// <summary>The splitmix64 finaliser.</summary>
    private static ulong Mix(ulong z)
    {
        unchecked
        {
            z ^= z >> 30;
            z *= 0xbf58476d1ce4e5b9UL;
            z ^= z >> 27;
            z *= 0x94d049bb133111ebUL;
            return z ^ (z >> 31);
        }
    }

    /// <summary>Smoothstep in fixed point: 0 at 0, 65536 at 65536, flat at both ends.</summary>
    private static long Fade(long t) => ((t * t) >> 16) * (3 * Unit - 2 * t) >> 16;

    private static long Lerp(long a, long b, long f) => a + (((b - a) * f) >> 16);

    /// <summary>0 below 0, 100 at span and above, eased at both ends.</summary>
    private static int SmoothPercent(int v, int span)
    {
        int p = Math.Clamp(v * 100 / span, 0, 100);
        return p * p * (300 - 2 * p) / 10000;
    }

    private static long FloorDiv(long a, long b)
    {
        long q = a / b;
        return (a % b != 0 && (a < 0) != (b < 0)) ? q - 1 : q;
    }
}
