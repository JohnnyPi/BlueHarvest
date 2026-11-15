using BlueHarvest.Sim.Actors;
using BlueHarvest.Sim.Ai;
using BlueHarvest.Sim.Injuries;
using BlueHarvest.Sim.Perception;
using BlueHarvest.Sim.Power;
using BlueHarvest.Sim.World;
using Xunit;

namespace BlueHarvest.Sim.Tests;

/// <summary>World effects step 18: fire. Hand-made maps (dry climate unless a test makes it wet).</summary>
public class FireTests
{
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

    private static WorldMap Field(CoverType cover, int size = 40)
    {
        var map = new WorldMap(size, size);
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
                map.Cover[new TilePos(x, y)] = cover;
        return map;
    }

    private static long Seconds(int s) => s * 100L;

    /// <summary>Lights a tile for certain: fuel on it, and a flare's heat.</summary>
    private static void LightWithFuel(Simulation sim, TilePos p)
    {
        sim.SpillFuel(p);
        Assert.True(sim.TryIgnite(p, FireRules.FlareHeat, "a test"));
    }

    [Theory]
    [InlineData(CoverType.Track)]
    [InlineData(CoverType.Wall)]
    [InlineData(CoverType.Sand)]
    [InlineData(CoverType.BareRock)]
    [InlineData(CoverType.ShallowWater)]
    [InlineData(CoverType.Fence)]
    public void MetalStoneSandAndWater_NeverBurnOfThemselves(CoverType cover)
    {
        var sim = new Simulation(Field(cover, 10), seed: 1);
        var p = new TilePos(5, 5);
        Assert.Equal(0, sim.BurnableAt(p));
        Assert.False(sim.TryIgnite(p, FireRules.LightningHeat, "a test"));
    }

    [Fact]
    public void FuelSpilt_OnTheTrack_Burns_ButNotOnWater()
    {
        WorldMap map = Field(CoverType.Track, 10);
        map.Cover[new TilePos(2, 2)] = CoverType.ShallowWater;
        var sim = new Simulation(map, seed: 1);
        sim.SpillFuel(new TilePos(5, 5));
        sim.SpillFuel(new TilePos(2, 2));
        Assert.Equal(FireRules.FuelBurnable, sim.BurnableAt(new TilePos(5, 5)));
        Assert.Equal(0, sim.BurnableAt(new TilePos(2, 2)));
    }

    [Fact]
    public void AFlareHeldToDryGrass_LightsIt_ButNotAfterRain()
    {
        long Burnt(int rain, out bool caughtAtOnce)
        {
            var sim = new Simulation(Field(CoverType.TallGrass, 20), seed: 3);
            sim.SetWeather(new WeatherSettings(RainPercent: rain));
            bool? caught = null;
            sim.Kindled += (_, _, c) => caught = c;
            sim.AddActor("You", '@', new TilePos(10, 10), Species.Human, new Once(new IgniteAction(Direction.East)), loadout: Loadout.TestbedSurvivor);
            sim.RunUntil(Seconds(60));
            caughtAtOnce = caught ?? false;
            Assert.True(caught.HasValue);
            return sim.TilesBurnt + sim.Fires.Count;
        }
        Assert.True(Burnt(0, out bool dry) > 0, "dry grass didn't burn");
        Assert.True(dry);                                                     // 60 + 70: it takes at once
        Assert.Equal(0, Burnt(60, out bool wet));                            // soaked
        Assert.False(wet);
    }

    [Fact]
    public void AFlare_WontLightDenseJungle_OrShortGrass_ButFuelOnWetGroundGoesUp()
    {
        var lawn = new Simulation(Field(CoverType.Grass, 10), seed: 3);
        for (int i = 0; i < 40; i++)
            Assert.False(lawn.TryIgnite(new TilePos(5, 5), FireRules.FlareHeat, "a test")); // 30 + 50

        var sim = new Simulation(Field(CoverType.DenseJungle, 20), seed: 3);
        var p = new TilePos(10, 10);
        for (int i = 0; i < 40; i++)
            Assert.False(sim.TryIgnite(p, FireRules.FlareHeat, "a test")); // 20 + 50: never enough

        sim.SetWeather(new WeatherSettings(RainPercent: 60));
        sim.SpillFuel(p);
        Assert.True(sim.TryIgnite(p, FireRules.FlareHeat, "a test"));
        Assert.Equal(100, sim.FireAt(p)!.Intensity);
    }

