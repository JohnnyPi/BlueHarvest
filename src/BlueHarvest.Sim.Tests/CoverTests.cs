using BlueHarvest.Sim.Actors;
using BlueHarvest.Sim.World;
using Xunit;

namespace BlueHarvest.Sim.Tests;

public class CoverTests
{
    private static (Simulation Sim, Actor Actor) OneTile(string coverRow, Species species)
    {
        var map = new WorldMap(coverRow.Length, 1);
        for (int x = 0; x < coverRow.Length; x++)
            map.Cover[new TilePos(x, 0)] = Covers.FromFileChar(coverRow[x])!.Type;

        var sim = new Simulation(map, seed: 1);
        var actor = sim.AddActor("Tester", '@', new TilePos(0, 0), species, new PlayerController());
        return (sim, actor);
    }

    [Fact]
    public void Fence_Blocks_WithReason()
    {
        var (sim, actor) = OneTile(".F", Species.Human);
        Assert.Equal("the electric fence is in the way", sim.MoveBlockReason(actor, Direction.East));
    }

    [Fact]
    public void DenseJungle_CostsMore()
    {
        var (sim, actor) = OneTile(".&", Species.Human);
        ((PlayerController)actor.Controller).Pending = new MoveAction(Direction.East);
        sim.RunUntilInputNeeded();
        Assert.Equal(126, actor.NextActTick); // 70 ticks × 180%
    }

    [Fact]
    public void Human_WalksThroughDoorways()
    {
        var (sim, human) = OneTile(".+", Species.Human);
        Assert.Null(sim.MoveBlockReason(human, Direction.East)); // the Rex case is in BodyTests
    }

    [Fact]
    public void BreakRules_MatchTheDesign()
    {
        int rex = Species.Rex.Force;
        Assert.False(Covers.Get(CoverType.SparseJungle).StopsLargeBody(rex));
        Assert.True(Covers.Get(CoverType.DenseJungle).StopsLargeBody(rex));
        Assert.False(Covers.Get(CoverType.Fence).StopsLargeBody(rex));
        Assert.True(Covers.Get(CoverType.Wall).StopsLargeBody(rex));
        Assert.True(Covers.Get(CoverType.Vehicle).StopsLargeBody(rex));
        Assert.True(Covers.Get(CoverType.Door).StopsLargeBody(rex));
    }

    [Fact]
    public void EveryCover_HasAUniqueFileChar()
    {
        var chars = Covers.All.Select(c => c.FileChar).ToList();
        Assert.Equal(chars.Count, chars.Distinct().Count());
        Assert.Equal(Enum.GetValues<CoverType>().Length, Covers.All.Count);
        for (int i = 0; i < Covers.All.Count; i++)
            Assert.Equal((CoverType)i, Covers.All[i].Type);
    }
}
