using BlueHarvest.Sim.World.Generation;
using Xunit;

namespace BlueHarvest.Sim.Tests;

/// <summary>
/// Stage 4a of the world-generator port: facilities, ley lines, species paddocks, power and the compounds'
/// buildings, and the Crosswake Island site program (P4c: the authored ruins, ley routes through the Court,
/// outstations, seeded states, ADM's footprint), checked against the island lab (tools/island-lab/golden-sites.js),
/// then the program's own rules, checked by the tests themselves.
/// If IslandShapeTests or IslandClimateTests fail too, fix those first.
/// </summary>
public class IslandWorksTests
{
    public sealed record Expected(int Sites, int Paddocks, int Power, int Labels, int Prims,
        ulong SiteGroup, ulong LeyGroup, ulong PaddockGroup, ulong PowerGroup, ulong CompoundGroup,
        ulong ProgramGroup, int Ruins, int Lines, int Outstations, int Failures);

    // node tools/island-lab/golden-sites.js
    private static readonly Dictionary<string, Expected> Golden = new()
    {
        ["Default"] = new(25, 5, 29, 357, 888,
            0x15CBBBD35DD13530UL, 0xD8B66F9DF38E59C1UL, 0x97179EB24FB0721CUL, 0xE13E59E1E4324F31UL, 0x9798A99E4D565AA3UL,
            0x290CCE13D24E635FUL, 7, 4, 2, 0),
        ["Seed12"] = new(28, 5, 32, 405, 1024,
            0x6A195AB97CF8903DUL, 0x3979D427178E8E03UL, 0xD91F8E13F7CAE137UL, 0x610A25B3877732B5UL, 0x9766A0F0DC79D748UL,
            0x0DA03A6A208E5DDEUL, 7, 4, 5, 0),
        ["SmallNoCliffs"] = new(28, 5, 32, 405, 1019,
            0x3342BC2552B732CDUL, 0xBBDBAA9D13FB864EUL, 0x7FBD4BB7B365E69EUL, 0xCE98CEC954AD9E18UL, 0x6B3CECCD2368791EUL,
            0x8E2360BC63F56239UL, 6, 4, 5, 1),
        ["CoolWet"] = new(26, 5, 30, 373, 936,
            0xCAF4F4915437ECA1UL, 0xB3EBBA196D936675UL, 0x3421C8F7F34D9C2DUL, 0xF52BF27DBBA8F668UL, 0x0A67AC3B84B88680UL,
            0xE6B6130CA964F6A1UL, 7, 4, 3, 0),
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
            h.I(t.Id).I(FacilityIndex(t.Kind)).D(t.X).D(t.Y).D(t.R).D(t.H).I((int)t.Region).S(t.Name).S(t.Owner).I(t.Major ? 1 : 0)
                .S(t.Sector ?? "").S(t.Key);
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
        foreach (LeyLine l in w.LeyLines) h.D(l.X1).D(l.Y1).D(l.X2).D(l.Y2).S(l.Line);
        Assert.Equal(Golden[name].LeyGroup, h.Value);
    }

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void SiteProgram_MatchesReference(string name)
    {
        IslandWorks w = TestIslands.Works(name);
        SiteProgram p = w.Program;
        Expected e = Golden[name];
        Assert.Equal(e.Ruins, p.Ruins.Count(q => q.Placed));
        Assert.Equal(e.Lines, p.Lines.Count);
        Assert.Equal(e.Outstations, p.Outstations.Count);
        Assert.Equal(e.Failures, p.Failures.Count);
        var h = new GenHasher().D(p.Centre.X).D(p.Centre.Y).I(p.Ruins.Count);
        foreach (AuthoredRuin q in p.Ruins)
        {
            h.S(q.Id).I(q.Placed ? 1 : 0);
            if (q.Placed) h.D(q.X).D(q.Y).I(q.R).I(q.LowTide ? 1 : 0).D(q.CausewayM);
        }
        h.I(p.Lines.Count);
        foreach (LeyLineRoute l in p.Lines) h.S(l.Key).Ds(l.Xs).Ds(l.Ys).I(l.Through).I(l.Survey);
        h.I(p.OutstationCount).I(p.Outstations.Count);
        foreach (Outstation o in p.Outstations) h.I(o.Site).S(o.Sector).I(o.OnRole ? 1 : 0);
        foreach (SectorState st in p.States) h.S(st.Sector).S(st.State).I(st.HistorySeed);
        if (p.Adm is { } adm)
        {
            List<AdmPort> ports = adm.Ports.Where(q => q.Kind != "monorail lift").ToList(); // the lift is added after transit
            h.I(1).D(adm.X).D(adm.Y).D(adm.B1Z).I(adm.WetCells).I(adm.RiverCells).I(ports.Count);
            foreach (AdmPort q in ports) h.S(q.Kind).D(q.X).D(q.Y);
        }
        else h.I(0);
        h.I(p.Failures.Count);
        foreach (PlacementFailure f in p.Failures) h.S(f.Key);
        if (p.ReefFlat is { } rf) h.I(1).I(rf.Cells); else h.I(0);
        if (p.Floodplain is { } fp) h.I(1).I(fp.RiverCells).I(fp.Cells); else h.I(0);
        Assert.Equal(e.ProgramGroup, h.Value);
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
        Assert.Equal(5, w.Paddocks.Count);
        var cells = new int[w.Paddocks.Count];
        foreach (short v in w.PaddockCell) if (v >= 0) cells[v]++;
        for (int k = 0; k < cells.Length; k++)
            Assert.Equal(w.Paddocks[k].Km2, cells[k] * TestIslands.Shape("Default").Cell * TestIslands.Shape("Default").Cell / 1e6, 9);
    }