    [Fact]
    public void Fire_RunsDownwind_AndNotAgainstTheWind()
    {
        var sim = new Simulation(Field(CoverType.TallGrass), seed: 5);
        sim.SetWeather(new WeatherSettings(WindFrom: Direction.West, WindSpeed: 10)); // blowing east
        var start = new TilePos(15, 20);
        LightWithFuel(sim, start);
        sim.RunUntil(Seconds(40));
        int east = 0, west = 0;
        for (int x = 0; x < 40; x++)
        {
            var p = new TilePos(x, 20);
            if (sim.Cover[p] != CoverType.Ash && sim.FireAt(p) is null)
                continue;
            east = Math.Max(east, x - start.X);
            west = Math.Max(west, start.X - x);
        }
        Assert.True(east >= 6, $"only {east} tiles downwind");
        Assert.True(west <= 1, $"{west} tiles upwind");
    }

    [Fact]
    public void Fire_StopsAtWater_AndAtDampForest()
    {
        WorldMap map = Field(CoverType.TallGrass);
        for (int y = 0; y < 40; y++)
        {
            map.Cover[new TilePos(20, y)] = CoverType.ShallowWater;
            for (int x = 30; x < 40; x++)
                map.Cover[new TilePos(x, y)] = CoverType.DenseJungle;
        }
        // grass on the far side of the stream, then the jungle: fire set on both sides
        var sim = new Simulation(map, seed: 2);
        LightWithFuel(sim, new TilePos(10, 20));
        LightWithFuel(sim, new TilePos(25, 20));
        sim.RunUntil(Seconds(300));
        Assert.Empty(sim.Fires);
        for (int y = 0; y < 40; y++)
        {
            Assert.Equal(CoverType.ShallowWater, sim.Cover[new TilePos(20, y)]);
            for (int x = 30; x < 40; x++)
                Assert.Equal(CoverType.DenseJungle, sim.Cover[new TilePos(x, y)]);
        }
        Assert.True(sim.TilesBurnt > 20);
        Assert.Equal(CoverType.Ash, sim.Cover[new TilePos(10, 20)]);
    }

    [Fact]
    public void TheSameSeed_BurnsTheSameWay()
    {
        ulong Burn()
        {
            var sim = new Simulation(Field(CoverType.Grass), seed: 9);
            sim.SetWeather(new WeatherSettings(WindFrom: Direction.North, WindSpeed: 6));
            LightWithFuel(sim, new TilePos(20, 10));
            sim.RunUntil(Seconds(90));
            return sim.ComputeStateHash();
        }
        Assert.Equal(Burn(), Burn());
    }

    [Fact]
    public void ABurntOutFire_LeavesAsh_AndSaysSo()
    {
        var sim = new Simulation(Field(CoverType.Scrub, 12), seed: 4);
        sim.SetWeather(new WeatherSettings(RainPercent: 30)); // damp enough that it doesn't spread far
        bool outEvent = false;
        sim.FiresOut += () => outEvent = true;
        var p = new TilePos(6, 6);
        LightWithFuel(sim, p);
        sim.RunUntil(Seconds(600));
        Assert.Empty(sim.Fires);
        Assert.True(outEvent);
        Assert.Equal(CoverType.Ash, sim.Cover[p]);
        Assert.Equal(0, sim.BurnableAt(p));
    }

    [Fact]
    public void Rain_PutsAFireOut()
    {
        var sim = new Simulation(Field(CoverType.DenseJungle, 12), seed: 4);
        var p = new TilePos(6, 6);
        LightWithFuel(sim, p);
        sim.SetWeather(new WeatherSettings(RainPercent: 100));
        sim.RunUntil(Seconds(20));
        Assert.Empty(sim.Fires);
    }

    [Fact]
    public void StandingInFire_Burns()
    {
        var sim = new Simulation(Field(CoverType.TallGrass, 12), seed: 4);
        var you = sim.AddActor("You", '@', new TilePos(6, 6), Species.Human, new Once(new WaitAction(1000)));
        var burns = new List<Severity>();
        sim.Burned += (a, s) => { if (a == you) burns.Add(s); };
        LightWithFuel(sim, you.Position);
        sim.RunUntil(Seconds(3));
        Assert.Contains(Severity.Red, burns);
        Assert.Contains(Enum.GetValues<BodySection>(), s => you.Health[s].Severity != Severity.Green);
    }

    [Fact]
    public void Fire_MakesSmokeDownwind_ThatHidesWhatsBeyond()
    {
        var sim = new Simulation(Field(CoverType.TallGrass), seed: 6);
        sim.SetWeather(new WeatherSettings(WindFrom: Direction.West, WindSpeed: 10));
        LightWithFuel(sim, new TilePos(10, 20));
        sim.RunUntil(Seconds(4));
        Assert.True(sim.Map.Smoke[new TilePos(14, 20)] > 0);
        Assert.Equal(0, sim.Map.Smoke[new TilePos(10, 35)]);
        int through = SightLine.Trace(sim.Map, new TilePos(12, 18), 15, new TilePos(12, 26), 15, includeTargetCover: false).Visibility;
        int clear = SightLine.Trace(sim.Map, new TilePos(35, 2), 15, new TilePos(35, 10), 15, includeTargetCover: false).Visibility;
        Assert.True(through < clear, $"through the smoke {through}, clear air {clear}");
    }

