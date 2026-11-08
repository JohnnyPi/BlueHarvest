using BlueHarvest.Sim.Actors;
using BlueHarvest.Sim.World;
using Xunit;

namespace BlueHarvest.Sim.Tests;

public class HeightMapTests
{
    [Fact]
    public void FromRows_EachDigitIsHalfAMeter()
    {
        var map = HeightMap.FromRows("04");
        Assert.Equal(0, map[new TilePos(0, 0)]);
        Assert.Equal(20, map[new TilePos(1, 0)]);
    }

    [Fact]
    public void FromRows_RejectsRaggedRows()
    {
        Assert.Throws<FormatException>(() => HeightMap.FromRows("00", "0"));
    }

    [Fact]
    public void FromRows_LettersContinueAfterNine()
    {
        var map = HeightMap.FromRows("9ag");
        Assert.Equal(45, map[new TilePos(0, 0)]);  // 9 levels = 4.5 m
        Assert.Equal(50, map[new TilePos(1, 0)]);  // 'a' = 10 levels = 5.0 m
        Assert.Equal(80, map[new TilePos(2, 0)]);  // 'g' = 16 levels = 8.0 m
    }

    [Fact]
    public void FromRows_RejectsOtherCharacters()
    {
        Assert.Throws<FormatException>(() => HeightMap.FromRows("0!"));
        Assert.Throws<FormatException>(() => HeightMap.FromRows("0A"));
    }

    [Fact]
    public void Human_CanStepHalfAMeter_ButNotClimbTwoMeters()
    {
        Assert.True(MovementProfile.Human.CanTraverse(5));
        Assert.False(MovementProfile.Human.CanTraverse(20));
    }

    [Fact]
    public void Human_CanDropTwoMeters_ButNotMore()
    {
        Assert.True(MovementProfile.Human.CanTraverse(-20));
        Assert.False(MovementProfile.Human.CanTraverse(-25));
    }
}
