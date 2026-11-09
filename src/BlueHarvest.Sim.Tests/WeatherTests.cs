using BlueHarvest.Sim.Actors;
using BlueHarvest.Sim.Ai;
using BlueHarvest.Sim.Core;
using BlueHarvest.Sim.Demo;
using BlueHarvest.Sim.Injuries;
using BlueHarvest.Sim.Lighting;
using BlueHarvest.Sim.Perception;
using BlueHarvest.Sim.Scenarios;
using BlueHarvest.Sim.World;
using Xunit;

namespace BlueHarvest.Sim.Tests;

/// <summary>Rain, wind, fog and storms, and what they do to sight and hearing.</summary>
public class WeatherTests
{
    [Fact]
    public void ClearWeather_LeavesTheSensesAlone()
    {
        var sim = new Simulation(new WorldMap(20, 20), seed: 1);
        var rex = sim.AddActor("Rex", 'T', new TilePos(10, 10), Species.Rex, new HoldStillController(), Direction.North);

        Assert.Equal(WeatherSettings.Clear, sim.Weather.Settings);
        Assert.Equal(0, sim.Weather.HearingMask(12_345));
        Assert.Equal(100, sim.Weather.SightRangePercent);
        Assert.Equal(Species.Rex.Senses.SightRangeTiles, sim.SensesOf(rex).SightRangeTiles);
        Assert.Empty(sim.Weather.StrikesBetween(-1, 100_000));
        Assert.False(sim.Weather.Thunder(5_000));
    }

    [Fact]
    public void Rain_MasksSounds()
    {
        var sim = new Simulation(new WorldMap(12, 1), seed: 1);
        var listener = sim.AddActor("Listener", '@', new TilePos(0, 0), Species.Human, new PlayerController());
        int dry = PerceptionSystem.HearingLevel(sim, listener, new TilePos(10, 0), 18);

        sim.SetWeather(new WeatherSettings(RainPercent: 50));

        Assert.Equal(8, dry);
        Assert.Equal(dry - 10, PerceptionSystem.HearingLevel(sim, listener, new TilePos(10, 0), 18)); // 20 at full rain
    }

    [Fact]
    public void Gusts_MaskQuietSounds_TheSameWayEveryRun()
    {
        IReadOnlyList<int> Masks(ulong seed, int windSpeed)
        {
            var sim = new Simulation(new WorldMap(4, 4), seed);
            sim.SetWeather(new WeatherSettings(WindSpeed: windSpeed));
            return Enumerable.Range(0, 50).Select(i => sim.Weather.HearingMask(i * Weather.GustWindowTicks)).ToList();
        }

        var windy = Masks(7, 10);
        Assert.All(windy, m => Assert.InRange(m, 10 * 50 / 200, 10 * 150 / 200)); // steady 2.5 up to 7.5 in a gust
        Assert.True(windy.Distinct().Count() > 1, "gusts never changed");
        Assert.Equal(windy, Masks(7, 10));
        Assert.All(Masks(7, 0), m => Assert.Equal(0, m));
    }

    [Fact]
    public void RainAndFog_ShortenSight()
    {
        var sim = new Simulation(new WorldMap(20, 20), seed: 1);
        var rex = sim.AddActor("Rex", 'T', new TilePos(10, 10), Species.Rex, new HoldStillController(), Direction.North);
        var human = sim.AddActor("Pete", 'h', new TilePos(15, 15), Species.Human, new HoldStillController());

        sim.SetWeather(new WeatherSettings(RainPercent: 100));
        Assert.Equal(20, sim.SensesOf(rex).SightRangeTiles); // halved in a downpour

        sim.SetWeather(new WeatherSettings(FogPercent: 100));
        Assert.Equal(10, sim.SensesOf(rex).SightRangeTiles); // a quarter in thick fog

        sim.SetWeather(WeatherSettings.TestbedStorm);
        Assert.Equal(19, sim.SensesOf(human).SightRangeTiles); // 30 at 65% in heavy rain (70%)
    }

