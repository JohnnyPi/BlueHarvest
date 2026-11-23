using BlueHarvest.Sim.World;
using BlueHarvest.Sim.World.Generation;
using Xunit;

namespace BlueHarvest.Sim.Tests;

/// <summary>
/// World effects step 2: weather that differs by place. A 4 × 4 km grid with a dry west half
/// (500 mm a year), a wet east half (6000 mm) and a 1200 m summit in the north-east corner, the
/// trade wind from the east. Bounds were picked from an exact Python port of IslandWeather.
/// </summary>
public class IslandWeatherTests
{
    private static readonly (long X, long Y) Wet = (3500, 3500), Dry = (500, 3500), Summit = (3500, 500);
    private const int Day = WorldClock.MinutesPerDay;

    /// <summary>Seed 1's first Kona storm starts at minute 5643 (day 4, 22:03), lasts 2919 minutes and peaks at 964.</summary>
    private const long FirstKonaStart = 5643, FirstKonaMinutes = 2919;

    private static IslandWeather Grid(ulong seed = 1)
    {
        var rain = new int[16];
        var height = new int[16];
        var crest = new int[16];
        for (int i = 0; i < 16; i++)
        {
            rain[i] = i % 4 < 2 ? 500 : 6000;
            height[i] = 100;
        }
        height[3] = 1200; // cell (3, 0)
        crest[3] = 100;
        return new IslandWeather(seed, 4, 4, 1000, rain, height, crest, cloudBaseM: 650, tradeFromDegrees: 90);
    }

    [Fact]
    public void TheSamePlaceAndMinute_AlwaysGiveTheSameWeather()
    {
        IslandWeather a = Grid(), b = Grid(), other = Grid(seed: 2);
        bool differs = false;
        for (long m = 0; m < 3 * Day; m += 37)
        {
            Assert.Equal(a.At(Wet.X, Wet.Y, m), a.At(Wet.X, Wet.Y, m));
            Assert.Equal(a.At(Wet.X, Wet.Y, m), b.At(Wet.X, Wet.Y, m));
            differs |= a.At(Wet.X, Wet.Y, m) != other.At(Wet.X, Wet.Y, m);
        }
        Assert.True(differs, "another run seed gives the same weather");
    }

    [Theory]
    [InlineData(1UL)]
    [InlineData(2UL)]
    [InlineData(3UL)]
    public void TheWetSide_RainsFarMoreOften_AndOftenAlone(ulong seed)
    {
        // port: seed 1 wet 35.6% / dry 9.3% (a Kona storm in the month), seed 2 28.7 / 3.8, seed 3 31.1 / 8.7
        IslandWeather w = Grid(seed);
        int wet = 0, dry = 0, wetAlone = 0, samples = 0;
        for (long m = 0; m < 30 * Day; m += 10)
        {
            bool wetRains = w.At(Wet.X, Wet.Y, m).RainPercent > 0, dryRains = w.At(Dry.X, Dry.Y, m).RainPercent > 0;
            samples++;
            if (wetRains) wet++;
            if (dryRains) dry++;
            if (wetRains && !dryRains) wetAlone++;
        }
        Assert.InRange(wet * 100 / samples, 20, 45);
        Assert.InRange(dry * 100 / samples, 1, 12);
        Assert.True(wet >= 3 * dry, $"wet {wet}, dry {dry}");
        // the same moment, different weather: raining on the wet side while the dry side stays dry
        Assert.True(wetAlone >= 500, $"only {wetAlone} of {samples} moments raining on one side only");
    }

    [Fact]
    public void Showers_ComeAndGo_AndChangeGradually()
    {
        // port: 64 showers in ten days, median 56 minutes; rain never moves more than 10 points in a minute
        IslandWeather w = Grid();
        var spells = new List<int>();
        int current = 0, previous = -1, biggestStep = 0;
        for (long m = 0; m < 10 * Day; m++)
        {
            int rain = w.At(Wet.X, Wet.Y, m).RainPercent;
            if (previous >= 0) biggestStep = Math.Max(biggestStep, Math.Abs(rain - previous));
            previous = rain;
            if (rain > 0) current++;
            else if (current > 0) { spells.Add(current); current = 0; }
        }
        spells.Sort();
        Assert.True(spells.Count >= 30, $"only {spells.Count} showers in ten days");
        Assert.InRange(spells[spells.Count / 2], 15, 120);
        Assert.True(biggestStep <= 15, $"rain jumped {biggestStep} points in a minute");
    }

    [Fact]
    public void TheSummit_IsInTheCloud_AndTheLowGroundNeverIs()
    {
        // port: summit fog averages 49% and never drops below 30; the low ground is never fogged
        IslandWeather w = Grid();
        int total = 0, samples = 0;
        for (long m = 0; m < 10 * Day; m += 10)
        {
            int fog = w.At(Summit.X, Summit.Y, m).FogPercent;
            Assert.True(fog >= 25, $"the summit was clear ({fog}%) at minute {m}");
            total += fog;
            samples++;
            Assert.Equal(0, w.At(Wet.X, Wet.Y, m).FogPercent);
        }
        Assert.InRange(total / samples, 35, 65);
    }