    // ---------- the site program's own rules (P4c) ----------

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void Sites_AreTheCrosswakeProgram_OwnedByThePowers_WithUniqueNamesAndKeys(string name)
    {
        IslandWorks w = TestIslands.Works(name);
        Assert.DoesNotContain(w.Sites, t => t.Kind is "control" or "nuclear" or "lab" or "ley");
        foreach (string kind in new[] { "court", "genetics", "raptors", "orgone", "orchard", "weather", "choir", "drowned", "geothermal", "yard" })
            Assert.Single(w.Sites, t => t.Kind == kind);
        var owners = IslandWorks.Powers.Values.Select(q => q.Name).ToHashSet();
        Assert.All(w.Sites, t => Assert.Contains(t.Owner, owners));
        // each sector's own sites belong to its power; ADM's to Central Administration
        foreach (Site t in w.Sites)
            Assert.Equal(IslandWorks.Powers[t.Sector ?? "ADM"].Name, t.Owner);
        // the naming fix (A2): no two sites share a name, however many of a kind point the same way
        Assert.Equal(w.Sites.Count, w.Sites.Select(t => t.Name).Distinct().Count());
        // stable keys (A3): unique, "site.<kind>:<k-th of its kind>", and found again by SiteIndex
        Assert.Equal(w.Sites.Count, w.Sites.Select(t => t.Key).Distinct().Count());
        foreach (Site t in w.Sites)
        {
            Assert.Equal("site." + t.Kind + ":" + w.Sites.Count(u => u.Kind == t.Kind && u.Id < t.Id), t.Key);
            Assert.Equal(t.Id, w.SiteIndex(t.Key));
        }
    }

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void AuthoredRuins_Lie15To6KmFromTheirSector_OutsidePaddocks_AndRuin06IsWalkedAtLowTide(string name)
    {
        IslandWorks w = TestIslands.Works(name);
        double s = TestIslands.Shape(name).Anchors.S;
        Assert.Equal(IslandWorks.AuthoredRuinTypes.Length, w.AuthoredRuins.Count);
        Site court = Assert.Single(w.Sites, t => t.Kind == "court");
        AuthoredRuin r7 = w.AuthoredRuins.Single(q => q.Id == "RUIN-07");
        Assert.True(r7.Placed);
        Assert.Equal((court.X, court.Y, court.Id), (r7.X, r7.Y, r7.AtSite));
        foreach (AuthoredRuin q in w.AuthoredRuins.Where(q => q.Placed && q.Id != "RUIN-07"))
        {
            Site home = w.Sites[q.SectorSite];
            Assert.Equal(IslandWorks.SectorSite[q.Type.Sector!], home.Kind);
            double d = Math.Sqrt((q.X - home.X) * (q.X - home.X) + (q.Y - home.Y) * (q.Y - home.Y));
            Assert.InRange(d, 1500 * s, 6000 * s);
            Assert.Equal(-1, w.PaddockCell[q.Cell]);
            // survey posts come after the ruins and don't keep this distance (anomaly A9, as the lab does: Seed12's RUIN-03)
            Assert.All(w.Sites.Where(t => t.Kind != "survey"),
                t => Assert.True(Math.Sqrt((t.X - q.X) * (t.X - q.X) + (t.Y - q.Y) * (t.Y - q.Y)) >= t.R + 300, $"{q.Id} too close to {t.Name}"));
        }
        // every placement that failed says why; on these islands only RUIN-05 on the cliff-less one can fail
        foreach (AuthoredRuin q in w.AuthoredRuins.Where(q => !q.Placed))
        {
            Assert.False(string.IsNullOrEmpty(q.Reason));
            Assert.Contains(w.Program.Failures, f => f.Key == "ruin:" + q.Id);
        }
        Assert.All(w.AuthoredRuins.Where(q => !q.Placed), q => Assert.Equal("RUIN-05", q.Id));
        // RUIN-06 out on the P4b reef flat, reached across it at low tide: no causeway
        AuthoredRuin r6 = w.AuthoredRuins.Single(q => q.Id == "RUIN-06");
        Assert.True(r6.Placed && r6.LowTide, $"RUIN-06: {r6.Approach ?? r6.Reason}");
        Assert.Equal("reef flat at low tide", r6.Approach);
        Assert.Equal(0, r6.CausewayM);
        Assert.NotNull(TestIslands.Shape(name).ReefFlatMask);
        Assert.Equal(1, TestIslands.Shape(name).ReefFlatMask![r6.Cell]);
    }

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void FourLeyLines_CrossAtTheCourt_EachWithASurveyPost(string name)
    {
        IslandWorks w = TestIslands.Works(name);
        Site court = Assert.Single(w.Sites, t => t.Kind == "court");
        Assert.Equal(new[] { "crown", "verdant", "resonant", "sounding" }, w.LeyRoutes.Select(l => l.Key));
        foreach (LeyLineRoute l in w.LeyRoutes)
        {
            Assert.Equal((court.X, court.Y), (l.Xs[1], l.Ys[1])); // past the frame edge, then through the Court
            Assert.Equal(l.Xs.Length - 1, w.LeyLines.Count(s => s.Line == l.Key));
            Site post = w.Sites[l.Survey];
            Assert.Equal(("survey", l.Key), (post.Kind, post.Line));
        }
        Assert.Equal(4, w.Sites.Count(t => t.Kind == "survey"));
    }

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void Outstations_TwoToEight_AtMostTwoPerSector_25To10KmFromTheirCompound(string name)
    {
        IslandWorks w = TestIslands.Works(name);
        double s = TestIslands.Shape(name).Anchors.S;
        SiteProgram p = w.Program;
        Assert.InRange(p.OutstationCount, 2, 8);
        Assert.Equal(p.OutstationCount, p.Outstations.Count);
        Assert.All(p.Outstations.GroupBy(o => o.Sector), g => Assert.True(g.Count() <= 2, g.Key));
        foreach (Outstation o in p.Outstations)
        {
            Site t = w.Sites[o.Site], home = w.Sites.Single(u => u.Kind == IslandWorks.SectorSite[o.Sector]);
            Assert.Equal(("outstation", o.Sector, home.KindName + " outstation"), (t.Kind, t.Sector, t.KindName));
            Assert.InRange(Math.Sqrt((t.X - home.X) * (t.X - home.X) + (t.Y - home.Y) * (t.Y - home.Y)), 2500 * s, 10000 * s);
        }
        Assert.Equal(IslandWorks.SectorCodes, p.States.Select(st => st.Sector));
        Assert.All(p.States, st => Assert.Contains(st.State, IslandWorks.FacilityStates));
    }

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void CentralAdministration_IsReservedBeneathTheCourt_WithItsWaysIn(string name)
    {
        IslandWorks w = TestIslands.Works(name);
        Site court = Assert.Single(w.Sites, t => t.Kind == "court");
        AdmFootprint adm = Assert.IsType<AdmFootprint>(w.Program.Adm);
        Assert.Equal((court.X, court.Y), (adm.X, adm.Y));
        Assert.True(adm.B1Z <= court.H - 15, $"B1 at {adm.B1Z} m under a Court at {court.H} m");
        Assert.Equal("stair", adm.Ports[0].Kind);
        // the monorail's first station is the Court's, and its lift is ADM's third way in
        Monorail mono = Assert.IsType<Monorail>(w.Monorail);
        Assert.Equal(court.Name, mono.Stations[0].Name);
        AdmPort lift = Assert.Single(adm.Ports, q => q.Kind == "monorail lift");
        Assert.Equal((mono.Stations[0].X, mono.Stations[0].Y), ((int)lift.X, (int)lift.Y));
        // the monorail key is kept at the Court
        Assert.Equal(court.Name, w.Objectives.Single(o => o.Key == "monorail-key").Site);
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
