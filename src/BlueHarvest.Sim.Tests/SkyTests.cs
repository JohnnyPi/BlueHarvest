using BlueHarvest.Sim.Actors;
using BlueHarvest.Sim.World;
using Xunit;

namespace BlueHarvest.Sim.Tests;

/// <summary>World effects step 1: the clock (sixty action seconds are ten game minutes) and the sky's light.</summary>
public class WorldClockTests
{
    [Fact]
    public void AnHour_Is36000Ticks_AndADay864000()
    {
        WorldClock clock = WorldClock.StartingAt(8);
        Assert.Equal((8, 0, 1), (clock.Hour(0), clock.Minute(0), clock.Day(0)));
        Assert.Equal((8, 10), (clock.Hour(6000), clock.Minute(6000)));          // 60 action seconds = 10 game minutes
        Assert.Equal((8, 59), (clock.Hour(35_999), clock.Minute(35_999)));
        Assert.Equal((9, 0), (clock.Hour(36_000), clock.Minute(36_000)));
        Assert.Equal((8, 0, 2), (clock.Hour(864_000), clock.Minute(864_000), clock.Day(864_000)));
        Assert.Equal(36_000, WorldClock.TicksPerGameHour);
        Assert.Equal(864_000, WorldClock.TicksPerGameDay);
    }

    [Fact]
    public void MidnightStartsTheNextDay()
    {
        WorldClock clock = WorldClock.StartingAt(23, 30);
        Assert.Equal(1, clock.Day(17_999));
        Assert.Equal((2, 0, 0), (clock.Day(18_000), clock.Hour(18_000), clock.Minute(18_000)));
    }

    [Fact]
    public void Describe_ShowsTenMinuteSteps()
    {
        WorldClock clock = WorldClock.StartingAt(14, 37);
        Assert.Equal("Day 1, 14:30", clock.Describe(0));
        Assert.Equal("Day 1, 14:37", clock.Describe(0, stepMinutes: 1));
        Assert.Equal("Day 2, 06:00", WorldClock.StartingAt(6).Describe(WorldClock.TicksPerGameDay));
    }

    [Fact]
    public void TicksUntil_IsTheWaitToTheNextTimeItReadsThat()
    {
        WorldClock clock = WorldClock.StartingAt(8);
        Assert.Equal(10 * WorldClock.TicksPerGameHour, clock.TicksUntil(0, 18));
        Assert.Equal(22 * WorldClock.TicksPerGameHour, clock.TicksUntil(0, 6));           // tomorrow's dawn
        Assert.Equal(10 * WorldClock.TicksPerGameHour - 250, clock.TicksUntil(250, 18));
        Assert.Equal(WorldClock.TicksPerGameDay, clock.TicksUntil(0, 8));                 // it reads 08:00 now: the next one
        long wait = clock.TicksUntil(123_456, 6);
        Assert.Equal((6, 0), (clock.Hour(123_456 + wait), clock.Minute(123_456 + wait)));
        Assert.Equal((5, 59), (clock.Hour(123_456 + wait - 1), clock.Minute(123_456 + wait - 1)));
    }
}

public class SkyTests
{
    private const int Noon = 12 * 60, Midnight = 0;
    private const int NewMoon = 0, FullMoon = 29;

    [Fact]
    public void TheSun_IsUpFromSixToSix()
    {
        Assert.Equal(0, Sky.SunPercent(Midnight));
        Assert.Equal(0, Sky.SunPercent(Sky.FirstLight));
        Assert.Equal(Sky.HorizonPercent, Sky.SunPercent(Sky.Sunrise));
        Assert.Equal(100, Sky.SunPercent(Sky.FullDay));
        Assert.Equal(100, Sky.SunPercent(Noon));
        Assert.Equal(100, Sky.SunPercent(Sky.DuskStart));
        Assert.Equal(Sky.HorizonPercent, Sky.SunPercent(Sky.Sunset));
        Assert.Equal(0, Sky.SunPercent(Sky.FullDark));
        Assert.Equal(0, Sky.SunPercent(23 * 60 + 59));
    }

