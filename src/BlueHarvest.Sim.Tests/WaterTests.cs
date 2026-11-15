using BlueHarvest.Sim.Actors;
using BlueHarvest.Sim.Perception;
using BlueHarvest.Sim.World;
using BlueHarvest.Sim.World.Generation;
using Xunit;

namespace BlueHarvest.Sim.Tests;

/// <summary>World effects step 17: the tide, the swell and the rivers' rise, as numbers.</summary>
public class WaterLevelsTests
{
    /// <summary>Weather that rains a set amount everywhere, by game minute.</summary>
    private sealed class Rain(Func<long, int> percent) : IWeatherSource
    {
        public WeatherSettings At(long x, long y, long gameMinute) => new(RainPercent: percent(gameMinute));
    }

    private static WaterLevels Levels(ulong seed = 1, params RiverGauge[] rivers) => new(seed, Direction.NorthWest, rivers);

    [Theory]
    [InlineData(0, 0)]
    [InlineData(30, 500)]
    [InlineData(90, 1000)]
    [InlineData(180, 0)]
    [InlineData(270, -1000)]
    [InlineData(-90, -1000)]
    [InlineData(450, 1000)]
    public void TheWholeNumberSine_IsRightAtTheMarks(int degrees, int permille) =>
        Assert.InRange(WaterLevels.SinPermille(degrees), permille - 2, permille + 2);

    [Fact]
    public void TheTide_TurnsAboutTwiceADay_WithinHalfAMetreOrSo()
    {
        WaterLevels w = Levels();
        var highs = new List<long>();
        int lowest = int.MaxValue, highest = int.MinValue;
        for (long m = 1; m < 4 * WorldClock.MinutesPerDay; m++)
        {
            int level = w.SeaLevelDm(m, moonPhase: 0);
            lowest = Math.Min(lowest, level);
            highest = Math.Max(highest, level);
            if (w.TideTurn(m, 0) == 1)
                highs.Add(m);
        }
        Assert.InRange(highs.Count, 7, 8); // four days of a 12 h 25 min tide
        for (int i = 1; i < highs.Count; i++)
            Assert.InRange(highs[i] - highs[i - 1], 600, 900);
        Assert.InRange(highest, 5, 6);
        Assert.InRange(lowest, -6, -5);
    }

    [Fact]
    public void SpringTides_AtNewAndFullMoon_RunFartherThanNeaps()
    {
        WaterLevels w = Levels();
        int Range(int phase)
        {
            int lo = int.MaxValue, hi = int.MinValue;
            for (long m = 0; m < WorldClock.MinutesPerDay; m++)
            {
                int level = w.SeaLevelCm(m, phase);
                lo = Math.Min(lo, level);
                hi = Math.Max(hi, level);
            }
            return hi - lo;
        }
        int spring = Range(0), full = Range(Sky.MoonCycle / 2), neap = Range(Sky.MoonCycle / 4);
        // a day holds both highs: one of them is at least the main tide's height, whatever the daily tide does
        Assert.True(spring >= 95, $"spring range {spring} cm");
        Assert.True(full >= 95, $"full-moon range {full} cm");
        Assert.True(neap <= 85, $"neap range {neap} cm");
    }

    [Fact]
    public void TheSameSeed_GivesTheSameTides_AndAnotherSeedOthers()
    {
        WaterLevels a = Levels(5), b = Levels(5), c = Levels(6);
        bool differs = false;
        for (long m = 0; m < 2000; m += 7)
        {
            Assert.Equal(a.SeaLevelCm(m, 3), b.SeaLevelCm(m, 3));
            Assert.Equal(a.SwellPercent(m), b.SwellPercent(m));
            differs |= a.SeaLevelCm(m, 3) != c.SeaLevelCm(m, 3);
        }
        Assert.True(differs);
    }

    [Fact]
    public void TheSwell_StaysInRange_AndChangesSlowly()
    {
        WaterLevels w = Levels();
        int last = w.SwellPercent(0);
        for (long m = 1; m < 10 * WorldClock.MinutesPerDay; m++)
        {
            int now = w.SwellPercent(m);
            Assert.InRange(now, 20, 100);
            Assert.InRange(now - last, -1, 1);
            last = now;
        }
    }

