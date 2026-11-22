using BlueHarvest.Content;
using BlueHarvest.Sim.Core;
using BlueHarvest.Sim.World;

namespace BlueHarvest.Sim.Tests.ContentLibrary;

public class ContentIdTests
{
    [Theory]
    [InlineData("core:ITM-CRF-004")]
    [InlineData("core:first-aid-kit")]
    [InlineData("core:battery-aa")]
    [InlineData("core:magazine-9mm")]
    [InlineData("core:MEN-01-N01")]
    [InlineData("island-mysteries:RUIN-01")]
    [InlineData("jdoe.raptor-pack:raptor-call")]
    public void TryParse_AcceptsTheContractForms(string text)
    {
        Assert.True(ContentId.TryParse(text, out ContentId id));
        Assert.Equal(text, id.Value);
        Assert.Equal(text[..text.IndexOf(':')], id.Namespace);
    }

    [Theory]
    [InlineData("ITM-CRF-004")]       // not qualified
    [InlineData("Core:knife")]        // namespace not lower case
    [InlineData("core:Knife")]        // neither kebab case nor a catalog ID
    [InlineData("core:ITM_CRF_004")]  // underscores
    [InlineData("core:knife-")]       // trailing hyphen
    [InlineData("core:I-1")]          // catalog prefixes are 2 to 4 letters
    [InlineData("core::knife")]
    [InlineData(":knife")]
    [InlineData("core:")]
    [InlineData("core:knife\n")]      // a trailing newline never slips through
    [InlineData("a:b:c")]
    [InlineData("1core:knife")]       // a namespace starts with a letter
    public void TryParse_RejectsEverythingElse(string text)
    {
        Assert.False(ContentId.TryParse(text, out _));
    }

    [Fact]
    public void TryParse_RejectsIdsOver64Characters()
    {
        string local = "item-" + new string('a', 54);
        Assert.True(ContentId.TryParse("core:" + local, out _)); // 64
        Assert.False(ContentId.TryParse("core:" + local + "a", out _)); // 65
    }

    [Fact]
    public void TryResolve_QualifiesBareIdsWithThePackNamespace()
    {
        Assert.True(ContentId.TryResolve("ITM-CRF-004", "core", out ContentId bare));
        Assert.Equal("core:ITM-CRF-004", bare.Value);
        Assert.True(ContentId.TryResolve("other:knife", "core", out ContentId qualified));
        Assert.Equal("other:knife", qualified.Value);
        Assert.Equal("knife", qualified.Local);
    }

    [Fact]
    public void ContentTime_MatchesTheSimClock()
    {
        // The content library can't reference the sim, so it keeps its own copy of the contract's tick rates.
        Assert.Equal(Ticks.PerSecond, ContentTime.TicksPerActionSecond);
        Assert.Equal(WorldClock.TicksPerGameMinute, ContentTime.TicksPerGameMinute);
    }
}
