using BlueHarvest.Sim.Actors;
using BlueHarvest.Sim.Core;
using BlueHarvest.Sim.Demo;
using BlueHarvest.Sim.World;
using Xunit;

namespace BlueHarvest.Sim.Tests;

public class SimulationTests
{
    private static Simulation Scripted(string[] rows, TilePos start, params ActorAction[] script)
    {
        var sim = new Simulation(HeightMap.FromRows(rows), seed: 1);
        sim.AddActor("Tester", '@', start, Species.Human, new ScriptedController(script));
        return sim;
    }

    [Fact]
    public void Moves_CostTheirTicks()
    {
        var sim = Scripted(new[] { "000", "000" }, new TilePos(0, 0),
            new MoveAction(Direction.East),
            new MoveAction(Direction.East),
            new MoveAction(Direction.South));

        sim.Step();
        sim.Step();
        sim.Step();

        var actor = sim.Actors[0];
        Assert.Equal(new TilePos(2, 1), actor.Position);
        Assert.Equal(210, actor.NextActTick); // three flat steps of 70 ticks
    }

    [Fact]
    public void Uphill_CostsExtra()
    {
        var sim = Scripted(new[] { "01" }, new TilePos(0, 0), new MoveAction(Direction.East));
        sim.Step();
        Assert.Equal(100, sim.Actors[0].NextActTick); // 70 flat + 5 dm uphill × 6
    }

    [Fact]
    public void Human_IsBlockedByCliff_AndLosesABump()
    {
        // 2.5 m: past the 2 m a human can climb (step 8b), so it's a cliff.
        var sim = Scripted(new[] { "05" }, new TilePos(0, 0), new MoveAction(Direction.East));
        sim.Step();

        var actor = sim.Actors[0];
        Assert.Equal(new TilePos(0, 0), actor.Position);
        Assert.Equal(Simulation.BumpTicks, actor.NextActTick);
    }

    [Fact]
    public void SameSeed_ReplaysIdentically()
    {
        var a = DemoWorld.Create(12345);
        var b = DemoWorld.Create(12345);
        a.RunUntil(Ticks.FromSeconds(60));
        b.RunUntil(Ticks.FromSeconds(60));

        Assert.True(a.ActionsResolved > 0);
        Assert.Equal(a.ComputeStateHash(), b.ComputeStateHash());
    }

    [Fact]
    public void DifferentSeed_Diverges()
    {
        var a = DemoWorld.Create(1);
        var b = DemoWorld.Create(2);
        a.RunUntil(Ticks.FromSeconds(60));
        b.RunUntil(Ticks.FromSeconds(60));

        Assert.NotEqual(a.ComputeStateHash(), b.ComputeStateHash());
    }

    [Fact]
    public void ActorsNeverShareATile()
    {
        var sim = DemoWorld.Create(777);
        for (int i = 0; i < 5000; i++)
        {
            sim.Step();
            var positions = sim.Actors.Select(a => a.Position).ToList();
            Assert.Equal(positions.Count, positions.Distinct().Count());
        }
    }
}
