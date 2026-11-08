using BlueHarvest.Sim.Actors;
using BlueHarvest.Sim.Ai;
using BlueHarvest.Sim.Lighting;
using BlueHarvest.Sim.Perception;
using BlueHarvest.Sim.World;
using Xunit;

namespace BlueHarvest.Sim.Tests;

/// <summary>Doors and roofs.</summary>
public class DoorTests
{
    /// <summary>A human at (0,0) next to a doorway at (1,0) in the given state.</summary>
    private static (Simulation Sim, PlayerController Drive, Actor Human) AtDoor(DoorState state)
    {
        var map = new WorldMap(4, 1);
        map.Cover[new TilePos(1, 0)] = CoverType.Door;
        map.Structures.SetDoor(new TilePos(1, 0), state);
        var sim = new Simulation(map, seed: 1);
        var drive = new PlayerController();
        var human = sim.AddActor("You", '@', new TilePos(0, 0), Species.Human, drive);
        sim.RunUntilInputNeeded();
        return (sim, drive, human);
    }

    private static void Do(Simulation sim, PlayerController drive, ActorAction action)
    {
        drive.Pending = action;
        sim.RunUntilInputNeeded();
    }

    [Fact]
    public void WalkingIntoAClosedDoor_OpensIt_ThenYouCanStepThrough()
    {
        var (sim, drive, human) = AtDoor(DoorState.Closed);

        Do(sim, drive, new MoveAction(Direction.East));
        Assert.Equal(DoorState.Open, sim.Map.Structures.DoorAt(new TilePos(1, 0)));
        Assert.Equal(new TilePos(0, 0), human.Position);
        Assert.Equal(Simulation.DoorTicks, human.NextActTick);
        Assert.Contains(sim.RecentNoises, n => n.Kind == NoiseKinds.Door);

        Do(sim, drive, new MoveAction(Direction.East));
        Assert.Equal(new TilePos(1, 0), human.Position);
    }

    [Fact]
    public void UseDoor_ClosesAnOpenDoor()
    {
        var (sim, drive, _) = AtDoor(DoorState.Open);

        Do(sim, drive, new UseDoorAction(Direction.East));

        Assert.Equal(DoorState.Closed, sim.Map.Structures.DoorAt(new TilePos(1, 0)));
        Assert.Equal(1, sim.StructureChanges);
    }

    [Fact]
    public void LockedDoor_StaysShut()
    {
        var (sim, drive, human) = AtDoor(DoorState.Locked);

        Assert.Equal("a locked door is in the way", sim.MoveBlockReason(human, Direction.East));
        Do(sim, drive, new UseDoorAction(Direction.East));
        Assert.Equal(DoorState.Locked, sim.Map.Structures.DoorAt(new TilePos(1, 0)));
    }

    [Fact]
    public void Rex_CantOpenDoors()
    {
        var map = new WorldMap(9, 12);
        for (int x = 0; x < 9; x++)
            map.Cover[new TilePos(x, 4)] = CoverType.Wall;
        map.Cover[new TilePos(4, 4)] = CoverType.Door;
        map.Structures.SetDoor(new TilePos(4, 4), DoorState.Closed);
        var sim = new Simulation(map, seed: 1);
        var rex = sim.AddActor("Rex", 'T', new TilePos(4, 6), Species.Rex, new PlayerController(), Direction.North);

        Assert.Equal("a wall is in the way", sim.MoveBlockReason(rex, Direction.North));
        Assert.False(sim.Map.CoverAt(new TilePos(4, 4)).IsSmashedBy(rex.Species.Force));
    }

    [Fact]
    public void Light_SpillsThroughAnOpenDoorway_NotAClosedOne()
    {
        // A lamp outside a roofed room, shining in through its doorway at (3,0).
        var map = new WorldMap(6, 1);
        map.Cover[new TilePos(3, 0)] = CoverType.Door;
        for (int x = 4; x < 6; x++)
            map.Structures.SetRoof(new TilePos(x, 0), 30);
        var lamp = new[] { new LightSource(new TilePos(0, 0), HeightDm: 10, RadiusTiles: 8, IntensityPercent: 100) };

        int open = LightGrid.Compute(map, lamp, ambientPercent: 0).LevelAt(new TilePos(4, 0));
        map.Structures.SetDoor(new TilePos(3, 0), DoorState.Closed);
        int closed = LightGrid.Compute(map, lamp, ambientPercent: 0).LevelAt(new TilePos(4, 0));

        Assert.True(open > 0, "no light came through the open doorway");
        Assert.Equal(0, closed);
    }
}

/// <summary>Reach height: how high and low jaws get.</summary>
public class ReachTests
{
    [Theory]
    [InlineData(40, true)]  // a 4 m ledge: in reach
    [InlineData(60, false)] // a 6 m ledge: out of reach
    public void Rex_ReachesFiveMetresUp(int ledgeDm, bool reachable)
    {
        var map = new WorldMap(20, 20);
        for (int y = 12; y < 20; y++)
            for (int x = 0; x < 20; x++)
                map.Heights[new TilePos(x, y)] = (short)ledgeDm;
        var sim = new Simulation(map, seed: 1);
        var rex = sim.AddActor("Rex", 'T', new TilePos(10, 8), Species.Rex, new PlayerController(), Direction.South);

        Assert.Equal(reachable, sim.BiteZone(rex).Contains(new TilePos(10, 13)));
    }

