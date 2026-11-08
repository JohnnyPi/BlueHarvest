using BlueHarvest.Sim.Actors;
using BlueHarvest.Sim.World;
using Xunit;

namespace BlueHarvest.Sim.Tests;

public class BodyTests
{
    /// <summary>A Rex the test drives, on open ground unless the map says otherwise.</summary>
    private static (Simulation Sim, PlayerController Drive, Actor Rex) RexOn(WorldMap map, TilePos at, Direction facing)
    {
        var sim = new Simulation(map, seed: 1);
        var drive = new PlayerController();
        var rex = sim.AddActor("Rex", 'T', at, Species.Rex, drive, facing);
        sim.RunUntilInputNeeded();
        return (sim, drive, rex);
    }

    private static void Move(Simulation sim, PlayerController drive, Direction direction)
    {
        drive.Pending = new MoveAction(direction);
        sim.RunUntilInputNeeded();
    }

    [Fact]
    public void RexFootprint_FacingSouth_IsTwelveTilesLong()
    {
        var tiles = BodyPlan.Rex.Footprint(new TilePos(10, 10), Compass16.Of(Direction.South));

        var core = tiles.Where(t => t.Part == BodyPart.Core).Select(t => t.Position).ToList();
        Assert.Equal(12, core.Count); // 3 wide × 4 long
        Assert.All(core, p => Assert.InRange(p.X, 9, 11));
        Assert.All(core, p => Assert.InRange(p.Y, 8, 11));

        Assert.Equal(new[] { 12, 13, 14 }, tiles.Where(t => t.Part == BodyPart.Head).Select(t => t.Position.Y));
        Assert.Equal(new[] { 3, 4, 5, 6, 7 }, tiles.Where(t => t.Part == BodyPart.Tail).Select(t => t.Position.Y));
        Assert.Equal(new TilePos(10, 14), BodyPlan.Rex.HeadTip(new TilePos(10, 10), Compass16.Of(Direction.South)));
    }

    [Fact]
    public void Rex_TurnsIn45DegreeSteps_ThenWalks()
    {
        var (sim, drive, rex) = RexOn(new WorldMap(40, 40), new TilePos(15, 15), Direction.North);

        Move(sim, drive, Direction.East);
        Assert.Equal(Direction.NorthEast, rex.Facing);
        Assert.Equal(new TilePos(15, 15), rex.Position);
        Assert.Equal(60, rex.NextActTick);

        Move(sim, drive, Direction.East);
        Assert.Equal(Direction.East, rex.Facing);

        Move(sim, drive, Direction.East);
        Assert.Equal(new TilePos(16, 15), rex.Position);
        Assert.Equal(170, rex.NextActTick); // two turns of 60, one step of 50
    }

    [Fact]
    public void Rex_SmashesSparseJungle_ButStopsAtDenseJungle()
    {
        var map = new WorldMap(30, 30);
        for (int x = 0; x < 30; x++)
        {
            map.Cover[new TilePos(x, 10)] = CoverType.SparseJungle;
            map.Cover[new TilePos(x, 5)] = CoverType.DenseJungle;
        }
        var (sim, drive, rex) = RexOn(map, new TilePos(15, 15), Direction.North);

        for (int i = 0; i < 12; i++)
            Move(sim, drive, Direction.North);

        Assert.Equal(new TilePos(15, 7), rex.Position); // core front at y=6, against the dense row
        Assert.Equal("dense jungle is in the way", sim.MoveBlockReason(rex, Direction.North));
        Assert.Equal(CoverType.Grass, sim.Cover[new TilePos(15, 10)]);        // smashed, then trampled
        Assert.Equal(CoverType.SparseJungle, sim.Cover[new TilePos(12, 10)]); // beside its path
        Assert.True(sim.CoverChanges > 0);
    }

    [Fact]
    public void Rex_BreaksThroughFence_HumansCanFollowThroughTheGap()
    {
        var map = new WorldMap(20, 20);
        for (int x = 0; x < 20; x++)
            map.Cover[new TilePos(x, 10)] = CoverType.Fence;
        var (sim, drive, rex) = RexOn(map, new TilePos(10, 15), Direction.North);

        for (int i = 0; i < 6; i++)
            Move(sim, drive, Direction.North);

        Assert.Equal(CoverType.BrokenFence, sim.Cover[new TilePos(9, 10)]);
        Assert.Equal(CoverType.Fence, sim.Cover[new TilePos(5, 10)]);
        Assert.Contains(sim.RecentNoises, n => n.Kind == "crashing");

        var human = sim.AddActor("You", '@', new TilePos(5, 11), Species.Human, new PlayerController());
        Assert.Equal("the electric fence is in the way", sim.MoveBlockReason(human, Direction.North));
        Assert.False(Covers.Get(CoverType.BrokenFence).BlocksMovement);
    }