    [Fact]
    public void Lightning_LightsEverythingForAMoment()
    {
        var sim = new Simulation(new WorldMap(20, 20), seed: 3) { AmbientLightPercent = 6 };
        sim.SetWeather(new WeatherSettings(Storm: true));
        var struck = new List<LightningStrike>();
        sim.LightningStruck += struck.Add;
        var strike = sim.Weather.StrikesBetween(-1, 100_000).First();
        var tile = new TilePos(10, 10);

        sim.RunUntil(strike.Tick);
        Assert.Equal(Weather.FlashAmbientPercent, sim.Light.LevelAt(tile));
        Assert.Equal(strike, Assert.Single(struck));

        sim.RunUntil(strike.Tick + Weather.FlashTicks);
        Assert.Equal(6, sim.Light.LevelAt(tile));
    }

    [Fact]
    public void Thunder_MasksEvenLoudNoises()
    {
        var sim = new Simulation(new WorldMap(4, 4), seed: 3);
        sim.SetWeather(new WeatherSettings(Storm: true));
        var strike = sim.Weather.StrikesBetween(-1, 100_000).First();

        Assert.InRange(strike.ThunderDelayTicks, Weather.ThunderDelayMinTicks, Weather.ThunderDelayMaxTicks);
        Assert.True(sim.Weather.Thunder(strike.ThunderTick));
        Assert.Equal(Weather.ThunderMask, sim.Weather.HearingMask(strike.ThunderTick)); // no rain or wind
    }

    [Fact]
    public void Weather_IsPartOfTheReplay()
    {
        ulong Run(WeatherSettings weather)
        {
            var sim = DemoWorld.Create(2024);
            sim.SetWeather(weather);
            sim.RunUntil(Ticks.FromSeconds(60));
            return sim.ComputeStateHash();
        }

        Assert.Equal(Run(WeatherSettings.TestbedStorm), Run(WeatherSettings.TestbedStorm));
        Assert.NotEqual(Run(WeatherSettings.TestbedStorm), Run(WeatherSettings.Clear));
    }

    private const string StormyMap =
        "bhmap 2\n" +
        "size 2 1\n" +
        "ambient 6\n" +
        "weather rain 70 wind E 8 storm\n" +
        "heights\n" +
        "0 0\n" +
        "cover\n" +
        "..\n" +
        "markers\n" +
        "player 0 0\n" +
        "end\n";

    [Fact]
    public void MapFiles_KeepTheWeatherLine()
    {
        var data = MapFile.Parse(StormyMap);
        Assert.Equal(WeatherSettings.TestbedStorm, data.StartingWeather);

        string written = MapFile.Serialize(data);
        Assert.Contains("ambient 6\nweather rain 70 wind E 8 storm\nheights\n", written);
        Assert.Equal(WeatherSettings.TestbedStorm, MapFile.Parse(written).StartingWeather);

        var sim = ScenarioBuilder.Build(data, seed: 1);
        Assert.Equal(WeatherSettings.TestbedStorm, sim.Weather.Settings);
    }

    [Theory]
    [InlineData("weather hail")]
    [InlineData("weather rain 120")]
    [InlineData("weather wind up 5")]
    [InlineData("weather wind E 30")]
    public void MapFiles_RejectBadWeather(string line)
    {
        Assert.Throws<FormatException>(() => MapFile.Parse(StormyMap.Replace("weather rain 70 wind E 8 storm", line)));
    }
}

/// <summary>Scent trails: laid by walking and waiting, washed out by rain, carried by the wind, followed by the Rex.</summary>
public class ScentTests
{
    [Fact]
    public void Walking_LeavesATrail_AndKeepingStillMarksTheSpot()
    {
        var sim = new Simulation(new WorldMap(20, 20), seed: 1);
        var human = sim.AddActor("Pete", 'h', new TilePos(5, 5), Species.Human, new ScriptedController(new ActorAction[]
        {
            new MoveAction(Direction.East), new MoveAction(Direction.East), new MoveAction(Direction.East),
        }));

        sim.RunUntil(3_000);

        var trail = sim.ScentMarks.Where(m => m.ActorId == human.Id && !m.Blood).ToList();
        Assert.Equal(new TilePos(5, 5), trail[0].Position); // where it started out
        Assert.Contains(trail, m => m.Position == new TilePos(6, 5));
        Assert.Contains(trail, m => m.Position == new TilePos(7, 5));
        Assert.Contains(trail, m => m.Position == new TilePos(8, 5) && m.Tick >= ScentRules.IdleMarkTicks); // still there, later
        Assert.All(trail, m => Assert.Equal(Species.Human.ScentStrength, m.Strength));
    }