    [Fact]
    public void Fire_GivesLight()
    {
        var sim = new Simulation(Field(CoverType.TallGrass, 20), seed: 6) { AmbientLightPercent = 2 };
        LightWithFuel(sim, new TilePos(10, 10));
        sim.RunUntil(Seconds(2));
        Assert.Contains(sim.Lights, l => l.On && l.Position == new TilePos(10, 10));
        Assert.True(sim.Light.LevelAt(new TilePos(11, 10)) > 20);
    }

    [Fact]
    public void PeopleStepOutOfFire_AndTheRexBacksAway()
    {
        var sim = new Simulation(Field(CoverType.Grass), seed: 7);
        var you = sim.AddActor("You", '@', new TilePos(10, 10), Species.Human, new Once(new WaitAction(600)));
        LightWithFuel(sim, new TilePos(11, 10));
        Assert.True(sim.FleeFire(you, Simulation.FireFearHuman) is MoveAction { Direction: var away } && away.Delta().Dx < 0);
        Assert.Null(sim.FleeFire(you, 0));

        var brain = new CreatureBrain(AiProfile.Rex, sim.Rng.Fork("ai:rex"));
        var rex = sim.AddActor("Rex", 'T', new TilePos(18, 10), Species.Rex, brain, Direction.East); // seven tiles off, facing away
        Assert.IsType<MoveAction>(brain.Decide(rex, sim));
        Assert.Equal(Activity.Fleeing, brain.Activity);
    }

    [Fact]
    public void SparksFromASaggingFence_LightTallGrass_WhenItsDry()
    {
        int Fires(int rain)
        {
            WorldMap map = Field(CoverType.TallGrass, 20);
            for (int x = 0; x < 20; x++)
                map.Cover[new TilePos(x, 10)] = CoverType.WeakFence;
            var sim = new Simulation(map, seed: 8);
            sim.SetWeather(new WeatherSettings(RainPercent: rain));
            var started = new List<string>();
            sim.FireStarted += (_, cause) => started.Add(cause);
            sim.AddSparks(new TilePos(10, 10));
            sim.RunUntil(Seconds(120));
            return started.Count;
        }
        Assert.True(Fires(0) > 0);
        Assert.Equal(0, Fires(70));
    }

    [Fact]
    public void ABurningPost_FallsToRubble()
    {
        WorldMap map = Field(CoverType.Grass, 12);
        var post = new TilePos(6, 6);
        map.Cover[post] = CoverType.Support;
        var sim = new Simulation(map, seed: 1);
        sim.SetWeather(new WeatherSettings(RainPercent: 40)); // the grass round it won't carry it
        LightWithFuel(sim, post);
        sim.RunUntil(Seconds(260));
        Assert.Equal(CoverType.Rubble, sim.Cover[post]);
    }

    [Fact]
    public void AVehicle_BurnsOut_AndDoesntBurnAgain()
    {
        WorldMap map = Field(CoverType.Track, 16);
        for (int x = 6; x <= 8; x++)
            for (int y = 6; y <= 7; y++)
                map.Cover[new TilePos(x, y)] = CoverType.Vehicle;
        var sim = new Simulation(map, seed: 1);
        Vehicle vehicle = Assert.Single(sim.Vehicles);
        LightWithFuel(sim, new TilePos(6, 6));
        sim.RunUntil(Seconds(10));
        Assert.True(sim.Fires.Count >= 4, "the tank didn't take the rest of it");
        sim.RunUntil(Seconds(400));
        Assert.Empty(sim.Fires);
        Assert.True(vehicle.Breached);
        Assert.Equal(0, sim.BurnableAt(new TilePos(7, 7)));
    }

    [Fact]
    public void FuelTanks_HeatedByAFire_GoUp()
    {
        WorldMap map = Field(CoverType.Grass, 20);
        var sim = new Simulation(map, seed: 1);
        var grid = new PowerGrid(seed: 2);
        PowerSite site = grid.AddSite("depot", "depot", 10, 10, 15, 10);
        FuelStore tanks = grid.AddDevice(new FuelStore(grid.NextDeviceId, site.Id, "the fuel tanks", new Footprint(8, 8, 10, 10), capacityLitres: 4000));
        PowerGrid.SetFuel(tanks, 3000, valveOpen: false);
        sim.UsePower(grid, 0, 0);
        var burst = new List<TilePos>();
        sim.TanksBurst += burst.Add;
        for (int y = 7; y <= 11; y++)
            LightWithFuel(sim, new TilePos(11, y));
        sim.RunUntil(Seconds(60));
        Assert.Equal(0, tanks.Litres);
        Assert.Single(burst);
        Assert.Contains(sim.RecentNoises, n => n.Kind == NoiseKinds.TanksBursting || n.Kind == NoiseKinds.Fire);
    }
}