    [Fact]
    public void Light_OnlyRisesThroughTheMorning_AndOnlyFallsThroughTheEvening()
    {
        for (int m = 1; m <= Noon; m++)
            Assert.True(Sky.SunPercent(m) >= Sky.SunPercent(m - 1), $"the light fell at minute {m}");
        for (int m = Noon + 1; m < WorldClock.MinutesPerDay; m++)
            Assert.True(Sky.SunPercent(m) <= Sky.SunPercent(m - 1), $"the light rose at minute {m}");
    }

    [Fact]
    public void PartsOfTheDay()
    {
        Assert.Equal(PartOfDay.Night, Sky.PartOf(Midnight));
        Assert.Equal(PartOfDay.Dawn, Sky.PartOf(Sky.Sunrise));
        Assert.Equal(PartOfDay.Day, Sky.PartOf(Noon));
        Assert.Equal(PartOfDay.Dusk, Sky.PartOf(Sky.Sunset));
        Assert.Equal(PartOfDay.Night, Sky.PartOf(Sky.FullDark));
    }

    [Fact]
    public void TheMoon_GoesFromNewToFullAndBack_In59HalfDays()
    {
        Assert.Equal(0, Sky.MoonLitPercent(NewMoon));
        Assert.InRange(Sky.MoonLitPercent(FullMoon), 95, 100);
        Assert.InRange(Sky.MoonLitPercent(15), 45, 55);
        Assert.Equal("new moon", Sky.MoonName(NewMoon));
        Assert.Equal("full moon", Sky.MoonName(FullMoon));
        Assert.Equal("half moon", Sky.MoonName(15));
        // one step every half day, and round again after 29.5 days
        Assert.Equal(7, Sky.MoonPhase(0, startPhase: 7));
        Assert.Equal(7, Sky.MoonPhase(719, startPhase: 7));
        Assert.Equal(8, Sky.MoonPhase(720, startPhase: 7));
        Assert.Equal(9, Sky.MoonPhase(WorldClock.MinutesPerDay, startPhase: 7));
        Assert.Equal(7, Sky.MoonPhase(59L * 720, startPhase: 7));
    }

    [Fact]
    public void AFullMoonIsUpAllNight_ANewMoonAllDay()
    {
        Assert.True(Sky.MoonUp(FullMoon, Midnight));
        Assert.True(Sky.MoonUp(FullMoon, 3 * 60));
        Assert.False(Sky.MoonUp(FullMoon, Noon));
        Assert.False(Sky.MoonUp(NewMoon, Midnight));
        Assert.True(Sky.MoonUp(NewMoon, Noon));
    }

    [Fact]
    public void ClearSky_NoonIsFullLight_AndNightGoesByTheMoon()
    {
        WeatherSettings clear = WeatherSettings.Clear;
        Assert.Equal(100, Sky.AmbientPercent(Noon, NewMoon, clear));
        Assert.Equal(Sky.StarlightPercent, Sky.AmbientPercent(Midnight, NewMoon, clear));
        Assert.Equal(Sky.FullMoonPercent, Sky.AmbientPercent(Midnight, FullMoon, clear));
        int half = Sky.AmbientPercent(22 * 60, 15, clear); // a waxing half moon, still up before midnight
        Assert.InRange(half, Sky.StarlightPercent + 1, Sky.FullMoonPercent - 1);
    }

    [Fact]
    public void Weather_DimsTheSky_ButNeverToNothing()
    {
        var storm = new WeatherSettings(RainPercent: 70, Storm: true);
        var rain = new WeatherSettings(RainPercent: 70);
        var fog = new WeatherSettings(FogPercent: 100);
        Assert.Equal(40, Sky.AmbientPercent(Noon, NewMoon, storm));
        Assert.Equal(58, Sky.AmbientPercent(Noon, NewMoon, rain));
        Assert.Equal(50, Sky.AmbientPercent(Noon, NewMoon, fog));
        Assert.Equal(4, Sky.AmbientPercent(Midnight, FullMoon, storm));
        Assert.Equal(Sky.DarkestPercent, Sky.AmbientPercent(Midnight, NewMoon, storm));
    }
}

public class SimulationClockTests
{
    private static Simulation Field() => new(new WorldMap(12, 12), seed: 5);

