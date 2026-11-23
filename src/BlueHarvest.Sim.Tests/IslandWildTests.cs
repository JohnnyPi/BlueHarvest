using BlueHarvest.Sim.World.Generation;
using Xunit;

namespace BlueHarvest.Sim.Tests;

/// <summary>
/// Stage 4d of the world-generator port: lava tubes and sea caves, wrecks, ruins and the wildlife maps,
/// checked against the island lab (tools/island-lab/golden-wild.js).
/// </summary>
public class IslandWildTests
{
    public sealed record Expected(int Caves, int Wrecks, int Ruins, double Compies, ulong CaveGroup, ulong WreckGroup, ulong RuinGroup, ulong FaunaGroup);

    // node tools/island-lab/golden-wild.js
    private static readonly Dictionary<string, Expected> Golden = new()
    {
        ["Default"] = new(16, 22, 28, 55094, 0x91839A1F54B458C8UL, 0x93D5F6E2BF6EFBD0UL, 0xF8A038D4D3F96D13UL, 0xBBC8B21CBBF488E0UL),
        ["Seed12"] = new(9, 12, 16, 28275, 0x7F96CB3EB730BF18UL, 0xBE06CFE9987A68BDUL, 0xD43B90A143C44DE3UL, 0x4D9077FE87AED3F3UL),
        ["SmallNoCliffs"] = new(4, 5, 7, 14660, 0xB6D47FAA46B003C9UL, 0x8906F266BFB88A72UL, 0xFF5644FAC9F9834CUL, 0xBDE2D8A8272FD3FAUL),
        ["CoolWet"] = new(6, 7, 10, 22592, 0xF13813CD4F909A06UL, 0x2E82B68A05A40EE4UL, 0x1E31371E7044F481UL, 0x39C2E5DDC37F0733UL),
    };

    public static IEnumerable<object[]> CaseNames => Golden.Keys.Select(k => new object[] { k });

    private static GenHasher Points(GenHasher h, List<(double X, double Y)> a)
    {
        h.I(a.Count);
        foreach ((double x, double y) in a) h.D(x).D(y);
        return h;
    }

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void Counts_MatchReference(string name)
    {
        IslandWorks w = TestIslands.Works(name);
        Expected e = Golden[name];
        Assert.Equal(e.Caves, w.Caves.Count);
        Assert.Equal(e.Wrecks, w.Wrecks.Count);
        Assert.Equal(e.Ruins, w.Ruins.Count);
        Assert.Equal(e.Compies, w.Fauna[0].Population);
    }

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void Caves_MatchReference(string name)
    {
        IslandWorks w = TestIslands.Works(name);
        var h = new GenHasher().I(w.Caves.Count);
        foreach (Cave c in w.Caves)
        {
            h.I(c.Id).I(c.Kind == CaveKind.SeaCave ? 1 : 0).S(c.Name).Ds(c.Xs.ToArray()).Ds(c.Ys.ToArray()).D(c.Depth).D(c.LengthKm);
            Points(h, c.Mouths);
            Points(h, c.Skylights);
        }
        Assert.Equal(Golden[name].CaveGroup, h.Value);
    }

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void Wrecks_MatchReference(string name)
    {
        IslandWorks w = TestIslands.Works(name);
        var h = new GenHasher().I(w.Wrecks.Count);
        foreach (Wreck q in w.Wrecks)
            h.I(q.Id).D(q.X).D(q.Y).S(q.Kind).S(q.Era).D(q.Length).D(q.Heading).I(q.OnReef ? 1 : 0).I(q.Beached ? 1 : 0).I(q.Inland ? 1 : 0).S(q.Name);
        Assert.Equal(Golden[name].WreckGroup, h.Value);
    }

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void Ruins_MatchReference(string name)
    {
        IslandWorks w = TestIslands.Works(name);
        var h = new GenHasher().I(w.Ruins.Count);
        foreach (Ruin q in w.Ruins) h.I(q.Id).S(q.Kind).D(q.X).D(q.Y).D(q.R).S(q.Era).S(q.Name).D(q.Rot).S(q.SatelliteOf ?? "");
        Assert.Equal(Golden[name].RuinGroup, h.Value);
    }

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void Wildlife_MatchesReference(string name)
    {
        IslandWorks w = TestIslands.Works(name);
        var h = new GenHasher().I(w.Fauna.Count);
        foreach (FaunaMap f in w.Fauna) h.S(f.Kind.Key).Is(f.Density).D(f.Population);
        Assert.Equal(Golden[name].FaunaGroup, h.Value);
    }

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void Wildlife_StaysOutOfPaddocksAndTheSea(string name)
    {
        IslandShape s = TestIslands.Shape(name);
        IslandWorks w = TestIslands.Works(name);
        Assert.Equal(12, w.Fauna.Count);
        foreach (FaunaMap f in w.Fauna)
        {
            Assert.True(f.Population > 0, f.Kind.Name);
            for (int i = 0; i < s.N; i++)
                if (s.Land[i] == 0 || w.PaddockCell[i] >= 0) Assert.Equal(0, f.Density[i]);
        }
    }

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void EveryCave_HasAWayIn(string name)
    {
        IslandWorks w = TestIslands.Works(name);
        foreach (Cave c in w.Caves) Assert.NotEmpty(c.Mouths);
    }
}