    [Fact]
    public void Surf_IsBigOnAnOnshoreGale_AndSmallOnASheltered_CalmCoast()
    {
        WaterLevels w = Levels();
        var gale = new WeatherSettings(WindFrom: Direction.East, WindSpeed: 20);
        Assert.Equal(100, w.SurfPercent(Direction.East, gale, 0));    // the coast looks straight into it
        var calm = new WeatherSettings(WindSpeed: 0);
        Assert.Equal(10, w.SurfPercent(Direction.SouthEast, calm, 0)); // turned away from the north-west swell
        Assert.True(w.SurfPercent(Direction.NorthWest, calm, 0) >= 13); // facing the swell
        Assert.True(w.SurfPercent(Direction.East, gale, 0) > w.SurfPercent(Direction.West, gale, 0));
    }

    [Fact]
    public void ARiver_RisesWithTheRainOverItsCatchment_AndFallsAfter()
    {
        var gauge = new RiverGauge(0, 20, [(100, 100), (200, 200)]);
        WaterLevels w = Levels(1, gauge);
        // twelve hours of downpour, then dry
        var weather = new Rain(m => m < 12 * 60 ? 100 : 0);
        Assert.Equal(0, w.RiverRiseDm(0, 0, weather));
        Assert.Equal(20, w.RiverRiseDm(0, 12 * 60 + 5, weather));          // full
        Assert.Equal(200, w.RiverSpeedPercent(0, 12 * 60 + 5, weather));  // twice as fast
        int later = w.RiverRiseDm(0, 18 * 60, weather);
        Assert.InRange(later, 1, 19);                                      // falling
        Assert.Equal(0, w.RiverRiseDm(0, 25 * 60, weather));               // back to usual
        Assert.Equal(0, w.RiverRiseDm(7, 12 * 60, weather));               // a river it has no gauge for
        Assert.Equal(0, w.RiverRiseDm(0, 12 * 60, null));                  // no weather, no rise
    }
}

/// <summary>The water in a simulation, on small hand-made maps whose water layer the test fills in.</summary>
public class WaterSimulationTests
{
    private sealed class Rain(Func<long, int> percent) : IWeatherSource
    {
        public WeatherSettings At(long x, long y, long gameMinute) => new(RainPercent: percent(gameMinute));
    }

    private sealed class Once(ActorAction action) : IActorController
    {
        private bool _done;

        public ActorAction? Decide(Actor self, Simulation sim)
        {
            if (_done) return new WaitAction(600);
            _done = true;
            return action;
        }
    }

    /// <summary>What a tile of the water layer should be at this water level.</summary>
    private static CoverType Expected(WaterTile w, int level)
    {
        int depth = level - w.GroundDm;
        return depth <= 0 ? w.DryCover : depth > WaterLevels.WadeDepthDm ? CoverType.DeepWater : CoverType.ShallowWater;
    }

    /// <summary>A beach running down into the sea from north to south: 8 dm above mean sea level at row 5, 2 dm lower each row.</summary>
    private static WorldMap Beach()
    {
        var map = new WorldMap(20, 20);
        for (int y = 5; y < 20; y++)
        {
            short ground = (short)(8 - 2 * (y - 5));
            for (int x = 0; x < 20; x++)
            {
                var p = new TilePos(x, y);
                map.Water[p] = new WaterTile(WaterKind.Sea, ground, CoverType.Sand, -1, Direction.South);
                map.Cover[p] = Expected(map.Water[p], 0);
            }
        }
        return map;
    }

