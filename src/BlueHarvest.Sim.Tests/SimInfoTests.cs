using BlueHarvest.Sim;
using Xunit;

namespace BlueHarvest.Sim.Tests;

public class SimInfoTests
{
    [Fact]
    public void Version_IsSet()
    {
        Assert.Equal("0.0.1", SimInfo.Version);
    }
}