    [Fact]
    public void WithoutAClock_LightIsTheLevelSet()
    {
        Simulation sim = Field();
        sim.AmbientLightPercent = 6;
        Assert.Null(sim.Clock);
        Assert.Equal(6, sim.AmbientLightPercent);
        Assert.Equal(6, sim.Light.LevelAt(new TilePos(3, 3)));
        sim.RunUntil(WorldClock.TicksPerGameDay / 2);
        Assert.Equal(6, sim.AmbientLightPercent);
    }

    [Fact]
    public void WithAClock_LightFollowsTheSun()
    {
        Simulation sim = Field();
        sim.AddActor("A", 'a', new TilePos(6, 6), Species.Human, new HoldStillController());
        sim.SetTimeOfDay(12);
        var p = new TilePos(3, 3);
        Assert.Equal(100, sim.AmbientLightPercent);
        Assert.Equal(100, sim.Light.LevelAt(p));

        sim.RunUntil(sim.CurrentTick + 6 * WorldClock.TicksPerGameHour); // 18:00, sunset
        Assert.Equal(18, sim.Clock!.Hour(sim.CurrentTick));
        Assert.Equal(Sky.HorizonPercent, sim.AmbientLightPercent);
        Assert.Equal(Sky.HorizonPercent, sim.Light.LevelAt(p));

        sim.RunUntil(sim.CurrentTick + WorldClock.TicksPerGameHour); // 19:00, night
        Assert.InRange(sim.AmbientLightPercent, Sky.StarlightPercent, Sky.FullMoonPercent);
        Assert.Equal(sim.AmbientLightPercent, sim.Light.LevelAt(p));

        sim.RunUntil(sim.CurrentTick + 13 * WorldClock.TicksPerGameHour); // 08:00 on day 2
        Assert.Equal(2, sim.Clock.Day(sim.CurrentTick));
        Assert.Equal(100, sim.Light.LevelAt(p));
    }

    [Fact]
    public void AStorm_DarkensTheDay()
    {
        Simulation sim = Field();
        sim.SetTimeOfDay(12);
        sim.SetWeather(new WeatherSettings(RainPercent: 70, Storm: true));
        Assert.Equal(40, sim.AmbientLightPercent);
    }

    [Fact]
    public void SettingTheTime_OnlyGoesForward()
    {
        Simulation sim = Field();
        sim.SetTimeOfDay(8);
        Assert.Equal("Day 1, 08:00", sim.Clock!.Describe(sim.CurrentTick));
        sim.SetTimeOfDay(17, 30);
        Assert.Equal("Day 1, 17:30", sim.Clock!.Describe(sim.CurrentTick));
        sim.SetTimeOfDay(0);
        Assert.Equal("Day 2, 00:00", sim.Clock!.Describe(sim.CurrentTick));
        sim.SetTimeOfDay(0); // already midnight: nothing moves
        Assert.Equal("Day 2, 00:00", sim.Clock!.Describe(sim.CurrentTick));
    }

    [Fact]
    public void TheClockStartedLate_ReadsTheTimeAskedFor()
    {
        Simulation sim = Field();
        sim.RunUntil(123_456);
        sim.SetTimeOfDay(6);
        Assert.Equal((6, 0), (sim.Clock!.Hour(sim.CurrentTick), sim.Clock.Minute(sim.CurrentTick)));
    }

    [Fact]
    public void TheMoon_DependsOnTheSeed_AndMovesOnWithTheDays()
    {
        var phases = new HashSet<int>();
        for (ulong seed = 1; seed <= 12; seed++)
        {
            var sim = new Simulation(new WorldMap(4, 4), seed);
            sim.SetTimeOfDay(0);
            phases.Add(sim.MoonPhase);
            Assert.InRange(sim.MoonPhase, 0, Sky.MoonCycle - 1);
        }
        Assert.True(phases.Count > 3, "every seed has the same moon");

        Simulation later = Field();
        later.SetTimeOfDay(0);
        int first = later.MoonPhase;
        later.RunUntil(WorldClock.TicksPerGameDay);
        Assert.Equal((first + 2) % Sky.MoonCycle, later.MoonPhase);
    }
}