    [Fact]
    public void TheTide_CoversAndUncoversTheBeach_AsTheSeaRisesAndFalls()
    {
        var sim = new Simulation(Beach(), seed: 3);
        var events = new List<WaterEvent>();
        sim.WaterChanged += events.Add;
        sim.UseWater(new WaterLevels(11, Direction.NorthWest, []));
        Assert.True(sim.HasSeaInWindow);
        void AllAsTheTideSays()
        {
            for (int y = 5; y < 20; y++)
                for (int x = 0; x < 20; x += 7)
                {
                    var p = new TilePos(x, y);
                    Assert.Equal(Expected(sim.Map.Water[p], sim.SeaLevelDm), sim.Cover[p]);
                }
        }
        AllAsTheTideSays();
        int first = sim.SeaLevelDm;
        int furthest = first;
        for (int hour = 1; hour <= 14; hour++)
        {
            sim.RunUntil(hour * WorldClock.TicksPerGameHour);
            AllAsTheTideSays();
            if (Math.Abs(sim.SeaLevelDm - first) > Math.Abs(furthest - first))
                furthest = sim.SeaLevelDm;
        }
        Assert.True(Math.Abs(furthest - first) >= 3, $"the sea only moved from {first} to {furthest} dm");
        Assert.True(sim.WaterChanges > 0);
        Assert.Contains(events, e => e.Kind == WaterEventKind.HighTide);
        Assert.Contains(events, e => e.Kind == WaterEventKind.LowTide);
        Assert.Equal(CoverType.Grass, sim.Cover[new TilePos(3, 2)]); // above the water layer, nothing changes
    }

    [Fact]
    public void TheTide_LeavesAloneWhatSomeoneChanged()
    {
        WorldMap map = Beach();
        var wreck = new TilePos(4, 12);
        map.Cover[wreck] = CoverType.Rubble;
        var sim = new Simulation(map, seed: 3);
        sim.UseWater(new WaterLevels(11, Direction.NorthWest, []));
        sim.RunUntil(14 * WorldClock.TicksPerGameHour);
        Assert.Equal(CoverType.Rubble, sim.Cover[wreck]);
    }

    /// <summary>
    /// A river running east along rows 10–11 (a 6 dm ford, 0.6 m deep), its banks 4 dm above the water
    /// on rows 9 and 12 and 15 dm on rows 8 and 13, grass beyond. Gauge 0 rises 2 m at full rain.
    /// </summary>
    private static (Simulation Sim, List<WaterEvent> Events) River(Func<long, int> rain, int flowDm = 6)
    {
        var map = new WorldMap(24, 24);
        for (int x = 0; x < 24; x++)
        {
            foreach ((int y, short ground) in new (int, short)[] { (8, 15), (9, 4), (10, -6), (11, -6), (12, 4), (13, 15) })
            {
                var p = new TilePos(x, y);
                bool bed = ground < 0;
                map.Water[p] = new WaterTile(WaterKind.River, ground, bed ? CoverType.Mud : CoverType.Grass, 0, null);
                map.Cover[p] = bed ? CoverType.ShallowWater : CoverType.Grass;
                if (bed)
                    map.Flow[p] = new WaterFlow((sbyte)flowDm, 0);
            }
        }
        var sim = new Simulation(map, seed: 4);
        var events = new List<WaterEvent>();
        sim.WaterChanged += events.Add;
        sim.FollowWeather(new Rain(rain), 0, 0);
        sim.UseWater(new WaterLevels(1, Direction.NorthWest, [new RiverGauge(0, 20, [(5, 10), (15, 10)])]));
        return (sim, events);
    }