    [Fact]
    public void TheRidge_IsWindier_AndTheTradesBlowFromTheEast()
    {
        IslandWeather w = Grid();
        Assert.Equal(Direction.East, w.TradeWind);
        long ridge = 0, low = 0;
        for (long m = 0; m < 10 * Day; m += 10)
        {
            WeatherSettings top = w.At(Summit.X, Summit.Y, m), bottom = w.At(Wet.X, Wet.Y, m);
            ridge += top.WindSpeed;
            low += bottom.WindSpeed;
            Assert.InRange(bottom.WindSpeed, 0, WeatherSettings.MaxWindSpeed);
            if (w.Kona(m) == 0)
                Assert.Equal(Direction.East, bottom.WindFrom);
        }
        Assert.True(ridge > low * 3 / 2, $"ridge {ridge}, low ground {low}");
    }

    [Fact]
    public void AKonaStorm_TurnsTheWind_AndRainsEverywhere()
    {
        IslandWeather w = Grid();
        (long start, long minutes, int peak) = w.KonaStorms(0, 30 * Day).First();
        Assert.Equal((FirstKonaStart, FirstKonaMinutes, 964), (start, minutes, peak));
        Assert.Equal(0, w.Kona(start - 1));
        Assert.Equal(0, w.Kona(start + minutes));
        long middle = start + minutes / 2;
        Assert.Equal(964, w.Kona(middle));
        foreach ((long x, long y) in new[] { Wet, Dry })
        {
            WeatherSettings s = w.At(x, y, middle);
            Assert.Equal(Direction.West, s.WindFrom);
            Assert.True(s.RainPercent >= 90, $"only {s.RainPercent}% rain at ({x}, {y})");
            Assert.True(s.Storm);
            Assert.Equal(100, s.CloudPercent);
        }
    }

    [Fact]
    public void KonaStorms_AreRare_AndLastOneToThreeDays()
    {
        IslandWeather w = Grid();
        var storms = w.KonaStorms(0, 5 * 365 * Day).ToList();
        Assert.Equal(24, storms.Count); // port: 24 in five years for seed 1 (3 to 6 a year over 40 seeds)
        foreach ((long start, long minutes, int peak) in storms)
        {
            Assert.InRange(minutes, IslandWeather.KonaShortestMinutes, IslandWeather.KonaLongestMinutes);
            Assert.InRange(peak, IslandWeather.KonaWeakest, 1000);
            Assert.Equal(0, w.Kona(start + minutes));
            Assert.True(w.Kona(start + minutes / 2) > 0);
        }
    }

    [Fact]
    public void RainThreshold_FallsAsTheAnnualRainRises()
    {
        Assert.Equal(800, IslandWeather.RainThreshold(0));
        Assert.Equal(775, IslandWeather.RainThreshold(500));
        Assert.Equal(682, IslandWeather.RainThreshold(2000));
        Assert.Equal(555, IslandWeather.RainThreshold(20_000));
        for (int mm = 100; mm <= 9000; mm += 100)
            Assert.True(IslandWeather.RainThreshold(mm) <= IslandWeather.RainThreshold(mm - 100));
    }

    [Fact]
    public void TheGeneratedIsland_IsWetterWindwardThanLeeward()
    {
        IslandWorld world = TestIslands.World("Default");
        IslandShape s = world.Shape;
        IslandWeather w = IslandWeather.For(world, runSeed: 1);
        // the wettest and the driest land cells above the shore
        int wettest = -1, driest = -1;
        for (int i = 0; i < s.N; i++)
        {
            if (s.Land[i] == 0 || s.Height[i] < 20) continue;
            if (wettest < 0 || s.Rain[i] > s.Rain[wettest]) wettest = i;
            if (driest < 0 || s.Rain[i] < s.Rain[driest]) driest = i;
        }
        (long X, long Y) Centre(int i) => ((long)((i % s.Nx + 0.5) * s.Cell), (long)((i / s.Nx + 0.5) * s.Cell));
        var (wx, wy) = Centre(wettest);
        var (dx, dy) = Centre(driest);
        // trade showers only: a Kona storm rains on both sides alike, by design
        int wet = 0, dry = 0;
        for (long m = 0; m < 20 * Day; m += 15)
        {
            if (w.Kona(m) > 0) continue;
            if (w.At(wx, wy, m).RainPercent > 0) wet++;
            if (w.At(dx, dy, m).RainPercent > 0) dry++;
        }
        Assert.True(wet > 2 * dry, $"wettest cell ({s.Rain[wettest]:F0} mm) rained {wet} times, driest ({s.Rain[driest]:F0} mm) {dry}");
        Assert.Equal(w.At(wx, wy, 12_345), IslandWeather.For(world, runSeed: 1).At(wx, wy, 12_345));
    }
}

