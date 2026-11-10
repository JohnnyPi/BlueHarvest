using BlueHarvest.Sim.World.Generation;
using Xunit;

namespace BlueHarvest.Sim.Tests;

/// <summary>
/// Stage 3 of the world-generator port: climate, biomes and regions, checked against the island lab
/// (tools/island-lab/golden-climate.js). If IslandShapeTests fail too, fix those first.
/// </summary>
public class IslandClimateTests
{
    public sealed record Expected(int RegionLabels, ulong Fields, ulong Biomes, ulong Regions);

    // node tools/island-lab/golden-climate.js
    private static readonly Dictionary<string, Expected> Golden = new()
    {
        ["Default"] = new(8, 0x12C012BF41711B90UL, 0x5C0F2A6B9D32381DUL, 0xB59CFCBD4929C3AEUL),
        ["Seed12"] = new(10, 0xE60AB1813ADA4C97UL, 0x1AD774AE01434A26UL, 0xAB8F6CDFE5AD0A8BUL),
        ["SmallNoCliffs"] = new(7, 0xD9BA286A2CF5382EUL, 0x3CE159D9D39C92D8UL, 0xE12B87DE97BC7E3DUL),
        ["CoolWet"] = new(8, 0x71AC6133EE50AE21UL, 0x36BB0523079F3091UL, 0x3559AE9B363B5DD0UL),
    };

    public static IEnumerable<object[]> CaseNames => Golden.Keys.Select(k => new object[] { k });

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void ClimateFields_MatchReference(string name)
    {
        IslandClimate c = TestIslands.Climate(name);
        ulong got = new GenHasher().Ds(c.Temperature).Ds(c.Fog).Ds(c.Wetness).Ds(c.Crest).Ds(c.SlopeDeg).Is(c.NearLake)
            .D(c.TempMin).D(c.TempMax).Value;
        Assert.Equal(Golden[name].Fields, got);
    }

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void Biomes_MatchReference(string name)
    {
        IslandClimate c = TestIslands.Climate(name);
        Assert.Equal(Golden[name].Biomes, new GenHasher().Is(c.Biomes).Ds(c.BiomeKm2).Value);
    }

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void Regions_MatchReference(string name)
    {
        IslandClimate c = TestIslands.Climate(name);
        var h = new GenHasher().Is(c.Regions).Ds(c.RegionKm2).I(c.RegionLabels.Count);
        foreach (RegionLabel l in c.RegionLabels) h.I((int)l.Region).D(l.X).D(l.Y).D(l.Km2);
        Assert.Equal(Golden[name].RegionLabels, c.RegionLabels.Count);
        Assert.Equal(Golden[name].Regions, h.Value);
    }

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void Biomes_CoverOnlyLandAndSea(string name)
    {
        IslandShape w = TestIslands.Shape(name);
        IslandClimate c = TestIslands.Climate(name);
        for (int i = 0; i < w.N; i++)
        {
            bool sea = w.Land[i] == 0;
            Assert.Equal(sea, c.Biomes[i] == (byte)Biome.Ocean);
            Assert.Equal(sea, c.Regions[i] == (byte)Region.Sea);
        }
        Assert.True(c.BiomeKm2[(int)Biome.Cloud] > 0, "every island should have some cloud forest");
    }

    [Fact]
    public void Classify_FollowsItsPriorities()
    {
        // young lava beats everything; then cliffs; then beaches on sandy coast
        Assert.Equal(Biome.Lava, IslandClimate.Classify(100, 3000, 22, 1, 1, 50, 0.9, 50, true, 1, 0));
        Assert.Equal(Biome.Cliff, IslandClimate.Classify(100, 3000, 22, 1, 1, 50, 0, 50, true, 1, 0));
        Assert.Equal(Biome.Strand, IslandClimate.Classify(3, 3000, 24, 0, 0, 1, 0, 50, true, 0, 0));
        // aridity bands at 24 °C: ai = rain / 1300
        Assert.Equal(Biome.Wet, IslandClimate.Classify(100, 4000, 24, 0, 0, 5, 0, 900, false, 0, 0));
        Assert.Equal(Biome.Moist, IslandClimate.Classify(100, 2500, 24, 0, 0, 5, 0, 900, false, 0, 0));
        Assert.Equal(Biome.Mesic, IslandClimate.Classify(100, 1500, 24, 0, 0, 5, 0, 900, false, 0, 0));
        Assert.Equal(Biome.Grassland, IslandClimate.Classify(100, 1100, 24, 0, 0, 5, 0, 900, false, 0, 0));
        Assert.Equal(Biome.DryScrub, IslandClimate.Classify(100, 600, 24, 0, 0, 5, 0, 900, false, 0, 0));
    }
}
