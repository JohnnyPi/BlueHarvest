using BlueHarvest.Sim.Actors;
using BlueHarvest.Sim.Lighting;
using BlueHarvest.Sim.Perception;
using BlueHarvest.Sim.World;
using Xunit;

namespace BlueHarvest.Sim.Tests;

public class SightLineTests
{
    private static WorldMap Row(string cover, string? heights = null)
    {
        var map = new WorldMap(cover.Length, 1);
        for (int x = 0; x < cover.Length; x++)
        {
            var p = new TilePos(x, 0);
            map.Cover[p] = Covers.FromFileChar(cover[x])!.Type;
            if (heights is not null)
                map.Heights.SetLevel(p, HeightMap.ParseLevel(heights[x]));
        }
        return map;
    }

    [Fact]
    public void OpenGround_IsClear()
    {
        var trace = SightLine.Trace(Row(".........."), new TilePos(0, 0), 16, new TilePos(9, 0), 17, true);
        Assert.Equal(LineTrace.Clear, trace);
    }

    [Fact]
    public void Wall_Blocks_AndCountsAsSolid()
    {
        var trace = SightLine.Trace(Row(".....#...."), new TilePos(0, 0), 16, new TilePos(9, 0), 17, true);
        Assert.Equal(0, trace.Visibility);
        Assert.Equal(1, trace.SolidTiles);
    }

    [Fact]
    public void DenseJungle_ThinsTheLine()
    {
        var trace = SightLine.Trace(Row(".&."), new TilePos(0, 0), 16, new TilePos(2, 0), 17, true);
        Assert.Equal(55, trace.Visibility); // 45% opacity
    }

    [Fact]
    public void Ridge_BlocksHumanEyes_ButNotRexEyes()
    {
        var map = Row("....", heights: "0040"); // a 2 m ridge
        var human = SightLine.Trace(map, new TilePos(0, 0), 16, new TilePos(3, 0), 17, true);
        var rex = SightLine.Trace(map, new TilePos(0, 0), 45, new TilePos(3, 0), 17, true);

        Assert.True(human.TerrainBlocked);
        Assert.Equal(0, human.Visibility);
        Assert.Equal(100, rex.Visibility);
    }

    [Fact]
    public void IndoorAndOutdoor_DontSeeEachOther()
    {
        var trace = SightLine.Trace(Row("._"), new TilePos(0, 0), 16, new TilePos(1, 0), 17, true);
        Assert.True(trace.IndoorMismatch);
        Assert.Equal(0, trace.Visibility);
    }
}

public class AngleTests
{
    [Theory]
    [InlineData(1, 0, 500, true)]    // straight ahead
    [InlineData(1, 1, 500, true)]    // 45° is inside a 120° view
    [InlineData(0, 1, 500, false)]   // 90° is outside it
    [InlineData(-1, 0, 500, false)]  // behind
    [InlineData(0, 1, 0, true)]      // 90° is the edge of a 180° view
    [InlineData(-1, 1, 0, false)]
    [InlineData(-1, 0, -1000, true)] // 360° sees everything
    public void WithinCone_FacingEast(int dx, int dy, int cosPermille, bool expected)
    {
        Assert.Equal(expected, Angles.WithinCone(dx, dy, Direction.East, cosPermille));
    }

    [Theory]
    [InlineData(5, 1, Direction.East)]
    [InlineData(1, 5, Direction.South)]
    [InlineData(3, 3, Direction.SouthEast)]
    [InlineData(-4, -4, Direction.NorthWest)]
    [InlineData(0, -7, Direction.North)]
    public void FromDelta_PicksNearestDirection(int dx, int dy, Direction expected)
    {
        Assert.Equal(expected, Directions.FromDelta(dx, dy));
    }
}

public class LightGridTests
{
    private static WorldMap Open(int width, int height) => new(width, height);

    [Fact]
    public void Emergency_Light_FallsOff_AndCastsShadows()
    {
        var map = Open(11, 11);
        map.Cover[new TilePos(6, 5)] = CoverType.Wall;
        var grid = LightGrid.Compute(map, new[] { LightSource.Emergency(new TilePos(5, 5)) }, ambientPercent: 5);

        Assert.Equal(65, grid.LevelAt(new TilePos(5, 5))); // 5 ambient + 60
        Assert.Equal(50, grid.LevelAt(new TilePos(2, 5))); // 3 tiles away: 60 × 27/36 = 45
        Assert.Equal(5, grid.LevelAt(new TilePos(8, 5)));  // behind the wall
        Assert.Equal(5, grid.LevelAt(new TilePos(0, 0)));  // outside the radius
    }

    [Fact]
    public void Headlight_ShinesAhead_NotBehind_NorOnItself()
    {
        var map = Open(20, 11);
        var grid = LightGrid.Compute(map, new[] { LightSource.Headlight(new TilePos(10, 5), Direction.East) }, ambientPercent: 5);

        Assert.Equal(86, grid.LevelAt(new TilePos(15, 5))); // 5 + 90 × 231/256
        Assert.Equal(5, grid.LevelAt(new TilePos(5, 5)));
        Assert.Equal(5, grid.LevelAt(new TilePos(10, 5)));
    }

    [Fact]
    public void Indoors_StartsDarker()
    {
        var map = Open(2, 1);
        map.Cover[new TilePos(1, 0)] = CoverType.Floor;
        var grid = LightGrid.Compute(map, Array.Empty<LightSource>(), ambientPercent: 30);
        Assert.Equal(30, grid.LevelAt(new TilePos(0, 0)));
        Assert.Equal(10, grid.LevelAt(new TilePos(1, 0)));
    }
}