    [Fact]
    public void Rain_WashesTrailsOut_BloodLastsLonger()
    {
        int StrengthAfter40Seconds(int rain, bool bleeding)
        {
            var sim = new Simulation(new WorldMap(10, 10), seed: 1);
            var human = sim.AddActor("Pete", 'h', new TilePos(5, 5), Species.Human, new HoldStillController());
            if (bleeding)
                sim.Injure(human, BodySection.LeftArm, Severity.Red, bleeding: true, "a test");
            sim.SetWeather(new WeatherSettings(RainPercent: rain));
            sim.RunUntil(4_000);
            var first = sim.ScentMarks.First(m => m.Blood == bleeding);
            return first.StrengthAt(sim.Weather.Wash(sim.CurrentTick));
        }

        Assert.Equal(19, StrengthAfter40Seconds(rain: 0, bleeding: false));  // 25 × (180 - 40) / 180
        Assert.Equal(0, StrengthAfter40Seconds(rain: 100, bleeding: false)); // the clock runs ×5: gone after 36 s
        Assert.Equal(66, StrengthAfter40Seconds(rain: 100, bleeding: true)); // 100 × (600 - 200) / 600
    }

    /// <summary>The Rex at (20,20) facing north (nose at (20,16)); Pete keeping still at (27,20), 8 tiles off.</summary>
    private static (Simulation Sim, Actor Rex, Actor Human) RexAndStillHuman(WeatherSettings weather)
    {
        var sim = new Simulation(new WorldMap(40, 40), seed: 1) { AmbientLightPercent = 6 };
        sim.SetWeather(weather);
        var human = sim.AddActor("Pete", 'h', new TilePos(27, 20), Species.Human, new HoldStillController());
        var rex = sim.AddActor("Rex", 'T', new TilePos(20, 20), Species.Rex, new HoldStillController(), Direction.North);
        sim.RunUntil(1); // Pete's first turn marks the spot
        return (sim, rex, human);
    }

    [Fact]
    public void TheRex_SmellsATrailDownwind_NotUpwind_NorInStillAir()
    {
        // Scent 25 loses 3 per tile in still air; an 8 m/s wind makes that 1.6 downwind and 4.4 upwind.
        var (downwind, rex, human) = RexAndStillHuman(new WeatherSettings(WindFrom: Direction.East, WindSpeed: 8));
        var trail = Assert.Single(PerceptionSystem.Perceive(downwind, rex, 0).Smelled);
        Assert.Equal(human.Id, trail.ActorId);
        Assert.Equal(new TilePos(27, 20), trail.At);
        Assert.Equal(12, trail.Perceived); // 24 (25 a tick after it was laid, rounded down) - 8 tiles × 1.6
        Assert.False(trail.Blood);

        var (upwind, rex2, _) = RexAndStillHuman(new WeatherSettings(WindFrom: Direction.West, WindSpeed: 8));
        Assert.Empty(PerceptionSystem.Perceive(upwind, rex2, 0).Smelled);

        var (still, rex3, _) = RexAndStillHuman(WeatherSettings.Clear);
        Assert.Empty(PerceptionSystem.Perceive(still, rex3, 0).Smelled); // 25 - 24 = 1
    }

