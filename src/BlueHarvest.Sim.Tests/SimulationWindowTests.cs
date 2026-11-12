using BlueHarvest.Sim.Actors;
using BlueHarvest.Sim.Lighting;
using BlueHarvest.Sim.World;
using BlueHarvest.Sim.World.Generation;
using Xunit;

namespace BlueHarvest.Sim.Tests;

/// <summary>
/// Stage 6b: when a streamed map scrolls, everything the simulation holds by position moves with it.
/// </summary>
public class SimulationWindowTests
{
    private static Simulation Field(int size = 20) => new(new WorldMap(size, size), seed: 1);

    /// <summary>Scrolls the map and tells the simulation, as IslandWindow.Follow does.</summary>
    private static void Shift(Simulation sim, int dx, int dy)
    {
        sim.Map.Scroll(dx, dy);
        sim.ShiftWindow(dx, dy);
    }

    [Fact]
    public void Actors_MoveWithTheMap()
    {
        Simulation sim = Field();
        Actor a = sim.AddActor("A", 'a', new TilePos(10, 10), Species.Human, new HoldStillController());
        int raised = 0;
        sim.WindowShifted += (dx, dy) => { raised++; Assert.Equal((5, 0), (dx, dy)); };
        Shift(sim, 5, 0);
        Assert.Equal(new TilePos(5, 10), a.Position);
        Assert.Same(a, sim.ActorAt(new TilePos(5, 10)));
        Assert.Null(sim.ActorAt(new TilePos(10, 10)));
        Assert.False(a.IsGone);
        Assert.Equal(1, raised);
        Assert.Equal(1, sim.WindowShifts);
    }

    [Fact]
    public void ActorsOutsideTheNewWindow_AreLeftBehind()
    {
        Simulation sim = Field();
        Actor near = sim.AddActor("Near", 'n', new TilePos(10, 10), Species.Human, new HoldStillController());
        Actor far = sim.AddActor("Far", 'f', new TilePos(2, 10), Species.Human, new HoldStillController());
        Shift(sim, 5, 0);
        Assert.True(far.LeftBehind);
        Assert.True(far.IsGone);
        Assert.True(sim.Map.InBounds(far.Position));
        Assert.Null(sim.ActorAt(far.Position));
        Assert.False(near.LeftBehind);
        // the simulation carries on without them
        sim.RunUntil(sim.CurrentTick + 500);
        Assert.Equal(new TilePos(5, 10), near.Position);
    }

    [Fact]
    public void TheLeftBehind_ComeBackWhenTheMapReturns()
    {
        Simulation sim = Field();
        Actor near = sim.AddActor("Near", 'n', new TilePos(10, 10), Species.Human, new HoldStillController());
        Actor far = sim.AddActor("Far", 'f', new TilePos(2, 10), Species.Human, new HoldStillController());
        Shift(sim, 5, 0);
        Assert.True(far.LeftBehind);
        Assert.Equal(new TilePos(2, 10), far.ParkedAt);
        Assert.Equal(new TilePos(5, 0), sim.WindowOrigin);
        sim.RunUntil(sim.CurrentTick + 500); // long enough for the schedule to drop Far

        Shift(sim, -5, 0);
        Assert.False(far.LeftBehind);
        Assert.False(far.IsGone);
        Assert.Equal(new TilePos(2, 10), far.Position);
        Assert.Same(far, sim.ActorAt(new TilePos(2, 10)));
        Assert.Equal(new TilePos(10, 10), near.Position);
        Assert.Equal(new TilePos(0, 0), sim.WindowOrigin);

        // Far takes turns again, at the usual rate: two people waiting a step at a time
        long before = sim.ActionsResolved;
        int step = far.Movement.StepTicks;
        sim.RunUntil(sim.CurrentTick + step * 10);
        Assert.InRange(sim.ActionsResolved - before, 19, 23);
    }

    [Fact]
    public void TheLeftBehind_ComeBackAtOnce_WithoutASecondTurn()
    {
        // gone and back before the schedule noticed: Far must not end up with two turns queued
        Simulation sim = Field();
        sim.AddActor("Near", 'n', new TilePos(10, 10), Species.Human, new HoldStillController());
        Actor far = sim.AddActor("Far", 'f', new TilePos(2, 10), Species.Human, new HoldStillController());
        Shift(sim, 5, 0);
        Shift(sim, -5, 0);
        Assert.False(far.LeftBehind);
        long before = sim.ActionsResolved;
        sim.RunUntil(sim.CurrentTick + far.Movement.StepTicks * 10);
        Assert.InRange(sim.ActionsResolved - before, 19, 23);
    }

    [Fact]
    public void TheLeftBehind_StayAwayUntilTheMapCoversThem()
    {
        Simulation sim = Field();
        sim.AddActor("Near", 'n', new TilePos(12, 10), Species.Human, new HoldStillController());
        Actor far = sim.AddActor("Far", 'f', new TilePos(2, 10), Species.Human, new HoldStillController());
        Shift(sim, 6, 0);
        Assert.True(far.LeftBehind);
        Shift(sim, -2, 0); // Far's spot is still 2 tiles off the west edge
        Assert.True(far.LeftBehind);
        Shift(sim, -2, 0); // now it is the map's first column
        Assert.False(far.LeftBehind);
        Assert.Equal(new TilePos(0, 10), far.Position);
    }