/// <summary>The simulation following a weather source: when it looks the weather up, where, and what stops it.</summary>
public class FollowWeatherTests
{
    /// <summary>Rain is the minute's last two digits; fog is the place's x, modulo 100 (so moving the map shows).</summary>
    private sealed class Ticking : IWeatherSource
    {
        public WeatherSettings At(long x, long y, long gameMinute) =>
            new((int)(gameMinute % 100), Direction.East, 5, (int)(x % 100));
    }

    private static Simulation Field() => new(TestContent.NewSession(), new WorldMap(20, 20), seed: 3);

    [Fact]
    public void TheWeather_IsLookedUpEveryGameMinute()
    {
        Simulation sim = Field();
        sim.FollowWeather(new Ticking(), 1000, 0);
        Assert.True(sim.FollowingWeatherSource);
        Assert.Equal(0, sim.Weather.Settings.RainPercent);
        sim.RunUntil(Simulation.WeatherStepTicks - 1);
        Assert.Equal(0, sim.Weather.Settings.RainPercent);
        sim.RunUntil(Simulation.WeatherStepTicks);
        Assert.Equal(1, sim.Weather.Settings.RainPercent);
        sim.RunUntil(10 * Simulation.WeatherStepTicks + 300);
        Assert.Equal(10, sim.Weather.Settings.RainPercent);
    }

    [Fact]
    public void TheWeather_IsTakenFromTheMiddleOfTheMap_AndMovesWithIt()
    {
        Simulation sim = Field();
        sim.FollowWeather(new Ticking(), 1000, 500);
        Assert.Equal((1010L, 510L), sim.WeatherPlace);
        Assert.Equal(10, sim.Weather.Settings.FogPercent);
        sim.Map.Scroll(15, 0);
        sim.ShiftWindow(15, 0);
        Assert.Equal((1025L, 510L), sim.WeatherPlace);
        Assert.Equal(10, sim.Weather.Settings.FogPercent); // not until the next game minute
        sim.RunUntil(Simulation.WeatherStepTicks);
        Assert.Equal(25, sim.Weather.Settings.FogPercent);
    }

    [Fact]
    public void WithAClock_TheMinuteIsTheClocks()
    {
        Simulation sim = Field();
        sim.SetTimeOfDay(8);
        sim.FollowWeather(new Ticking(), 0, 0);
        Assert.Equal(480 % 100, sim.Weather.Settings.RainPercent);
        Assert.Equal(480, sim.GameMinute(sim.CurrentTick));
        sim.SetTimeOfDay(9, 15); // a jump: the weather of the new time at once
        Assert.Equal(555 % 100, sim.Weather.Settings.RainPercent);
        Assert.Equal(10, sim.Forecast(10)!.RainPercent - sim.Weather.Settings.RainPercent);
    }

    [Fact]
    public void SettingTheWeather_StopsFollowing_UntilResumed()
    {
        Simulation sim = Field();
        sim.FollowWeather(new Ticking(), 0, 0);
        sim.SetWeather(WeatherSettings.TestbedStorm);
        Assert.False(sim.FollowingWeatherSource);
        sim.RunUntil(5 * Simulation.WeatherStepTicks);
        Assert.Equal(WeatherSettings.TestbedStorm, sim.Weather.Settings);
        sim.ResumeWeatherSource();
        Assert.True(sim.FollowingWeatherSource);
        Assert.Equal(5, sim.Weather.Settings.RainPercent);
    }

    [Fact]
    public void TheScentClock_RunsOn_ThroughEveryChange()
    {
        Simulation sim = Field();
        sim.FollowWeather(new Ticking(), 0, 0);
        long previous = sim.Weather.Wash(0);
        for (long t = 100; t <= 30 * Simulation.WeatherStepTicks; t += 100)
        {
            sim.RunUntil(t);
            long now = sim.Weather.Wash(t);
            Assert.True(now > previous, $"the scent clock stood still or ran back at tick {t}");
            previous = now;
        }
    }

    [Fact]
    public void TwoRuns_FollowingTheSameIsland_EndTheSame()
    {
        IslandWorld world = TestIslands.World("Default");
        ulong Run()
        {
            var sim = new Simulation(TestContent.NewSession(), new WorldMap(16, 16), seed: 9);
            sim.SetTimeOfDay(8);
            sim.FollowWeather(IslandWeather.For(world, runSeed: 9), (long)(world.WidthM / 2), (long)(world.HeightM / 2));
            sim.RunUntil(2 * WorldClock.TicksPerGameHour);
            return sim.ComputeStateHash();
        }
        Assert.Equal(Run(), Run());
    }

    [Fact]
    public void WithoutASource_NothingChanges()
    {
        Simulation sim = Field();
        sim.SetWeather(WeatherSettings.TestbedStorm);
        sim.ResumeWeatherSource(); // nothing to resume
        sim.RunUntil(20 * Simulation.WeatherStepTicks);
        Assert.Null(sim.WeatherSource);
        Assert.Null(sim.Forecast(60));
        Assert.Equal(WeatherSettings.TestbedStorm, sim.Weather.Settings);
    }
}
