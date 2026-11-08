using BlueHarvest.Sim.Actors;
using BlueHarvest.Sim.Perception;
using BlueHarvest.Sim.World;
using Xunit;

namespace BlueHarvest.Sim.Tests;

public class BiteTests
{
    [Fact]
    public void BiteZone_IsAroundTheHead_NotTheCore()
    {
        var sim = new Simulation(new WorldMap(30, 30), seed: 1);
        var rex = sim.AddActor("Rex", 'T', new TilePos(10, 10), Species.Rex, new PlayerController(), Direction.South);

        var zone = sim.BiteZone(rex);

        Assert.Equal(12, zone.Count); // x 9-11, y 12-15
        Assert.All(zone, p => Assert.InRange(p.X, 9, 11));
        Assert.All(zone, p => Assert.InRange(p.Y, 12, 15));
        Assert.Contains(new TilePos(10, 14), zone);       // the head tip
        Assert.DoesNotContain(new TilePos(10, 11), zone); // its own core
    }

    [Fact]
    public void Bite_HitsWhoeverIsInReach_AndTheyScream()
    {
        var sim = new Simulation(new WorldMap(30, 30), seed: 1);
        var drive = new PlayerController();
        var rex = sim.AddActor("Rex", 'T', new TilePos(10, 10), Species.Rex, drive, Direction.South);
        var human = sim.AddActor("Pete", 'h', new TilePos(11, 14), Species.Human, new HoldStillController());
        var bitten = new List<(Actor, Actor)>();
        sim.ActorBitten += (biter, victim) => bitten.Add((biter, victim));
        sim.RunUntilInputNeeded();

        drive.Pending = new BiteAction();
        sim.RunUntilInputNeeded();

        Assert.Equal(new[] { (rex, human) }, bitten);
        Assert.Equal(80, rex.NextActTick);
        var bite = Assert.IsType<BiteResult>(rex.LastBite);
        Assert.Equal(human, Assert.Single(bite.Hits).Victim);
        Assert.False(bite.Investigative);
        Assert.Contains(sim.RecentNoises, n => n.Kind == NoiseKinds.Scream && n.ActorId == human.Id);
        Assert.Contains(sim.RecentNoises, n => n.Kind == NoiseKinds.Jaws && n.ActorId == rex.Id);
    }

    [Fact]
    public void Bite_OnAVehicle_Crunches_AndMissesWhoeverIsOutOfReach()
    {
        var map = new WorldMap(30, 30);
        for (int x = 8; x <= 12; x++)
            map.Cover[new TilePos(x, 14)] = CoverType.Vehicle;
        var sim = new Simulation(map, seed: 1);
        var drive = new PlayerController();
        var rex = sim.AddActor("Rex", 'T', new TilePos(10, 10), Species.Rex, drive, Direction.South);
        var human = sim.AddActor("Marisol", 'h', new TilePos(12, 14), Species.Human, new HoldStillController());
        sim.RunUntilInputNeeded();

        drive.Pending = new BiteAction(Investigative: true);
        sim.RunUntilInputNeeded();

        var bite = Assert.IsType<BiteResult>(rex.LastBite);
        Assert.True(bite.Investigative);
        Assert.Empty(bite.Hits);
        Assert.Equal(new[] { new TilePos(9, 14), new TilePos(10, 14), new TilePos(11, 14) },
            bite.CoverBitten.OrderBy(p => p.X));
        Assert.Contains(sim.RecentNoises, n => n.Kind == "crunching metal");
        Assert.DoesNotContain(sim.RecentNoises, n => n.ActorId == human.Id);
    }

    [Fact]
    public void TailStrike_MakesTheVictimScream()
    {
        var sim = new Simulation(new WorldMap(40, 40), seed: 1);
        var drive = new PlayerController();
        sim.AddActor("Rex", 'T', new TilePos(15, 15), Species.Rex, drive, Direction.North);
        var human = sim.AddActor("Pete", 'h', new TilePos(13, 21), Species.Human, new HoldStillController());
        sim.RunUntilInputNeeded();

        drive.Pending = new MoveAction(Direction.East); // swings the tail through (13,21)
        sim.RunUntilInputNeeded();

        Assert.Contains(sim.RecentNoises, n => n.Kind == NoiseKinds.Scream && n.ActorId == human.Id);
    }
}