    [Fact]
    public void AfterHeavyRain_TheRiverRises_ClosesItsFord_AndFloodsItsBanks_ThenFallsBack()
    {
        var (sim, events) = River(m => m < 12 * 60 ? 100 : 0);
        var ford = new TilePos(5, 10);
        var lowBank = new TilePos(5, 9);
        var highBank = new TilePos(5, 8);
        // someone wading the ford when the rain starts: the water doesn't close over them
        var wader = sim.AddActor("Wader", 'w', new TilePos(20, 10), Species.Human, new Once(new WaitAction(600)));
        Assert.Equal(CoverType.ShallowWater, sim.Cover[ford]);

        sim.RunUntil(12 * WorldClock.TicksPerGameHour + WorldClock.TicksPerGameMinute);
        Assert.Equal(20, sim.RiverRiseDm(0));
        Assert.Equal(CoverType.DeepWater, sim.Cover[ford]);      // 2.6 m deep: no wading
        Assert.Equal(CoverType.DeepWater, sim.Cover[lowBank]);   // 1.6 m over the low bank
        Assert.Equal(CoverType.ShallowWater, sim.Cover[highBank]);
        Assert.Equal(CoverType.ShallowWater, sim.Cover[wader.Position]);
        Assert.Equal(12, sim.CurrentAt(ford).SpeedDm);            // twice as fast
        Assert.Contains(events, e => e.Kind == WaterEventKind.RiverRising);
        Assert.Contains(events, e => e.Kind == WaterEventKind.RiverInFlood);

        sim.RunUntil(26 * WorldClock.TicksPerGameHour);
        Assert.Equal(0, sim.RiverRiseDm(0));
        Assert.Equal(CoverType.ShallowWater, sim.Cover[ford]);
        Assert.Equal(CoverType.Grass, sim.Cover[lowBank]);
        Assert.Equal(CoverType.Grass, sim.Cover[highBank]);
        Assert.Contains(events, e => e.Kind == WaterEventKind.RiverFalling);
    }

    [Fact]
    public void WadingWithTheCurrent_IsQuicker_ThanAgainstIt()
    {
        long Step(Direction way)
        {
            var (sim, _) = River(_ => 0, flowDm: 10);
            var you = sim.AddActor("You", '@', new TilePos(10, 10), Species.Human, new Once(new MoveAction(way)));
            long start = sim.CurrentTick;
            sim.RunUntil(start + 1);
            return you.NextActTick - start;
        }
        long withIt = Step(Direction.East), againstIt = Step(Direction.West);
        Assert.True(againstIt > withIt * 3 / 2, $"against {againstIt} ticks, with {withIt}");
    }

    [Fact]
    public void FastWater_SweepsAWaderDownstream()
    {
        var (sim, _) = River(_ => 0, flowDm: 24);
        var swept = new List<Actor>();
        sim.Swept += (a, _) => swept.Add(a);
        var you = sim.AddActor("You", '@', new TilePos(5, 9), Species.Human, new Once(new MoveAction(Direction.South)));
        sim.RunUntil(sim.CurrentTick + 1);
        Assert.Equal(new TilePos(6, 10), you.Position);
        Assert.Equal(you, Assert.Single(swept));
        Assert.Contains(sim.RecentNoises, n => n.Kind == NoiseKinds.Splashing);
    }

    [Fact]
    public void RunningWater_DrownsOutSoundsNearIt()
    {
        var (sim, _) = River(_ => 0, flowDm: 20);
        Assert.Equal(10, sim.WaterMaskAt(new TilePos(5, 10)));
        Assert.Equal(9, sim.WaterMaskAt(new TilePos(5, 9)));
        Assert.Equal(0, sim.WaterMaskAt(new TilePos(5, 23)));

        var listener = sim.AddActor("Ears", 'e', new TilePos(5, 8), Species.Human, new Once(new WaitAction(600)));
        int heard = PerceptionSystem.HearingLevel(sim, listener, new TilePos(5, 4), 30);
        Assert.Equal(30 - 4 - sim.WaterMaskAt(new TilePos(5, 8)), heard);
    }

    [Fact]
    public void WithoutAWaterLayer_NothingMoves()
    {
        var map = new WorldMap(10, 10);
        map.Cover[new TilePos(2, 2)] = CoverType.ShallowWater;
        var sim = new Simulation(map, seed: 1);
        sim.UseWater(new WaterLevels(1, Direction.NorthWest, []));
        sim.RunUntil(8 * WorldClock.TicksPerGameHour);
        Assert.Equal(CoverType.ShallowWater, sim.Cover[new TilePos(2, 2)]);
        Assert.Equal(0, sim.WaterChanges);
        Assert.False(sim.HasSeaInWindow);
    }
}