    [Fact]
    public void LightsExitsAndNoises_MoveOrAreDropped()
    {
        Simulation sim = Field();
        sim.AddActor("A", 'a', new TilePos(10, 10), Species.Human, new HoldStillController());
        sim.AddLight(LightSource.Emergency(new TilePos(12, 10)));
        sim.AddLight(LightSource.Emergency(new TilePos(1, 1)));
        sim.AddExit(new TilePos(11, 11), "culvert");
        sim.AddExit(new TilePos(0, 0), "gate");
        sim.EmitNoise(new TilePos(10, 9), 20, "test");
        sim.EmitNoise(new TilePos(3, 3), 20, "test");
        Shift(sim, 5, 0);
        Assert.Equal(new TilePos(7, 10), sim.Lights[0].Position);
        Assert.True(sim.Lights[0].On);
        Assert.False(sim.Lights[1].On);
        Assert.True(sim.Map.InBounds(sim.Lights[1].Position));
        ExitPoint exit = Assert.Single(sim.Exits);
        Assert.Equal(new TilePos(6, 11), exit.Position);
        Assert.Equal(new TilePos(5, 9), Assert.Single(sim.RecentNoises).Position);
        _ = sim.Light; // the light grid rebuilds on the scrolled map without falling off it
    }

    [Fact]
    public void ThePlayer_CanWalkOnAfterAShift()
    {
        Simulation sim = Field();
        var controller = new PlayerController();
        Actor player = sim.AddActor("P", '@', new TilePos(10, 10), Species.Human, controller);
        sim.RunUntilInputNeeded();
        Shift(sim, 5, -3);
        Assert.Equal(new TilePos(5, 13), player.Position);
        controller.Pending = new MoveAction(Direction.East);
        sim.RunUntilInputNeeded();
        Assert.Equal(new TilePos(6, 13), player.Position);
        Assert.Same(player, sim.ActorAt(new TilePos(6, 13)));
        Assert.Null(sim.ActorAt(new TilePos(5, 13)));
    }

    [Fact]
    public void ShiftingThereAndBack_PutsEveryoneWhereTheyWere()
    {
        Simulation sim = Field();
        Actor a = sim.AddActor("A", 'a', new TilePos(10, 10), Species.Human, new HoldStillController());
        Actor b = sim.AddActor("B", 'b', new TilePos(12, 9), Species.Human, new HoldStillController());
        Shift(sim, 3, 2);
        Shift(sim, -3, -2);
        Assert.Equal(new TilePos(10, 10), a.Position);
        Assert.Equal(new TilePos(12, 9), b.Position);
        Assert.Same(a, sim.ActorAt(a.Position));
        Assert.Same(b, sim.ActorAt(b.Position));
        Assert.Equal(2, sim.WindowShifts);
    }

    [Fact]
    public void ShiftingByNothing_DoesNothing()
    {
        Simulation sim = Field();
        sim.AddActor("A", 'a', new TilePos(10, 10), Species.Human, new HoldStillController());
        sim.ShiftWindow(0, 0);
        Assert.Equal(0, sim.WindowShifts);
    }

    [Fact]
    public void Follow_KeepsThePlayerOnTheIslandAsTheWindowMoves()
    {
        const string island = "SmallNoCliffs";
        Site gen = TestIslands.Works(island).Sites.First(t => t.Kind == "genetics");
        var window = new IslandWindow(TestIslands.World(island), TestIslands.Chunker(island), gen.X, gen.Y, 4);
        var sim = new Simulation(window.Map, seed: 1);
        // start the player near the window's west edge
        TilePos start = window.NearestStandable(new TilePos(30, 128));
        Assert.True(window.NearEdge(start));
        (double wx, double wy) = window.ToWorld(start);
        Actor player = sim.AddActor("P", '@', start, Species.Human, new PlayerController());

        Assert.True(window.Follow(sim, player));

        Assert.False(window.NearEdge(player.Position));
        Assert.Equal((wx, wy), window.ToWorld(player.Position)); // same spot on the island
        Assert.Same(player, sim.ActorAt(player.Position));
        Assert.False(sim.Map.CoverAt(player.Position).BlocksMovement);
        Assert.Equal(1, sim.WindowShifts);
        Assert.False(window.Follow(sim, player)); // nothing more to do
        // and the map under them is the island at the new place
        var fresh = new IslandWindow(TestIslands.World(island), TestIslands.Chunker(island), wx, wy, 4);
        Assert.Equal((fresh.OriginX, fresh.OriginY), (window.OriginX, window.OriginY));
        for (int y = 0; y < window.Size; y += 3)
        {
            for (int x = 0; x < window.Size; x += 3)
            {
                var p = new TilePos(x, y);
                Assert.Equal(fresh.Map.Cover[p], window.Map.Cover[p]);
                Assert.Equal(fresh.Map.Heights[p], window.Map.Heights[p]);
            }
        }
    }

    [Fact]
    public void Follow_RefusesASimulationOnAnotherMap()
    {
        const string island = "SmallNoCliffs";
        var window = new IslandWindow(TestIslands.World(island), TestIslands.Chunker(island), 200, 200, 2);
        Simulation other = Field();
        Actor a = other.AddActor("A", 'a', new TilePos(1, 1), Species.Human, new HoldStillController());
        Assert.Throws<ArgumentException>(() => window.Follow(other, a));
    }
}
