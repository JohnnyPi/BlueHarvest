using BlueHarvest.Sim.World.Generation;
using Xunit;

namespace BlueHarvest.Sim.Tests;

/// <summary>
/// Stage 4a of the world-generator port: facilities, ley lines, species paddocks, power and the compounds'
/// buildings, checked against the island lab (tools/island-lab/golden-sites.js).
/// If IslandShapeTests or IslandClimateTests fail too, fix those first.
/// </summary>
public class IslandWorksTests
{
    public sealed record Expected(int Sites, int Paddocks, int Power, int Labels, int Prims,
        ulong SiteGroup, ulong LeyGroup, ulong PaddockGroup, ulong PowerGroup, ulong CompoundGroup);

    // node tools/island-lab/golden-sites.js
    private static readonly Dictionary<string, Expected> Golden = new()
    {
        ["Default"] = new(21, 4, 23, 302, 812,
            0x4FD6FED625983BD0UL, 0x0439C111FA8CEB5AUL, 0xC8BDDE2CAA1AE2BAUL, 0x0976288DFE7BE5F5UL, 0x427FD7FE71FCF63EUL),
        ["Seed12"] = new(21, 4, 23, 302, 811,
            0x4BA3EDE6CB8BB8A6UL, 0xA55B550FCBDA5E5AUL, 0x52BABED785B57480UL, 0xACF6B9FD1B6E228EUL, 0xC6CDC958FC2C7979UL),
        ["SmallNoCliffs"] = new(21, 4, 23, 302, 812,
            0xA4690C1D8F99CDD0UL, 0x46B0CC3BEE92FCF1UL, 0x1E077D0656E3E9E2UL, 0x70D4BF59C6C65BF2UL, 0xF4BE4AB1E733BDDAUL),
        ["CoolWet"] = new(21, 4, 23, 302, 802,
            0x127DB88977C8FA64UL, 0xB39A5856A850EDDCUL, 0x0FF09CAF1F23968BUL, 0x239B02CA69F9C993UL, 0xC5AB7CD751678834UL),
    };

    public static IEnumerable<object[]> CaseNames => Golden.Keys.Select(k => new object[] { k });

    private static int FacilityIndex(string key) => Array.FindIndex(IslandWorks.Facilities, f => f.Key == key);

    private static int SpeciesIndex(string key) => Array.FindIndex(IslandWorks.PaddockSpeciesList, s => s.Key == key);

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void Counts_MatchReference(string name)
    {
        IslandWorks w = TestIslands.Works(name);
        Expected e = Golden[name];
        Assert.Equal(e.Sites, w.Sites.Count);
        Assert.Equal(e.Paddocks, w.Paddocks.Count);
        Assert.Equal(e.Power, w.Power.Count);
        // as they stood when the compounds were finished; transit adds more later
        Assert.Equal(e.Labels, w.CompoundLabelCount);
        Assert.Equal(e.Prims, w.CompoundPrims.Count);
    }

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void Sites_MatchReference(string name)
    {
        IslandWorks w = TestIslands.Works(name);
        var h = new GenHasher().D(w.CenterX).D(w.CenterY).I(w.Sites.Count);
        foreach (Site t in w.Sites)
        {
            h.I(t.Id).I(FacilityIndex(t.Kind)).D(t.X).D(t.Y).D(t.R).D(t.H).I((int)t.Region).S(t.Name).S(t.Owner).I(t.Major ? 1 : 0);
            if (t.Dir is { } dir) h.I(1).D(dir.X).D(dir.Y); else h.I(0);
            if (t.Runway is { } rw) h.I(1).D(rw.X1).D(rw.Y1).D(rw.X2).D(rw.Y2).D(rw.H); else h.I(0);
        }
        Assert.Equal(Golden[name].SiteGroup, h.Value);
    }

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void LeyLines_MatchReference(string name)
    {
        IslandWorks w = TestIslands.Works(name);
        var h = new GenHasher().I(w.LeyLines.Count);
        foreach (LeyLine l in w.LeyLines) h.D(l.X1).D(l.Y1).D(l.X2).D(l.Y2);
        Assert.Equal(Golden[name].LeyGroup, h.Value);
    }

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void Paddocks_MatchReference(string name)
    {
        IslandWorks w = TestIslands.Works(name);
        var h = new GenHasher().I(w.Paddocks.Count);
        foreach (Paddock q in w.Paddocks)
        {
            h.I(q.Id).I(SpeciesIndex(q.Key)).D(q.X).D(q.Y).Ds(q.Poly).D(q.Km2).I((int)q.Biome)
                .D(q.Gate.X).D(q.Gate.Y).D(q.Out.X).D(q.Out.Y).I(q.HasLake ? 1 : 0);
            if (q.Wallow is { } wl) h.I(1).D(wl.X).D(wl.Y); else h.I(0);
            if (q.Inner != null) h.Ds(q.Inner); else h.I(-1);
        }
        h.I(w.PaddockCell.Length);
        foreach (short v in w.PaddockCell) h.I(v);
        Assert.Equal(Golden[name].PaddockGroup, h.Value);
    }

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void Power_MatchesReference(string name)
    {
        IslandWorks w = TestIslands.Works(name);
        var h = new GenHasher().I(w.Power.Count);
        foreach (PowerLine l in w.Power) h.D(l.X1).D(l.Y1).D(l.X2).D(l.Y2).S(l.From).S(l.To);
        h.D(w.PowerKm);
        Assert.Equal(Golden[name].PowerGroup, h.Value);
    }

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void Compounds_MatchReference(string name)
    {
        IslandWorks w = TestIslands.Works(name);
        var h = new GenHasher().I(w.CompoundLabelCount);
        foreach (string s in w.Labels.Take(w.CompoundLabelCount)) h.S(s);
        h.I(w.CompoundPrims.Count);
        foreach (Prim q in w.Prims.InOrder(w.CompoundPrims)) HashPrim(h, q);
        Assert.Equal(Golden[name].CompoundGroup, h.Value);
    }

