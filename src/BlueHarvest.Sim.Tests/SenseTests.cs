using BlueHarvest.Sim.Actors;
using BlueHarvest.Sim.Perception;
using BlueHarvest.Sim.World;
using Xunit;
using BlueHarvest.Sim.ContentIntegration;

namespace BlueHarvest.Sim.Tests;

/// <summary>
/// Step 19: the player's sense report. Four senses, each cue placed around the player, and exposure,
/// the feeling of being watched, worked out from every creature's senses rather than the Rex's alone.
/// Hand-made grass fields in daylight unless a test says otherwise.
/// </summary>
public class SenseTests
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

    private static Simulation Field(int width = 40, int height = 20, CoverType cover = CoverType.Grass, int ambient = 100)
    {
        var map = new WorldMap(width, height);
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                map.Cover[new TilePos(x, y)] = cover;
        return new Simulation(TestContent.NewSession(), map, seed: 1) { AmbientLightPercent = ambient };
    }

    private static Actor You(Simulation sim, TilePos at, ActorAction? first = null, Loadout? loadout = null) =>
        sim.AddActor("You", '@', at, Species.Human, new Once(first ?? new WaitAction(600)), loadout: loadout);

    private static Actor Rex(Simulation sim, TilePos at, Direction facing, ActorAction? first = null) =>
        sim.AddActor("Rex", 'T', at, Species.Rex, new Once(first ?? new WaitAction(600)), facing: facing);

    private static SenseReport Report(Simulation sim, Actor you) =>
        SenseReport.For(sim, you, PerceptionSystem.Perceive(sim, you, 0), 0);

    // ---------- exposure ----------

    [Fact]
    public void ARexThatSeesYou_AndThatYouSee_IsNamed()
    {
        var sim = Field();
        var you = You(sim, new TilePos(10, 10));
        var rex = Rex(sim, new TilePos(22, 10), Direction.West); // its eyes at (18,10), 8 tiles off

        var report = Report(sim, you);

        // you're still: 100 × 80 distance × 15 still = 12, against its 10
        Assert.Equal(ExposureStage.Seen, report.Exposure.Stage);
        Assert.Equal(rex.Id, report.Exposure.WatcherId);
        Assert.Equal(12, report.Exposure.Score);
        var cue = Assert.Single(report.Sight, c => c.Kind == CueKind.Creature);
        Assert.Equal(rex.Id, cue.ActorId);
        Assert.Equal(new TilePos(22, 10), cue.At);
        Assert.Equal(Direction.East, cue.Toward);
        Assert.Equal(12, cue.Distance);
        Assert.True(cue.Danger);
    }

    [Fact]
    public void ARexThatSeesYou_FromBeyondYourSight_HasSpottedYou()
    {
        var sim = Field(width: 60);
        var you = You(sim, new TilePos(4, 10), new MoveAction(Direction.East));
        Rex(sim, new TilePos(38, 10), Direction.West); // eyes at (34,10); its body 33 tiles off, past your 30
        sim.RunUntil(1);

        var report = Report(sim, you);

        // moving, 29 tiles from its eyes: 100 × 27 distance = 27, against its 10
        Assert.Equal(ExposureStage.Spotted, report.Exposure.Stage);
        Assert.Equal(27, report.Exposure.Score);
        Assert.DoesNotContain(report.Sight, c => c.Kind == CueKind.Creature);
    }

    [Fact]
    public void ARexThatHearsYou_BehindIt_MakesYouFeelWatched()
    {
        var sim = Field();
        var you = You(sim, new TilePos(10, 10), new MoveAction(Direction.East));
        var rex = Rex(sim, new TilePos(20, 10), Direction.East); // facing away; eyes at (24,10)
        sim.RunUntil(1);

        var exposure = Report(sim, you).Exposure;

        // it can't see behind itself; your footsteps (18) carry 13 tiles to its ears: 5, against its 3
        Assert.Equal(ExposureStage.Watched, exposure.Stage);
        Assert.Equal(rex.Id, exposure.WatcherId);
        Assert.True(exposure.Heard);
        Assert.False(exposure.Smelled);
        Assert.Equal(0, exposure.Score);
    }

    [Fact]
    public void WithNothingAround_NothingWatches_AndTheSensesAreQuiet()
    {
        var sim = Field();
        var you = You(sim, new TilePos(10, 10));

        var report = Report(sim, you);

        Assert.Equal(Exposure.Nothing, report.Exposure);
        Assert.Empty(report.Sight);
        Assert.Empty(report.Sound);
        Assert.Empty(report.Smell);
        Assert.Empty(report.Feel);
    }

    // ---------- sight and sound ----------

    [Fact]
    public void AThrownFlare_IsSeen_AndHeard_ThoughNobodyMadeTheNoise()
    {
        var sim = Field(cover: CoverType.Track); // nothing to catch fire
        var you = You(sim, new TilePos(10, 10), new ThrowAction(CoreItems.Flare, new TilePos(15, 10)), Loadout.TestbedSurvivor);
        sim.RunUntil(1);
        TilePos landed = Assert.Single(sim.GroundFlares);

        var report = Report(sim, you);

        var heard = Assert.Single(report.Sound, c => c.NoiseKind == NoiseKinds.Flare);
        Assert.Equal(CueKind.Noise, heard.Kind);
        Assert.Equal(landed, heard.At);
        Assert.Null(heard.ActorId);
        var seen = Assert.Single(report.Sight, c => c.Kind == CueKind.Flare);
        Assert.Equal(landed, seen.At);
    }

    /// <summary>A river running east along rows 10–11 at <paramref name="flowDm"/> dm/s, grass banks either side (as in the water tests).</summary>
    private static Simulation River(int flowDm)
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
        var sim = new Simulation(TestContent.NewSession(), map, seed: 4);
        sim.UseWater(new WaterLevels(1, Direction.NorthWest, []));
        return sim;
    }

    [Fact]
    public void OnTheBank_YouHearTheRiver_StraightAhead()
    {
        var sim = River(flowDm: 20);
        var you = You(sim, new TilePos(5, 8));
        Assert.Equal(8, sim.WaterMaskAt(new TilePos(5, 8))); // the river's 10, two tiles off

        var cue = Assert.Single(Report(sim, you).Sound);

        Assert.Equal(CueKind.Water, cue.Kind);
        Assert.Equal(new TilePos(5, 10), cue.At);
        Assert.Equal(Direction.South, cue.Toward);
        Assert.Equal(40, cue.Strength);
    }

    [Fact]
    public void WadingTheRiver_YouFeelTheCurrentPushEast()
    {
        var sim = River(flowDm: 20);
        var you = You(sim, new TilePos(5, 10));

        var cue = Assert.Single(Report(sim, you).Feel, c => c.Kind == CueKind.Current);

        Assert.Equal(Direction.East, cue.Toward);
        Assert.Null(cue.At);
        Assert.True(cue.Danger); // 20 dm/s can sweep you off your feet
    }

    // ---------- smell ----------

    [Fact]
    public void DownwindOfARex_YouSmellItsTrail_UpwindYouDont()
    {
        SenseReport From(TilePos where)
        {
            var sim = Field();
            sim.SetWeather(new WeatherSettings(WindFrom: Direction.East, WindSpeed: 10)); // blowing west
            var you = You(sim, where);
            Rex(sim, new TilePos(15, 9), Direction.West, new MoveAction(Direction.West)); // a step to (14,9) marks it
            sim.RunUntil(1);
            return Report(sim, you);
        }

        // 8 tiles straight downwind: 39 left of its 40, less 1 a tile = 31, against the nose's 25
        var downwind = Assert.Single(From(new TilePos(6, 12)).Smell, c => c.Kind == CueKind.Trail);
        Assert.Equal(new TilePos(14, 9), downwind.At);
        Assert.True(downwind.Danger);
        // 12 tiles upwind: 5 a tile, so nothing reaches you
        Assert.DoesNotContain(From(new TilePos(26, 12)).Smell, c => c.Kind == CueKind.Trail);
    }

    [Fact]
    public void SpiltFuel_NearBy_Smells()
    {
        var sim = Field();
        var you = You(sim, new TilePos(10, 10));
        sim.SpillFuel(new TilePos(12, 10));

        var cue = Assert.Single(Report(sim, you).Smell, c => c.Kind == CueKind.Fuel);

        Assert.Equal(new TilePos(12, 10), cue.At);
        Assert.Equal(Direction.East, cue.Toward);
        Assert.Equal(2, cue.Distance);
    }

    // ---------- feel ----------

    [Fact]
    public void ARexStep_TwentyTilesOff_ShakesTheGround()
    {
        var sim = Field();
        var you = You(sim, new TilePos(10, 10));
        var rex = Rex(sim, new TilePos(31, 10), Direction.West, new MoveAction(Direction.West));
        sim.RunUntil(1);

        var cue = Assert.Single(Report(sim, you).Feel, c => c.Kind == CueKind.Tremor);

        // its 60, less 2 a tile: 20 underfoot, above your 10 (and heavy enough to alarm)
        Assert.Equal(rex.Id, cue.ActorId);
        Assert.Equal(new TilePos(30, 10), cue.At);
        Assert.Equal(Direction.East, cue.Toward);
        Assert.Equal(60, cue.Strength);
        Assert.True(cue.Danger);
    }

    [Fact]
    public void AFire_ThreeTilesOff_GivesHeat()
    {
        var sim = Field();
        var you = You(sim, new TilePos(10, 10));
        var fire = new TilePos(13, 10);
        sim.SpillFuel(fire);
        Assert.True(sim.TryIgnite(fire, FireRules.FlareHeat, "a test"));

        var cue = Assert.Single(Report(sim, you).Feel, c => c.Kind == CueKind.Heat);

        Assert.Equal(fire, cue.At);
        Assert.Equal(57, cue.Strength); // 100 × 4 / 7
        Assert.False(cue.Danger);       // not yet within 2 tiles
    }

    // ---------- determinism ----------

    [Fact]
    public void TheSameState_GivesTheSameReport()
    {
        SenseReport Build()
        {
            var sim = Field();
            sim.SetWeather(new WeatherSettings(RainPercent: 40, WindFrom: Direction.East, WindSpeed: 6));
            var you = You(sim, new TilePos(10, 10), new MoveAction(Direction.East));
            Rex(sim, new TilePos(25, 10), Direction.West, new MoveAction(Direction.West));
            sim.SpillFuel(new TilePos(13, 11));
            sim.RunUntil(1);
            return Report(sim, you);
        }

        SenseReport a = Build(), b = Build();
        Assert.Equal(a.All.ToList(), b.All.ToList());
        Assert.Equal(a.Exposure, b.Exposure);
        Assert.NotEmpty(a.All);
    }
}