    [Fact]
    public void Rex_CantFitThroughADoorway()
    {
        var map = new WorldMap(9, 12);
        for (int x = 0; x < 9; x++)
            map.Cover[new TilePos(x, 4)] = CoverType.Wall;
        map.Cover[new TilePos(4, 4)] = CoverType.Door;
        var (sim, drive, rex) = RexOn(map, new TilePos(4, 7), Direction.North);

        Move(sim, drive, Direction.North);
        Assert.Equal(new TilePos(4, 6), rex.Position);
        Assert.Equal("a wall is in the way", sim.MoveBlockReason(rex, Direction.North));
    }

    [Fact]
    public void Rex_ShovesVehicles_UnlessTheyreStuck()
    {
        var map = new WorldMap(20, 20);
        for (int x = 8; x <= 12; x++)
            map.Cover[new TilePos(x, 10)] = CoverType.Vehicle;
        var (sim, drive, rex) = RexOn(map, new TilePos(10, 13), Direction.North);
        var vehicle = Assert.Single(sim.Vehicles);

        Move(sim, drive, Direction.North);  // free step
        Assert.Equal("a vehicle is in the way", sim.MoveBlockReason(rex, Direction.North)); // only a shove moves it
        drive.Pending = new MoveAction(Direction.North, Shove: true);
        sim.RunUntilInputNeeded();

        Assert.Equal(new TilePos(10, 11), rex.Position);
        Assert.All(vehicle.Tiles, t => Assert.Equal(9, t.Y));
        Assert.Equal(CoverType.Grass, sim.Cover[new TilePos(10, 10)]);
        Assert.Equal(CoverType.Vehicle, sim.Cover[new TilePos(10, 9)]);
        Assert.Equal(Vehicle.MaxIntegrity - Simulation.ShoveDamage, vehicle.Integrity);
        Assert.Equal(50 + 50 + Simulation.ShoveTicks, rex.NextActTick);

        for (int x = 6; x <= 14; x++)
            sim.Cover[new TilePos(x, 8)] = CoverType.Wall;
        Assert.Equal("vehicle 1 won't budge: a wall is behind it", sim.MoveBlockReason(rex, Direction.North, shove: true));
    }

    [Fact]
    public void TurningSweepsTheTail_IntoAnyoneStandingThere()
    {
        var sim = new Simulation(new WorldMap(40, 40), seed: 1);
        var drive = new PlayerController();
        var rex = sim.AddActor("Rex", 'T', new TilePos(15, 15), Species.Rex, drive, Direction.North);
        var human = sim.AddActor("Pete", 'h', new TilePos(13, 21), Species.Human,
            new ScriptedController(Array.Empty<ActorAction>()));
        var struck = new List<(Actor, Actor)>();
        sim.ActorStruck += (striker, victim) => struck.Add((striker, victim));
        sim.RunUntilInputNeeded();

        Move(sim, drive, Direction.East); // north → north-east swings the tail through (13,21)

        Assert.Equal(new[] { (rex, human) }, struck);
        Assert.Contains(sim.RecentNoises, n => n.Kind == "a heavy blow");
    }

    [Fact]
    public void RexCore_OccupiesTiles_HeadAndTailDont()
    {
        var sim = new Simulation(new WorldMap(40, 40), seed: 1);
        var rex = sim.AddActor("Rex", 'T', new TilePos(15, 15), Species.Rex, new PlayerController(), Direction.North);
        var human = sim.AddActor("You", '@', new TilePos(13, 15), Species.Human, new PlayerController());

        Assert.Equal(rex, sim.ActorAt(new TilePos(14, 15)));
        Assert.Null(sim.ActorAt(new TilePos(15, 19)));   // tail
        Assert.Null(sim.ActorAt(new TilePos(15, 12)));   // head
        Assert.Equal("Rex is in the way", sim.MoveBlockReason(human, Direction.East));
        Assert.Equal(new TilePos(15, 11), sim.EyeTile(rex));
    }

    [Fact]
    public void LargeBody_MustSpawnInsideTheMap()
    {
        var sim = new Simulation(new WorldMap(10, 10), seed: 1);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            sim.AddActor("Rex", 'T', new TilePos(0, 5), Species.Rex, new PlayerController(), Direction.North));
    }
}
