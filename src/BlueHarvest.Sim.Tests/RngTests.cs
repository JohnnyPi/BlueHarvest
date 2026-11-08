using BlueHarvest.Sim.Core;
using Xunit;

namespace BlueHarvest.Sim.Tests;

public class RngTests
{
    [Fact]
    public void SameSeed_GivesSameSequence()
    {
        var a = new Rng(42);
        var b = new Rng(42);
        for (int i = 0; i < 1000; i++)
            Assert.Equal(a.NextULong(), b.NextULong());
    }

    [Fact]
    public void DifferentSeeds_GiveDifferentSequences()
    {
        Assert.NotEqual(new Rng(1).NextULong(), new Rng(2).NextULong());
    }

    [Fact]
    public void NextInt_StaysInRange_AndHitsEveryValue()
    {
        var rng = new Rng(7);
        var seen = new bool[7];
        for (int i = 0; i < 10_000; i++)
        {
            int value = rng.NextInt(7);
            Assert.InRange(value, 0, 6);
            seen[value] = true;
        }
        Assert.DoesNotContain(false, seen);
    }

    [Fact]
    public void Fork_DoesNotDependOnParentDraws()
    {
        var parent = new Rng(99);
        ulong before = parent.Fork("ai").NextULong();
        parent.NextULong();
        parent.NextULong();
        ulong after = parent.Fork("ai").NextULong();
        Assert.Equal(before, after);
    }

    [Fact]
    public void Fork_StreamsDifferByName()
    {
        var parent = new Rng(99);
        Assert.NotEqual(parent.Fork("ai").NextULong(), parent.Fork("weather").NextULong());
    }
}
