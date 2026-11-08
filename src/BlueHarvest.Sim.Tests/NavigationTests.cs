using BlueHarvest.Sim.Actors;
using BlueHarvest.Sim.Navigation;
using BlueHarvest.Sim.World;
using Xunit;

namespace BlueHarvest.Sim.Tests;

public class NavigationTests
{
    private static int Chebyshev(TilePos a, TilePos b) => Math.Max(Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));

    /// <summary>Feeds moves to a player-controlled actor and returns where it ends up.</summary>
    private static void Follow(Simulation sim, PlayerController drive, IEnumerable<Direction> moves)
    {
        sim.RunUntilInputNeeded();
        foreach (var move in moves)
        {
            drive.Pending = new MoveAction(move);
            sim.RunUntilInputNeeded();
        }
    }

    [Fact]
    public void Human_PathsAroundAWall()
    {
        var map = new WorldMap(10, 10);
        for (int y = 0; y <= 8; y++)
            map.Cover[new TilePos(5, y)] = CoverType.Wall;
        var sim = new Simulation(map, seed: 1);
        var drive = new PlayerController();
        var human = sim.AddActor("You", '@', new TilePos(2, 2), Species.Human, drive);

        var path = Pathfinder.FindPath(sim, human, new TilePos(8, 2));

        Assert.NotNull(path);
        Follow(sim, drive, path);
        Assert.Equal(new TilePos(8, 2), human.Position);
    }

    [Fact]
    public void Rex_PlansTurns_AndArrives()
    {
        var sim = new Simulation(new WorldMap(40, 40), seed: 1);
        var drive = new PlayerController();
        var rex = sim.AddActor("Rex", 'T', new TilePos(20, 20), Species.Rex, drive, Direction.North);

        var path = Pathfinder.FindPath(sim, rex, new TilePos(30, 20), tolerance: 1);

        Assert.NotNull(path);
        var poses = Pathfinder.Trace(rex, path);
        Assert.Contains(poses, p => p.Anchor == new TilePos(20, 20)); // turned on the spot at least once
        Follow(sim, drive, path);
        Assert.True(Chebyshev(rex.Position, new TilePos(30, 20)) <= 1);
    }

    [Fact]
    public void Doorway_LetsHumansThrough_ButNotTheRex()
    {
        var map = new WorldMap(15, 15);
        for (int x = 0; x < 15; x++)
            map.Cover[new TilePos(x, 7)] = CoverType.Wall;
        map.Cover[new TilePos(7, 7)] = CoverType.Door;
        var sim = new Simulation(map, seed: 1);
        var rex = sim.AddActor("Rex", 'T', new TilePos(7, 11), Species.Rex, new PlayerController(), Direction.North);
        var human = sim.AddActor("You", '@', new TilePos(2, 12), Species.Human, new PlayerController());

        Assert.Null(Pathfinder.FindPath(sim, rex, new TilePos(7, 2)));
        Assert.NotNull(Pathfinder.FindPath(sim, human, new TilePos(7, 2)));
    }

    [Theory]
    [InlineData(12, 16, true)]  // 5-wide gap: the Rex fits
    [InlineData(13, 14, false)] // 2-wide gap: it doesn't, and it can't smash dense jungle
    public void Rex_NeedsAGapWideEnough_InDenseJungle(int gapFrom, int gapTo, bool expectPath)
    {
        var map = new WorldMap(30, 30);
        for (int x = 0; x < 30; x++)
        {
            if (x < gapFrom || x > gapTo)
                map.Cover[new TilePos(x, 12)] = CoverType.DenseJungle;
        }
        var sim = new Simulation(map, seed: 1);
        var rex = sim.AddActor("Rex", 'T', new TilePos(14, 20), Species.Rex, new PlayerController(), Direction.North);

        var path = Pathfinder.FindPath(sim, rex, new TilePos(14, 5), tolerance: 1);
        Assert.Equal(expectPath, path is not null);
    }

    [Fact]
    public void GoToController_ChasesAMovingGoal()
    {
        var sim = new Simulation(new WorldMap(40, 40), seed: 1);
        var goal = new TilePos(30, 30);
        var rex = sim.AddActor("Rex", 'T', new TilePos(10, 10), Species.Rex,
            new GoToController(() => goal, tolerance: 2), Direction.North);

        sim.RunUntil(Seconds(40));
        goal = new TilePos(30, 12);
        sim.RunUntil(Seconds(80));

        Assert.True(Chebyshev(rex.Position, goal) <= 2, $"Rex ended at {rex.Position}");
    }

    private static long Seconds(int seconds) => Core.Ticks.FromSeconds(seconds);
}