    [Fact]
    public void Escape_FromALedge_TheRexCantReach()
    {
        var map = new WorldMap(40, 40);
        for (int y = 0; y < 8; y++)
            for (int x = 0; x < 40; x++)
                map.Heights[new TilePos(x, y)] = 80; // an 8 m ledge along the top
        var sim = new Simulation(map, seed: 1);
        var brain = new CreatureBrain(AiProfile.Rex, sim.Rng.Fork("ai:rex"));
        var rex = sim.AddActor("Rex", 'T', new TilePos(20, 25), Species.Rex, brain, Direction.North);
        var human = sim.AddActor("You", '@', new TilePos(20, 6), Species.Human, new HoldStillController());

        var status = EscapeCheck.Evaluate(sim, rex, brain, human);

        Assert.True(status.OutOfReach);
        Assert.True(status.Escaped);
    }
}

/// <summary>Vehicles: found from cover, damaged by bites, shoved with whoever is inside.</summary>
public class VehicleTests
{
    [Fact]
    public void Vehicles_AreFoundFromCover_WithTheTrackUnderThem()
    {
        var map = new WorldMap(12, 4);
        for (int x = 0; x < 12; x++)
            for (int y = 0; y < 4; y++)
                map.Cover[new TilePos(x, y)] = CoverType.Track;
        foreach (var (x, y) in new[] { (1, 1), (2, 1), (1, 2), (2, 2), (8, 1), (9, 1) })
            map.Cover[new TilePos(x, y)] = CoverType.Vehicle;

        var sim = new Simulation(map, seed: 1);

        Assert.Equal(2, sim.Vehicles.Count);
        Assert.Equal("vehicle 1", sim.Vehicles[0].Name);
        Assert.Equal(4, sim.Vehicles[0].Tiles.Count);
        Assert.Equal(new TilePos(8, 1), sim.Vehicles[1].Anchor);
        Assert.Same(sim.Vehicles[1], sim.VehicleAt(new TilePos(9, 1)));
        Assert.Null(sim.VehicleAt(new TilePos(5, 1)));
    }

    /// <summary>A Rex facing south whose jaws cover x 9-11, y 12-15; a 3×2 vehicle at x 9-11, y 13-14 with Pete inside.</summary>
    private static (Simulation Sim, PlayerController Drive, Actor Rex, Actor Pete, Vehicle Vehicle) BiteSetup()
    {
        var map = new WorldMap(30, 30);
        for (int x = 9; x <= 11; x++)
        {
            map.Cover[new TilePos(x, 13)] = CoverType.Vehicle;
            map.Cover[new TilePos(x, 14)] = CoverType.Vehicle;
        }
        var sim = new Simulation(map, seed: 1);
        var drive = new PlayerController();
        var rex = sim.AddActor("Rex", 'T', new TilePos(10, 10), Species.Rex, drive, Direction.South);
        var pete = sim.AddActor("Pete", 'h', new TilePos(10, 14), Species.Human, new HoldStillController());
        sim.RunUntilInputNeeded();
        return (sim, drive, rex, pete, Assert.Single(sim.Vehicles));
    }

    [Fact]
    public void Bites_DamageAVehicle_ButCantReachInside_UntilItsBreached()
    {
        var (sim, drive, rex, pete, vehicle) = BiteSetup();
        var bitten = new List<Actor>();
        sim.ActorBitten += (_, victim) => bitten.Add(victim);

        for (int i = 0; i < 5; i++)
        {
            drive.Pending = new BiteAction();
            sim.RunUntilInputNeeded();
        }
        Assert.Empty(bitten);
        Assert.True(vehicle.Breached);                     // 5 bites × 20
        Assert.Same(vehicle, Assert.Single(rex.LastBite!.VehiclesBitten));
        Assert.Contains(sim.RecentNoises, n => n.Kind == NoiseKinds.MetalTear);

        drive.Pending = new BiteAction();
        sim.RunUntilInputNeeded();
        Assert.Equal(new[] { pete }, bitten);
    }

    [Fact]
    public void Shove_CarriesOccupantsAndHeadlights()
    {
        var map = new WorldMap(20, 20);
        for (int x = 8; x <= 12; x++)
            map.Cover[new TilePos(x, 10)] = CoverType.Vehicle;
        var sim = new Simulation(map, seed: 1);
        sim.AddLight(LightSource.Headlight(new TilePos(12, 10), Direction.East));
        var drive = new PlayerController();
        var rex = sim.AddActor("Rex", 'T', new TilePos(10, 12), Species.Rex, drive, Direction.North);
        var pete = sim.AddActor("Pete", 'h', new TilePos(9, 10), Species.Human, new HoldStillController());
        var vehicle = Assert.Single(sim.Vehicles);
        sim.RunUntilInputNeeded();

        drive.Pending = new MoveAction(Direction.North, Shove: true);
        sim.RunUntilInputNeeded();

        Assert.Equal(new TilePos(10, 11), rex.Position);
        Assert.Equal(new TilePos(9, 9), pete.Position);
        Assert.Same(pete, sim.ActorAt(new TilePos(9, 9)));
        Assert.Same(rex, sim.ActorAt(new TilePos(9, 10))); // the Rex stepped into the space
        Assert.Equal(new[] { 0 }, vehicle.Lights);
        Assert.Equal(new TilePos(12, 9), sim.Lights[0].Position);
    }
}