    internal static void HashPrim(GenHasher h, Prim q)
    {
        h.I((int)q.Kind);
        switch (q.Kind)
        {
            case PrimKind.Building:
                h.D(q.X0).D(q.Y0).D(q.X1).D(q.Y1).I(q.Label).I(q.Rooms!.Count);
                foreach (Room r in q.Rooms) h.D(r.X0).D(r.Y0).D(r.X1).D(r.Y1).I(r.Label);
                h.I(q.Doors!.Count);
                foreach ((int x, int y) in q.Doors) h.D(x).D(y);
                break;
            case PrimKind.Furniture:
                h.D(q.X0).D(q.Y0).D(q.X1).D(q.Y1).I((int)q.Cover).I(q.Label);
                break;
            case PrimKind.Circle:
                h.D(q.X).D(q.Y).D(q.R).I((int)q.Cover).I(q.Ring ? 1 : 0).I(q.Fill is { } f ? (int)f : -1).I(q.Door ? 1 : 0).I(q.Label);
                break;
            case PrimKind.Rect:
                h.D(q.X0).D(q.Y0).D(q.X1).D(q.Y1).I((int)q.Cover).I(q.Fence ? 1 : 0).I(q.Label);
                break;
            case PrimKind.Tower:
                h.D(q.X).D(q.Y).I(q.Label);
                break;
            case PrimKind.Line:
                h.D(q.Ax).D(q.Ay).D(q.Bx).D(q.By).D(q.W).I((int)q.Cover).D(q.Depth).I(q.Label);
                break;
            case PrimKind.Pen:
                h.D(q.X0).D(q.Y0).D(q.X1).D(q.Y1).I(q.Label);
                break;
        }
    }

    [Fact]
    public void GeneticsComplex_HasTheRoomsItNeeds()
    {
        IslandWorks w = TestIslands.Works("Default");
        Site gen = Assert.Single(w.Sites, t => t.Kind == "genetics");
        string[] need = ["Sequencing lab", "Server hall", "Cryo vault", "Bunk room 1", "Kitchen", "Showers", "Security office"];
        foreach (string room in need)
            Assert.Contains(w.Labels, l => l.StartsWith(gen.Name + " · ", StringComparison.Ordinal) && l.EndsWith(" · " + room, StringComparison.Ordinal));
    }

    [Fact]
    public void Paddocks_CellsMatchTheirArea()
    {
        IslandWorks w = TestIslands.Works("Default");
        Assert.Equal(4, w.Paddocks.Count);
        var cells = new int[w.Paddocks.Count];
        foreach (short v in w.PaddockCell) if (v >= 0) cells[v]++;
        for (int k = 0; k < cells.Length; k++)
            Assert.Equal(w.Paddocks[k].Km2, cells[k] * TestIslands.Shape("Default").Cell * TestIslands.Shape("Default").Cell / 1e6, 9);
    }

    [Fact]
    public void Layout_FitsInsideItsRadius()
    {
        foreach (string kind in FacilityPrograms.Programs.Keys)
        {
            CompoundLayout l = FacilityPrograms.Layout(kind);
            foreach (LayoutItem it in l.Items)
            {
                Assert.True(Math.Abs(it.X) <= l.R && Math.Abs(it.Y) <= l.R, kind);
                Assert.True(Math.Abs(it.X + it.W) <= l.R && Math.Abs(it.Y + it.H) <= l.R, kind);
            }
        }
    }
}