/// <summary>The generated island's water layer and gauges, and the window keeping tides out of its change store.</summary>
public class IslandWaterTests
{
    private const string Island = "SmallNoCliffs";

    [Fact]
    public void EveryRiver_HasAGauge_RisingABitOrALot()
    {
        IslandWorld world = TestIslands.World(Island);
        WaterLevels w = IslandWaters.For(world, runSeed: 1);
        Assert.Equal(world.Shape.Rivers.Count(r => r.Xs.Length > 0), w.Rivers.Count);
        Assert.All(w.Rivers, g =>
        {
            Assert.InRange(g.RiseDmAtFull, 8, 40);
            Assert.NotEmpty(g.Catchment);
        });
    }

    private static IslandWindow AtTheCoast(IslandWorld world)
    {
        // a coastal facility if there is one, else the first river's mouth
        Site? docks = world.Works.Sites.FirstOrDefault(t => t.Kind is "docks" or "airstrip");
        (double x, double y) = docks is not null
            ? (docks.X, docks.Y)
            : (world.Shape.Rivers[0].Xs[^1], world.Shape.Rivers[0].Ys[^1]);
        return new IslandWindow(world, TestIslands.Chunker(Island), x, y, 4);
    }

    [Fact]
    public void TheCoast_HasTidalTiles_AndSurf()
    {
        IslandWorld world = TestIslands.World(Island);
        IslandWindow window = AtTheCoast(world);
        int tidal = 0, shore = 0;
        for (int y = 0; y < window.Size; y++)
            for (int x = 0; x < window.Size; x++)
            {
                var p = new TilePos(x, y);
                WaterTile w = window.Map.Water[p];
                if (w.Kind == WaterKind.Sea)
                {
                    tidal++;
                    Assert.InRange(w.GroundDm, IslandTiles.TidalLowestDm, IslandTiles.TidalHighestDm);
                    Assert.True(w.IsWaterState(window.Map.Cover[p]), $"{p} is {window.Map.Cover[p]}");
                }
                if (w.ShoreFacing is not null)
                    shore++;
            }
        Assert.True(tidal > 0, "no tidal tiles at the coast");
        Assert.True(shore > 0, "no surf along the coast");
    }

    [Fact]
    public void TheRivers_HaveBedsAndBanks()
    {
        IslandWorld world = TestIslands.World(Island);
        River river = world.Shape.Rivers.OrderByDescending(r => r.Qs.Length > 0 ? r.Qs[^1] : 0).First();
        int mid = river.Xs.Length / 2;
        var window = new IslandWindow(world, TestIslands.Chunker(Island), river.Xs[mid], river.Ys[mid], 3);
        int beds = 0, banks = 0;
        for (int y = 0; y < window.Size; y++)
            for (int x = 0; x < window.Size; x++)
            {
                var p = new TilePos(x, y);
                WaterTile w = window.Map.Water[p];
                if (w.Kind != WaterKind.River) continue;
                if (window.Map.Cover[p] is CoverType.ShallowWater or CoverType.DeepWater)
                {
                    beds++;
                    Assert.True(w.GroundDm < 0);
                }
                else
                {
                    banks++;
                    Assert.InRange(w.GroundDm, 1, IslandTiles.FloodHighestDm);
                    Assert.Equal(w.DryCover, window.Map.Cover[p]);
                }
            }
        Assert.True(beds > 0, "no river bed");
        Assert.True(banks > 0, "no river banks");
    }

    [Fact]
    public void TheTide_IsNotKeptAsAChange_WhenTheWindowMoves()
    {
        IslandWorld world = TestIslands.World(Island);
        IslandWindow window = AtTheCoast(world);
        var sim = new Simulation(window.Map, seed: 2);
        int before = window.AllChanges().TileCount;
        sim.UseWater(IslandWaters.For(world, runSeed: 1));
        sim.RunUntil(7 * WorldClock.TicksPerGameHour);
        Assert.True(sim.WaterChanges > 0, "the tide changed nothing");
        Assert.Equal(before, window.AllChanges().TileCount);
    }
}