    [Fact]
    public void TheRex_FollowsBloodItCantSee()
    {
        // Pete bleeds behind the Rex, out of its view and silent. Blood (100) at 16 tiles in still air
        // arrives at 52: it knows prey is there and turns to hunt.
        var sim = new Simulation(new WorldMap(40, 40), seed: 1) { AmbientLightPercent = 6 };
        var human = sim.AddActor("Pete", 'h', new TilePos(20, 32), Species.Human, new HoldStillController());
        sim.Injure(human, BodySection.LeftArm, Severity.Red, bleeding: true, "a test");
        var brain = new CreatureBrain(AiProfile.Rex, sim.Rng.Fork("ai:rex"));
        var rex = sim.AddActor("Rex", 'T', new TilePos(20, 20), Species.Rex, brain, Direction.North);

        sim.RunUntil(1);

        Assert.False(PerceptionSystem.Perceive(sim, rex, 0).Detects(human));
        var focus = brain.Focus;
        Assert.NotNull(focus);
        Assert.Equal(ContactKey.ForActor(human.Id), focus.Key);
        Assert.Equal(ContactClass.Prey, focus.Class);
        Assert.Equal("smelled blood", focus.Cue);
        Assert.Equal(Activity.Hunting, brain.Activity);
    }

    [Fact]
    public void OnlyANewerMark_RefreshesWhatItKnows()
    {
        // So it follows a trail forwards, and doesn't go back to where it already looked.
        var sim = new Simulation(new WorldMap(40, 40), seed: 1) { AmbientLightPercent = 6 };
        var human = sim.AddActor("Pete", 'h', new TilePos(20, 32), Species.Human, new HoldStillController());
        sim.Injure(human, BodySection.LeftArm, Severity.Red, bleeding: true, "a test");
        var rex = sim.AddActor("Rex", 'T', new TilePos(20, 20), Species.Rex, new HoldStillController(), Direction.North);
        var memory = new ContactMemory(AiProfile.Rex);
        var key = ContactKey.ForActor(human.Id);

        sim.RunUntil(1);
        memory.Observe(sim, rex, PerceptionSystem.Perceive(sim, rex, 0));
        Assert.Equal(60, memory.Find(key)!.Confidence(sim.CurrentTick, AiProfile.Rex));

        sim.RunUntil(201); // 2 s later: the same blood, nothing newer
        memory.Observe(sim, rex, PerceptionSystem.Perceive(sim, rex, 0));
        Assert.Equal(1, memory.Find(key)!.LastUpdatedTick);
        Assert.Equal(44, memory.Find(key)!.Confidence(sim.CurrentTick, AiProfile.Rex)); // fading, 8 per second

        sim.RunUntil(2_100); // Pete has marked the spot again (every 20 s)
        memory.Observe(sim, rex, PerceptionSystem.Perceive(sim, rex, 0));
        Assert.Equal(sim.CurrentTick, memory.Find(key)!.LastUpdatedTick);
        Assert.Equal(60, memory.Find(key)!.Confidence(sim.CurrentTick, AiProfile.Rex));
    }

    [Fact]
    public void AnIntactCab_KeepsYourScentIn()
    {
        var map = new WorldMap(10, 10);
        map.Cover[new TilePos(5, 5)] = CoverType.Vehicle;
        map.Cover[new TilePos(6, 5)] = CoverType.Vehicle;
        var sim = new Simulation(map, seed: 1);
        var human = sim.AddActor("Marisol", 'h', new TilePos(5, 5), Species.Human, new HoldStillController());

        sim.RunUntil(100);
        Assert.DoesNotContain(sim.ScentMarks, m => m.ActorId == human.Id);

        sim.SetVehicleIntegrity(Assert.Single(sim.Vehicles), 0);
        sim.RunUntil(2_200); // next mark due 20 s after the last try
        Assert.Contains(sim.ScentMarks, m => m.ActorId == human.Id && m.Position == new TilePos(5, 5));
    }
}

/// <summary>Ground vibration: the Rex's tread is felt long before it's seen, unless the ground is mud.</summary>
public class VibrationTests
{
    /// <summary>The Rex at (20,30) takes one step north to (20,29); Pete stands at (20,5), 24 tiles off.</summary>
    private static (Simulation Sim, Actor Rex, Actor Human) RexStepsTowardPete(WeatherSettings weather)
    {
        var sim = new Simulation(new WorldMap(40, 40), seed: 1);
        sim.SetWeather(weather);
        var rex = sim.AddActor("Rex", 'T', new TilePos(20, 30), Species.Rex,
            new ScriptedController(new ActorAction[] { new MoveAction(Direction.North) }), Direction.North);
        var human = sim.AddActor("Pete", 'h', new TilePos(20, 5), Species.Human, new HoldStillController());
        sim.RunUntil(1);
        return (sim, rex, human);
    }