public class PerceptionTests
{
    /// <summary>A human the test drives, and an east-facing Rex that only waits, its eyes 10 tiles away.</summary>
    private static (Simulation Sim, PlayerController Player, Actor Human, Actor Rex) Standoff(int ambient)
    {
        var sim = new Simulation(new WorldMap(30, 5), seed: 1) { AmbientLightPercent = ambient };
        var player = new PlayerController();
        var human = sim.AddActor("You", '@', new TilePos(17, 2), Species.Human, player);
        var rex = sim.AddActor("Rex", 'T', new TilePos(3, 2), Species.Rex,
            new ScriptedController(Array.Empty<ActorAction>()), facing: Direction.East);
        sim.RunUntilInputNeeded();
        return (sim, player, human, rex);
    }

    [Fact]
    public void Rex_MissesStillHuman_InDim_Light()
    {
        var (sim, _, human, rex) = Standoff(ambient: 20);
        var seen = PerceptionSystem.Perceive(sim, rex, 0).ContactWith(human);

        Assert.NotNull(seen);
        Assert.False(seen.Moving);
        Assert.Equal(4, seen.Score); // 100 vis × 40 light × 75 distance × 15 still
        Assert.False(seen.Detected);
    }

    [Fact]
    public void Rex_SpotsAndHearsHuman_WhoMoves()
    {
        var (sim, player, human, rex) = Standoff(ambient: 20);
        player.Pending = new MoveAction(Direction.East);
        sim.RunUntilInputNeeded();

        var percepts = PerceptionSystem.Perceive(sim, rex, 0);
        var seen = percepts.ContactWith(human);
        Assert.NotNull(seen);
        Assert.True(seen.Moving);
        Assert.Equal(28, seen.Score); // 11 tiles away now
        Assert.True(percepts.Detects(human));
        Assert.True(percepts.Hears(human)); // 18 loudness - 11 tiles = 7
    }

    [Fact]
    public void Observer_DoesNotHearItself()
    {
        var (sim, player, human, _) = Standoff(ambient: 20);
        player.Pending = new MoveAction(Direction.East);
        sim.RunUntilInputNeeded();
        Assert.Empty(PerceptionSystem.Perceive(sim, human, 0).Heard);
    }

    [Fact]
    public void Rex_CantSeeBehindItself()
    {
        var sim = new Simulation(new WorldMap(30, 5), seed: 1);
        var rex = sim.AddActor("Rex", 'T', new TilePos(15, 2), Species.Rex, new PlayerController(), facing: Direction.East);
        Assert.Equal(new TilePos(19, 2), sim.EyeTile(rex)); // it looks from its head
        Assert.Equal(0, PerceptionSystem.SightScore(sim, rex, new TilePos(5, 2), 17, moving: true));
        Assert.True(PerceptionSystem.SightScore(sim, rex, new TilePos(25, 2), 17, moving: true) > 0);
    }

    [Fact]
    public void HumanDeepInJungle_IsHidden_EvenMoving()
    {
        var map = new WorldMap(20, 3);
        for (int x = 14; x <= 17; x++)
            map.Cover[new TilePos(x, 1)] = CoverType.DenseJungle;
        var sim = new Simulation(map, seed: 1);
        var rex = sim.AddActor("Rex", 'T', new TilePos(3, 1), Species.Rex, new PlayerController(), facing: Direction.East);
        // Its eyes are at (7,1): the jungle starts 7 tiles ahead.

        int hidden = PerceptionSystem.SightScore(sim, rex, new TilePos(17, 1), 17, moving: true);
        int open = PerceptionSystem.SightScore(sim, rex, new TilePos(13, 1), 17, moving: true); // just short of the jungle

        Assert.Equal(6, hidden); // 55% → 30% → 16% through three tiles, 8% inside the fourth
        Assert.True(hidden < Species.Rex.Senses.DetectThreshold);
        Assert.True(open >= Species.Rex.Senses.DetectThreshold);
    }

    [Fact]
    public void Walls_MuffleNoise()
    {
        var open = new Simulation(new WorldMap(12, 1), seed: 1);
        var listener = open.AddActor("Listener", '@', new TilePos(0, 0), Species.Human, new PlayerController());
        Assert.Equal(8, PerceptionSystem.HearingLevel(open, listener, new TilePos(10, 0), 18));

        var walled = new WorldMap(12, 1);
        walled.Cover[new TilePos(5, 0)] = CoverType.Wall;
        var sim = new Simulation(walled, seed: 1);
        var behindWall = sim.AddActor("Listener", '@', new TilePos(0, 0), Species.Human, new PlayerController());
        Assert.Equal(2, PerceptionSystem.HearingLevel(sim, behindWall, new TilePos(10, 0), 18));
    }

    [Fact]
    public void Moving_SetsFacing_AndMakesNoise()
    {
        var (sim, player, human, _) = Standoff(ambient: 20);
        player.Pending = new MoveAction(Direction.North);
        sim.RunUntilInputNeeded();

        Assert.Equal(Direction.North, human.Facing);
        Assert.Equal(0, human.LastMovedTick);
        var noise = Assert.Single(sim.RecentNoises);
        Assert.Equal("footsteps", noise.Kind);
        Assert.Equal(human.Id, noise.ActorId);
        Assert.Equal(Species.Human.FootstepLoudness, noise.Loudness);
    }
}