    [Fact]
    public void People_FeelTheRexComing_UnlessTheGroundIsMud()
    {
        var (dry, rex, human) = RexStepsTowardPete(WeatherSettings.Clear);
        var felt = Assert.Single(PerceptionSystem.Perceive(dry, human, 0).Felt);
        Assert.Equal(rex.Id, felt.Vibration.ActorId);
        Assert.Equal(60, felt.Vibration.Strength);
        Assert.Equal(12, felt.Perceived); // 60 - 2 × 24

        var (mud, _, human2) = RexStepsTowardPete(new WeatherSettings(RainPercent: 100));
        Assert.Equal(30, Assert.Single(mud.RecentVibrations).Strength); // soft ground in a downpour: half
        Assert.Empty(PerceptionSystem.Perceive(mud, human2, 0).Felt);
    }

    [Fact]
    public void AHeavyTread_IsAThreat_ALightOneIsNot()
    {
        var (sim, rex, human) = RexStepsTowardPete(WeatherSettings.Clear);
        Percepts Feeling(int strength, int? who) => new(human, Array.Empty<SightContact>(), Array.Empty<HeardNoise>(),
            Array.Empty<SeenLight>(), felt: new[] { new FeltVibration(new VibrationEvent(0, new TilePos(20, 29), strength, who), 12) });

        var memory = new ContactMemory(AiProfile.Human);
        memory.Observe(sim, human, Feeling(60, rex.Id));
        var contact = memory.Find(ContactKey.ForActor(rex.Id))!;
        Assert.Equal(ContactClass.Threat, contact.Class);
        Assert.Equal("felt the ground shake", contact.Cue);
        Assert.Equal(36, contact.Confidence(sim.CurrentTick, AiProfile.Human)); // 12 × 3
        Assert.Equal(8, contact.Uncertainty(sim.CurrentTick, AiProfile.Human));  // 2 + 24 / 4

        var light = new ContactMemory(AiProfile.Human);
        light.Observe(sim, human, Feeling(15, 99));
        Assert.Equal(ContactClass.Unknown, light.Find(ContactKey.ForActor(99))!.Class);
    }

    [Fact]
    public void TheRex_DoesntFeelPeoplesFootsteps()
    {
        var sim = new Simulation(new WorldMap(40, 40), seed: 1);
        var rex = sim.AddActor("Rex", 'T', new TilePos(20, 20), Species.Rex, new HoldStillController(), Direction.North);
        sim.AddActor("Pete", 'h', new TilePos(22, 18), Species.Human,
            new ScriptedController(new ActorAction[] { new MoveAction(Direction.East, Sprint: true) }));

        sim.RunUntil(1);

        Assert.Equal(15, Assert.Single(sim.RecentVibrations).Strength); // 10, half again for a sprint
        Assert.Empty(PerceptionSystem.Perceive(sim, rex, 0).Felt);      // 15 - 2 × 3 = 9: it needs 20
    }

    [Fact]
    public void Ground_CarriesFootfallsDifferently()
    {
        var sim = new Simulation(new WorldMap(4, 4), seed: 1);
        var grass = Covers.Get(CoverType.Grass);
        Assert.Equal(100, sim.Weather.VibrationPercent(grass, outdoors: true));
        Assert.Equal(160, sim.Weather.VibrationPercent(Covers.Deck, outdoors: true));
        Assert.Equal(80, sim.Weather.VibrationPercent(Covers.Get(CoverType.DenseJungle), outdoors: true));

        sim.SetWeather(new WeatherSettings(RainPercent: 100));
        Assert.Equal(50, sim.Weather.VibrationPercent(grass, outdoors: true));
        Assert.Equal(100, sim.Weather.VibrationPercent(grass, outdoors: false)); // dry under a roof
        Assert.Equal(110, sim.Weather.VibrationPercent(Covers.Get(CoverType.Track), outdoors: true));
    }
}
