namespace BlueHarvest.Sim.World.Generation;

/// <summary>One of the six powers, or Central Administration (contested). Replaces the old corporations (D-8, F-4).</summary>
public sealed record IslandPower(string Code, string Name, string Goal);

/// <summary>A sector's seeded facility state: a label and a history seed, as data with no effect yet (P4.11).</summary>
public sealed record SectorState(string Sector, string State, uint HistorySeed);

/// <summary>Where a terrain role was found on the 128 m grid, and how much of it there is.</summary>
public sealed record RoleStat(string Role, double Km2, double LargestKm2, int Patches);

/// <summary>How a sector compound was placed: on its own ground with its ruin's in reach, or one of the fallbacks.</summary>
public sealed record SectorPlacement(string Sector, string How, bool OnRole);

/// <summary>An outstation (2–8 per seed, at most 2 per sector, P4.5): its site and sector, and whether it stands on the sector's ground.</summary>
public sealed record Outstation(int Site, string Sector, bool OnRole);

/// <summary>Something the site program could not place, and why. Seed checks read these (P4e).</summary>
public sealed record PlacementFailure(string Key, string Reason);

/// <summary>A way into Central Administration: the stair from the Court, a service adit, the monorail lift.</summary>
public sealed record AdmPort(string Kind, double X, double Y, double Z);

/// <summary>
/// Central Administration's underground footprint beneath the Court, reserved now and built in P10. B1Z is the floor of
/// B1, 15 m below the lowest ground over the footprint. WetCells: half-cells over it that are sea or lake; RiverCells: river.
/// </summary>
public sealed class AdmFootprint
{
    public required double X { get; init; }
    public required double Y { get; init; }
    public required double HalfW { get; init; }
    public required double HalfH { get; init; }
    public required double B1Z { get; init; }
    public required int WetCells { get; init; }
    public required int RiverCells { get; init; }
    /// <summary>Kinds of the other sites that reach over the footprint.</summary>
    public required List<string> Under { get; init; }
    public required List<AdmPort> Ports { get; init; }
    /// <summary>The service adit's distance from the Court, m (0 when there is none).</summary>
    public double AditFromCourt { get; init; }
}

/// <summary>One of the seven authored ruins (RUIN-01 to RUIN-07). RUIN-07 is the Court itself.</summary>
public sealed record AuthoredRuinType(string Id, string Name, string? Sector, string Role);

/// <summary>An authored ruin as placed. RUIN-06 records how its perimeter is reached (P4.7).</summary>
public sealed class AuthoredRuin
{
    public required AuthoredRuinType Type { get; init; }
    public string Id => Type.Id;
    public string Name => Type.Name;
    public required bool Placed { get; init; }
    public double X { get; init; }
    public double Y { get; init; }
    public int R { get; init; }
    /// <summary>The grid cell it stands on (−1 for RUIN-07, which is the Court's site).</summary>
    public int Cell { get; init; } = -1;
    /// <summary>RUIN-07: the Court's site id.</summary>
    public int AtSite { get; init; } = -1;
    /// <summary>Its sector compound's site id, and the distance to it in metres.</summary>
    public int SectorSite { get; init; } = -1;
    public double FromSector { get; init; }
    /// <summary>Cells of its role 1.5–6 km from its sector compound.</summary>
    public int InBand { get; init; }
    public string? Reason { get; init; }
    /// <summary>RUIN-06: walked across the reef flat at low tide, or reached by a causeway of CausewayM metres from Landing.</summary>
    public bool LowTide { get; init; }
    public string? Approach { get; init; }
    public (double X, double Y)? Landing { get; init; }
    public double CausewayM { get; init; }
    /// <summary>Its name's index in the works' labels (−1 for RUIN-07, the Court, and for one not placed): the chunks name its tiles (P4d).</summary>
    public int Label { get; set; } = -1;
    /// <summary>RUIN-06's causeway label, when it has a causeway; −1 otherwise.</summary>
    public int CausewayLabel { get; set; } = -1;
}

/// <summary>A ley line through the Court, bent toward its sites (D-10): Xs/Ys run from one frame edge to the other.</summary>
public sealed class LeyLineRoute
{
    public required string Key { get; init; }
    public required string Name { get; init; }
    public required double[] Xs { get; init; }
    public required double[] Ys { get; init; }
    /// <summary>How many of its sites the line passes through.</summary>
    public required int Through { get; init; }
    /// <summary>The site id of its survey post, −1 if none could be placed.</summary>
    public int Survey { get; set; } = -1;
}

/// <summary>What the site program decided and measured: terrain roles, sector placements, ruins, lines, outstations, states, ADM.</summary>
public sealed class SiteProgram
{
    /// <summary>The template centre: the saddle's midpoint, rounded to the metre.</summary>
    public (double X, double Y) Centre { get; set; }
    public Dictionary<string, RoleStat> Roles { get; } = new();
    public List<SectorPlacement> Sectors { get; } = [];
    public string CourtHow { get; set; } = "";
    public List<AuthoredRuin> Ruins { get; } = [];
    public List<LeyLineRoute> Lines { get; } = [];
    public List<Outstation> Outstations { get; } = [];
    public int OutstationCount { get; set; }
    public List<SectorState> States { get; } = [];
    public AdmFootprint? Adm { get; set; }
    public List<PlacementFailure> Failures { get; } = [];
    public ReefFlatAnchor? ReefFlat { get; set; }
    public FloodplainAnchor? Floodplain { get; set; }
}

/// <summary>
/// The site program for Crosswake Island (P4c, from the island lab's program 2): terrain roles as masks on the 128 m grid,
/// the Court on the central high ground, ADM's infrastructure, the six sectors on their roles with their authored ruins'
/// ground 1.5–6 km away, outstations, seeded states, ADM's footprint, then (after the paddocks) the authored ruins and the
/// four ley lines through the Court.
/// </summary>
public sealed partial class IslandWorks
{
    /// <summary>Sector codes, in P11's order.</summary>
    public static readonly string[] SectorCodes = ["MEN", "VIT", "ORC", "WTH", "CHO", "DRW"];

    /// <summary>The six powers and Central Administration, by code.</summary>
    public static readonly IReadOnlyDictionary<string, IslandPower> Powers = new Dictionary<string, IslandPower>
    {
        ["ADM"] = new("ADM", "Central Administration", "contested"),
        ["VIT"] = new("VIT", "Tesseract", "monetizable longevity"),
        ["CHO"] = new("CHO", "Order of the Grotesque", "the Great Change"),
        ["WTH"] = new("WTH", "Project Zephyrus", "storm dissipation and perpetual spring"),
        ["DRW"] = new("DRW", "KSSO", "recovery of the 1970s Soviet site"),
        ["MEN"] = new("MEN", "The Consortium", "weaponizable altered specimens"),
        ["ORC"] = new("ORC", "Caleb Archer's Project Cornucopia", "control of the prophetic fruit"),
    };

    /// <summary>The kind of each sector's main compound.</summary>
    public static readonly IReadOnlyDictionary<string, string> SectorSite = new Dictionary<string, string>
    {
        ["MEN"] = "genetics", ["VIT"] = "orgone", ["ORC"] = "orchard", ["WTH"] = "weather", ["CHO"] = "choir", ["DRW"] = "drowned",
    };

    /// <summary>The objective anchors the seed checks route between (step 24): the Court replaces the control centre.</summary>
    public static readonly string[] AnchorKinds = ["docks", "airstrip", "comms", "court", "watch"];

    public static readonly AuthoredRuinType[] AuthoredRuinTypes =
    [
        new("RUIN-01", "Unhatched Court", "MEN", "basin"),
        new("RUIN-02", "House of Borrowed Breath", "VIT", "ravine"),
        new("RUIN-03", "Garden of Unchosen Seasons", "ORC", "lowland"),
        new("RUIN-04", "Stair of the Second Horizon", "WTH", "ridge"),
        new("RUIN-05", "Cathedral of the Missing Note", "CHO", "cliff"),
        new("RUIN-06", "Tidemark Archive", "DRW", "reef"),
        new("RUIN-07", "Court of Unfinished Measures", null, "high"),
    ];

    private static readonly (string Key, string Name, string[] Toward)[] LeyLineTypes =
    [
        ("crown", "Crown line", ["weather", "RUIN-04"]),
        ("verdant", "Verdant line", ["RUIN-01", "orchard", "RUIN-03"]),
        ("resonant", "Resonant line", ["orgone", "choir", "RUIN-05"]),
        ("sounding", "Sounding line", ["drowned", "RUIN-06"]),
    ];

    public static readonly string[] FacilityStates = ["State of the Art", "Compromised", "Collapsing", "Fallen", "Derelict"];

    private static readonly string[] RoleKeys = ["high", "basin", "ravine", "lowland", "ridge", "cliff", "reef", "reefFlat", "plains"];

    public SiteProgram Program { get; } = new();

    /// <summary>The ley lines as routes (key, name, polyline); <see cref="LeyLines"/> holds their segments.</summary>
    public List<LeyLineRoute> LeyRoutes => Program.Lines;

    public List<AuthoredRuin> AuthoredRuins => Program.Ruins;

    // the terrain-role masks and their helpers, kept from placement to the ruins and lines
    private sealed class RoleMaps
    {
        public required Dictionary<string, byte[]> Mask { get; init; }
        public required Dictionary<string, List<int>> List { get; init; }
        public required float[] DistReef { get; init; }
        public required float[] DistCliff { get; init; }
        public required float[] DistFlat { get; init; }
        public required int[] FlatLabel { get; init; }
        public required byte[] FlatJoinedLabel { get; init; }
        public required double[] BlurBig { get; init; }
        public required double[] BlurMid { get; init; }
        public required double[] PlainsNear { get; init; }
        public bool FlatJoined(int i) => FlatLabel[i] >= 0 && FlatJoinedLabel[FlatLabel[i]] == 1;
    }

    private RoleMaps? _roles;

    private RoleMaps Roles2()
    {
        IslandShape w = _w;
        IslandClimate c = _c;
        IslandParams p = w.Params;
        IslandAnchors a = w.Anchors;
        int nx = _nx, ny = _ny, n = _n;
        double cell = _cell, area = _area, s = _s;
        double[] h = w.Height, rain = w.Rain;
        byte[] land = w.Land, sea = w.Sea, coast = w.Coast, region = c.Regions, biome = c.Biomes;
        float[] slopeD = c.SlopeDeg, crest = c.Crest, oldW = w.OldWeight;
        LineSegment sg = a.Saddle.Segment;
        double ccx = sg.Ax + sg.Dx / 2, ccy = sg.Ay + sg.Dy / 2;
        Program.Centre = (GenMath.Round(ccx), GenMath.Round(ccy));
        var blurBig = new double[n];
        var blurMid = new double[n];
        for (int i = 0; i < n; i++) { double v = land[i] != 0 ? Math.Max(0, h[i]) : 0; blurBig[i] = v; blurMid[i] = v; }
        GenGrid.BoxBlur(blurBig, nx, ny, Math.Max(1, (int)GenMath.Round(1500 / cell)), 2);
        GenGrid.BoxBlur(blurMid, nx, ny, Math.Max(1, (int)GenMath.Round(800 / cell)), 2);
        var bigRiver = new byte[n]; // within two cells of a river carrying 0.25 m³/s or more (2 marks 1 m³/s or more)
        for (int i = 0; i < n; i++)
        {
            if (w.IsRiver[i] == 0 || !(w.Flow[i] >= 0.25)) continue;
            int x = i % nx, y = i / nx;
            byte v = w.Flow[i] >= 1 ? (byte)2 : (byte)1;
            for (int yy = Math.Max(0, y - 2); yy <= Math.Min(ny - 1, y + 2); yy++)
                for (int xx = Math.Max(0, x - 2); xx <= Math.Min(nx - 1, x + 2); xx++)
                    if (bigRiver[yy * nx + xx] < v) bigRiver[yy * nx + xx] = v;
        }
        var mask = new Dictionary<string, byte[]>();
        foreach (string k in RoleKeys) mask[k] = new byte[n];
        byte[] high = mask["high"], basin = mask["basin"], ravine = mask["ravine"], lowland = mask["lowland"], ridge = mask["ridge"],
            cliff = mask["cliff"], reef = mask["reef"], reefFlat = mask["reefFlat"], plains = mask["plains"];
        var notReef = new byte[n];
        var notCliff = new byte[n];
        const double lowTide = -(12 + 5 + 1) / 10.0; // IslandWindow.TidalLowestDm: wade depth + spring + daily tide
        byte rgCoast = (byte)Region.Coast, rgWetland = (byte)Region.Wetland, rgVent = (byte)Region.Vent, rgRidge = (byte)Region.Ridge, rgCrater = (byte)Region.Crater;
        for (int i = 0; i < n; i++)
        {
            notReef[i] = 1; notCliff[i] = 1;
            if (land[i] == 0)
            {
                if (sea[i] == SeaBand.Reef) { reef[i] = 1; notReef[i] = 0; }
                if ((sea[i] == SeaBand.Reef || sea[i] == SeaBand.Lagoon) && h[i] >= lowTide) reefFlat[i] = 1;
                continue;
            }
            if (!OnMain(i)) continue;
            byte rg = region[i], bi = biome[i];
            double sl = slopeD[i], hh = h[i];
            // the central high ground: the plateau's height range (as the plateau region: below the saddle + 300 m), within 8 km of the template centre
            if (sl < 6 && hh <= p.Saddle + 300 && rg != rgCoast && rg != rgWetland && rg != rgVent && rg != rgRidge
                && GenMath.Hyp(CxOf(i) - ccx, CyOf(i) - ccy) <= 8000 * s) high[i] = 1;
            if (hh > 15 && sl < 12 && (rg == rgCrater || blurBig[i] - hh >= 50)) basin[i] = 1;
            if (rain[i] > 1800 && blurMid[i] - hh >= 50 && sl >= 6 && sl < 35 && hh >= 40 && hh <= 800 && rg != rgCoast) ravine[i] = 1;
            if ((bi == (byte)Biome.Moist || bi == (byte)Biome.Wet) && hh < 200 && sl < 8 && bigRiver[i] == 2) lowland[i] = 2; // along a river of 1 m³/s or more (P4b)
            if (hh >= 0.55 * p.Peak && crest[i] > 0.3 && sl < 25 && oldW[i] < 0.5) ridge[i] = 1;
            if (coast[i] == CoastClass.Cliff && hh >= 30) { cliff[i] = 1; notCliff[i] = 0; }
            if ((bi == (byte)Biome.Grassland || bi == (byte)Biome.Mesic) && sl < 10 && rg != rgCoast && rg != rgVent && rg != rgCrater) plains[i] = 1;
        }
        float[] distReef = GenGrid.Chamfer(notReef, nx, ny, cell), distCliff = GenGrid.Chamfer(notCliff, nx, ny, cell);
        // the reef flat (P4b): its distance map, and which of its patches join the main island (walked at low tide)
        var notFlat = new byte[n];
        for (int i = 0; i < n; i++) notFlat[i] = reefFlat[i] != 0 ? (byte)0 : (byte)1;
        float[] distFlat = GenGrid.Chamfer(notFlat, nx, ny, cell);
        var fl = new int[n];
        List<int> fsz = GenGrid.Components(reefFlat, nx, ny, fl);
        var joined = new byte[fsz.Count];
        for (int i = 0; i < n; i++)
        {
            if (fl[i] < 0 || joined[fl[i]] != 0) continue;
            int x = i % nx, y = i / nx;
            for (int k = 0; k < 8; k++)
            {
                int xx = x + GenGrid.DX[k], yy = y + GenGrid.DY[k];
                if (xx >= 0 && yy >= 0 && xx < nx && yy < ny && OnMain(yy * nx + xx)) { joined[fl[i]] = 1; break; }
            }
        }
        var plainsNear = new double[n];
        for (int i = 0; i < n; i++) plainsNear[i] = plains[i];
        GenGrid.BoxBlur(plainsNear, nx, ny, Math.Max(1, (int)GenMath.Round(2000 / cell)), 1);
        var list = new Dictionary<string, List<int>>();
        var lab = new int[n];
        foreach (string k in RoleKeys)
        {
            byte[] m = mask[k];
            var l = new List<int>();
            for (int i = 0; i < n; i++) if (m[i] != 0) l.Add(i);
            list[k] = l;
            List<int> sizes = GenGrid.Components(m, nx, ny, lab);
            Program.Roles[k] = new RoleStat(k, l.Count * area / 1e6, sizes.Count > 0 ? sizes.Max() * area / 1e6 : 0, sizes.Count);
        }
        Program.ReefFlat = w.ReefFlat;
        Program.Floodplain = c.Floodplain;
        return new RoleMaps
        {
            Mask = mask, List = list, DistReef = distReef, DistCliff = distCliff, DistFlat = distFlat, FlatLabel = fl, FlatJoinedLabel = joined,
            BlurBig = blurBig, BlurMid = blurMid, PlainsNear = plainsNear,
        };
    }

    /// <summary>Cells by score, best first (ties by cell index), with the same 0.6 jitter as PlaceBest.</summary>
    private List<(double V, int I)> Ranked(string key, Func<int, double> score)
    {
        uint js = GenHash.Stream(_w.Params.Seed, "sites2." + key);
        var list = new List<(double V, int I)>();
        for (int i = 0; i < _n; i++)
        {
            if (!OnMain(i)) continue;
            double v = score(i);
            if (v < -1e8) continue;
            list.Add((v + 0.6 * GenHash.Hash2(js, i, 7) / 4294967296.0, i));
        }
        list.Sort((a, b) => a.V != b.V ? b.V.CompareTo(a.V) : a.I.CompareTo(b.I));
        return list;
    }

    private double BandLo => 1500 * _s;
    private double BandHi => 6000 * _s;

    private int BandCount(List<int> l, double x, double y)
    {
        int c = 0;
        foreach (int j in l)
        {
            double d = GenMath.Hyp(CxOf(j) - x, CyOf(j) - y);
            if (d >= BandLo && d <= BandHi) c++;
        }
        return c;
    }

    /// <summary>The best-ranked cell that is clear of other sites and (with band) has its ruin's role 1.5–6 km away; −1 if none.</summary>
    private int Pick2(FacilityType f, string key, Func<int, double> score, List<int>? band, int tries = 400)
    {
        List<(double V, int I)> l = Ranked(key, score);
        int r = FacilityPrograms.Layout(f.Key).R, clear = 0;
        foreach (var (_, i) in l)
        {
            if (TooClose(CxOf(i), CyOf(i), r, f.Key, f.Apart)) continue;
            clear++;
            if (band == null || BandCount(band, CxOf(i), CyOf(i)) >= 3) return i;
            if (clear >= tries) break;
        }
        return -1;
    }

    private static FacilityType Fac(string key) => Array.Find(Facilities, f => f.Key == key)!;

    private void PlaceProgram(Func<string, Func<int, double>> sc, Func<FacilityType, int, int> placeBest, Action<FacilityType> placeAirstrip)
    {
        IslandShape w = _w;
        IslandClimate c = _c;
        IslandParams p = w.Params;
        IslandAnchors a = w.Anchors;
        int n = _n;
        double s = _s, sq = _sq, cell = _cell;
        double[] h = w.Height;
        byte[] coast = w.Coast, region = c.Regions, biome = c.Biomes;
        float[] slopeD = c.SlopeDeg, distLand = w.DistLand;
        const double No = -1e9;
        RoleMaps rm = Roles2();
        _roles = rm;
        SiteProgram pg = Program;
        void Fail(string key, string reason) => pg.Failures.Add(new PlacementFailure(key, reason));
        // tries: its own ground with its ruin's ground in reach; the fallback ground likewise; then each without the ruin's ground
        void Sector(string code, FacilityType f, Func<int, bool> onRole, string ruinRole, Func<int, double> score, Func<int, double> fallback)
        {
            (Func<int, double> Fn, bool Band, string Name)[] tries =
                [(score, true, "role"), (fallback, true, "fallback"), (score, false, "role, no ruin ground in reach"), (fallback, false, "fallback, no ruin ground in reach")];
            int got = -1;
            string how = "";
            foreach ((Func<int, double> fn, bool band, string name) in tries)
            {
                got = Pick2(f, f.Key + "." + name, fn, band ? rm.List[ruinRole] : null);
                how = name;
                if (got >= 0) break;
            }
            if (got < 0) { Fail("sector:" + code, "no ground for " + f.Name); return; }
            AddSite(f, CxOf(got), CyOf(got), sector: code);
            pg.Sectors.Add(new SectorPlacement(code, how, onRole(got)));
        }

        // 1. the Court of Unfinished Measures on the central high ground
        byte[] high = rm.Mask["high"];
        (double ccx, double ccy) = pg.Centre;
        double HiScore(int i) => high[i] != 0
            ? h[i] / 100 + (region[i] == (byte)Region.Plateau ? 1 : 0) - GenMath.Hyp(CxOf(i) - ccx, CyOf(i) - ccy) / (2500 * s) - slopeD[i] / 6.0 : No;
        int ci = Pick2(Fac("court"), "court", HiScore, null);
        string courtHow = "high ground";
        if (ci < 0) { ci = Pick2(Fac("court"), "court.fallback", sc("control"), null); courtHow = "fallback (control-centre ground)"; }
        if (ci < 0) Fail("court", "no ground for the Court");
        else { AddSite(Fac("court"), CxOf(ci), CyOf(ci), ruin: "RUIN-07"); pg.CourtHow = courtHow; }

        // 2. ADM's objective anchors, as before
        for (int k = 0; k < 2; k++)
        {
            int i = placeBest(Fac("docks"), k);
            if (i < 0) { Fail("docks", "no dock " + (k + 1)); break; }
            AddSite(Fac("docks"), CxOf(i), CyOf(i));
        }
        placeAirstrip(Fac("airstrip"));
        if (!Sites.Exists(t => t.Kind == "airstrip")) Fail("airstrip", "no straight 1.4 km");
        {
            int i = placeBest(Fac("comms"), 0);
            if (i < 0) Fail("comms", "no ground");
            else AddSite(Fac("comms"), CxOf(i), CyOf(i));
        }

        // 3. the six sectors, the most constrained ground first
        bool hasFlat = rm.List["reefFlat"].Count > 0;
        float[] reefD = hasFlat ? rm.DistFlat : rm.DistReef, distCliff = rm.DistCliff;
        Sector("DRW", Fac("drowned"), i => reefD[i] <= 600, hasFlat ? "reefFlat" : "reef",
            i => distLand[i] < 400 && slopeD[i] < 8 && h[i] < 40 && reefD[i] <= 600
                ? 2 - slopeD[i] / 8.0 - reefD[i] / 1000.0 + (coast[i] != 0 && coast[i] != CoastClass.Cliff ? 0.3 : 0) : No,
            i => distLand[i] < 600 && slopeD[i] < 8 && h[i] < 60 ? 1 - slopeD[i] / 8.0 : No);
        Sector("CHO", Fac("choir"), i => distCliff[i] <= 600, "cliff",
            i => distCliff[i] >= 100 && distCliff[i] <= 600 && h[i] > 30 && slopeD[i] < 15
                ? 2 + Math.Min(1, h[i] / 200) - slopeD[i] / 10.0 - distCliff[i] / 1000.0 : No,
            i => distLand[i] < 800 && h[i] > 20 && slopeD[i] < 15 ? 1 - slopeD[i] / 15.0 : No);
        byte[] ridge = rm.Mask["ridge"], lowland = rm.Mask["lowland"], basin = rm.Mask["basin"], ravine = rm.Mask["ravine"], plains = rm.Mask["plains"];
        Func<int, double> weather = sc("weather");
        Sector("WTH", Fac("weather"), i => ridge[i] != 0, "ridge", i => ridge[i] != 0 ? weather(i) : No, weather);
        Sector("ORC", Fac("orchard"), i => lowland[i] != 0, "lowland",
            i => lowland[i] != 0 ? 2 - slopeD[i] / 8.0 + 0.3 * lowland[i] + (w.IsRiver[i] != 0 ? 0 : 0.2) : No,
            i => (biome[i] == (byte)Biome.Moist || biome[i] == (byte)Biome.Wet) && NearRiver[i] != 0 && h[i] < 300 && slopeD[i] < 8 ? 1 - slopeD[i] / 8.0 : No);
        double[] plainsNear = rm.PlainsNear;
        Func<int, double> genetics = sc("genetics");
        Sector("MEN", Fac("genetics"), i => plainsNear[i] > 0.3, "basin",
            i => { double v = genetics(i); return v < -1e8 ? v : v + 0.5 * plainsNear[i]; },
            i => slopeD[i] < 8 && h[i] > 20 && h[i] < 450 && region[i] != (byte)Region.Coast ? 1 - slopeD[i] / 8.0 + 0.5 * plainsNear[i] : No);
        {
            int i = placeBest(Fac("raptors"), 0);
            if (i < 0) Fail("raptors", "no ground beside the Menagerie");
            else AddSite(Fac("raptors"), CxOf(i), CyOf(i), sector: "MEN");
        }
        Sector("VIT", Fac("orgone"), i => GenMath.Hyp(CxOf(i) - a.Caldera.X, CyOf(i) - a.Caldera.Y) < 1.3 * a.Caldera.R, "ravine", sc("orgone"),
            i => (basin[i] != 0 || ravine[i] != 0) && slopeD[i] < 10 ? 2 - slopeD[i] / 10.0 : No);

        // 4. the rest of ADM's infrastructure
        {
            int i = placeBest(Fac("geothermal"), 0);
            if (i < 0) Fail("geothermal", "no ground by the vent");
            else AddSite(Fac("geothermal"), CxOf(i), CyOf(i));
        }
        for (int k = 0; k < 2; k++)
        {
            int i = placeBest(Fac("depot"), k);
            if (i < 0) { Fail("depot", "no depot " + (k + 1)); break; }
            AddSite(Fac("depot"), CxOf(i), CyOf(i));
        }
        for (int k = 0; k < 3; k++)
        {
            int i = placeBest(Fac("watch"), k);
            if (i < 0) { Fail("watch", "no watch post " + (k + 1)); break; }
            AddSite(Fac("watch"), CxOf(i), CyOf(i));
        }
        {
            // the coastal service yard: low, flat ground by the first dock
            if (Sites.Find(t => t.Kind == "docks") is not { } dk) Fail("yard", "no flat ground by a dock");
            else
            {
                int got = Pick2(Fac("yard"), "yard", i =>
                {
                    double d = GenMath.Hyp(CxOf(i) - dk.X, CyOf(i) - dk.Y);
                    return d > dk.R + 150 && d < 1500 * sq && slopeD[i] < 6 && h[i] < 60 ? 3 - d / 1500 - slopeD[i] / 6.0 : No;
                }, null);
                if (got < 0) Fail("yard", "no flat ground by a dock");
                else AddSite(Fac("yard"), CxOf(got), CyOf(got), dock: dk.Id);
            }
        }

        // 5. outstations: 2–8, each tied to a sector, at most 2 per sector (P4.5)
        var ro = new GenRng(p.Seed, "sites.outstations");
        int count = 2 + (int)Math.Floor(ro.Next() * 7);
        var slots = new List<string>();
        foreach (string code in SectorCodes) { slots.Add(code); slots.Add(code); }
        for (int k = slots.Count - 1; k > 0; k--)
        {
            int j = (int)Math.Floor(ro.Next() * (k + 1));
            (slots[k], slots[j]) = (slots[j], slots[k]);
        }
        pg.OutstationCount = count;
        float[] distReef = rm.DistReef;
        Func<int, bool> Near(string code) => code switch
        {
            "MEN" => i => plains[i] != 0,
            "VIT" => i => ravine[i] != 0 || basin[i] != 0,
            "ORC" => i => lowland[i] != 0 || (biome[i] == (byte)Biome.Moist && NearRiver[i] != 0),
            "WTH" => i => ridge[i] != 0,
            "CHO" => i => distCliff[i] <= 600,
            _ => i => distReef[i] <= 600 && distLand[i] < 800,
        };
        for (int k = 0; k < count; k++)
        {
            string code = slots[k];
            if (Sites.Find(t => t.Sector == code && t.Kind == SectorSite[code]) is not { } home) { Fail("outstation:" + code, "its sector was not placed"); continue; }
            var f = new FacilityType("outstation", "Outstation", "R", 1, code);
            Func<int, bool> near = Near(code);
            int got = Pick2(f, "outstation." + k, i =>
            {
                double d = GenMath.Hyp(CxOf(i) - home.X, CyOf(i) - home.Y);
                if (d < 2500 * s || d > 10000 * s || slopeD[i] >= (code == "WTH" ? 20 : 10)) return No;
                return (near(i) ? 1 : 0) + 1 - d / (10000 * s) - slopeD[i] / 20.0;
            }, null);
            if (got < 0) { Fail("outstation:" + code, "no ground 2.5–10 km from " + home.KindName); continue; }
            Site os = AddSite(f, CxOf(got), CyOf(got), sector: code, kindName: home.KindName + " outstation", onRole: near(got));
            pg.Outstations.Add(new Outstation(os.Id, code, os.OnRole));
        }

        // 6. seeded facility states, data only (P4.11)
        var rs = new GenRng(p.Seed, "sites.state");
        uint hist = GenHash.Stream(p.Seed, "sites.history");
        for (int k = 0; k < SectorCodes.Length; k++)
            pg.States.Add(new SectorState(SectorCodes[k], FacilityStates[(int)Math.Floor(rs.Next() * FacilityStates.Length)], GenHash.Hash2(hist, k, 0)));

        // 7. Central Administration's underground footprint beneath the Court, reserved now and built in P10
        if (Sites.Find(t => t.Kind == "court") is { } court)
        {
            const double hw = 120, hh = 80;
            double minH = 1e9;
            int wetCells = 0, riverCells = 0;
            for (double y = court.Y - hh; y <= court.Y + hh; y += cell / 2)
            {
                for (double x = court.X - hw; x <= court.X + hw; x += cell / 2)
                {
                    int j = Idx(x, y);
                    minH = Math.Min(minH, h[j]);
                    if (!Dry(j)) wetCells++; // wet: sea or lake over it
                    else if (w.IsRiver[j] != 0) riverCells++;
                }
            }
            // a service adit: the nearest hillside below the Court within 3 km, driven in at B1 level
            int adit = -1;
            double ad = 1e30;
            for (int i = 0; i < n; i++)
            {
                if (!OnMain(i) || slopeD[i] < 8 || h[i] > court.H - 12) continue;
                double d = GenMath.Hyp(CxOf(i) - court.X, CyOf(i) - court.Y);
                if (d < 200 || d > 3000 * s) continue;
                if (d < ad) { ad = d; adit = i; }
            }
            var under = Sites.Where(t => t != court && Math.Abs(t.X - court.X) < hw + t.R && Math.Abs(t.Y - court.Y) < hh + t.R).Select(t => t.Kind).ToList();
            var ports = new List<AdmPort> { new("stair", court.X, court.Y, court.H) };
            if (adit >= 0) ports.Add(new AdmPort("service adit", CxOf(adit), CyOf(adit), h[adit]));
            // no adit is no fault: the plan's entrances are the stair from the Court and the monorail lift
            pg.Adm = new AdmFootprint
            {
                X = court.X, Y = court.Y, HalfW = hw, HalfH = hh, B1Z = GenMath.Round(minH - 15), WetCells = wetCells, RiverCells = riverCells,
                Under = under, Ports = ports, AditFromCourt = adit >= 0 ? ad : 0,
            };
        }
    }

    /// <summary>The authored ruins, after the paddocks so none falls inside one.</summary>
    private void PlaceAuthoredRuins()
    {
        RoleMaps rm = _roles!;
        IslandShape w = _w;
        double[] h = w.Height;
        byte[] region = _c.Regions;
        float[] crest = _c.Crest;
        int nx = _nx, ny = _ny;
        double cell = _cell, s = _s;
        var ruinSites = new List<(double X, double Y)>();
        foreach (AuthoredRuinType ru in AuthoredRuinTypes)
        {
            if (ru.Id == "RUIN-07")
            {
                Site? ct = Sites.Find(t => t.Kind == "court");
                AuthoredRuins.Add(ct != null
                    ? new AuthoredRuin { Type = ru, Placed = true, X = ct.X, Y = ct.Y, R = ct.R, AtSite = ct.Id }
                    : new AuthoredRuin { Type = ru, Placed = false, Reason = "no Court" });
                continue;
            }
            if (Sites.Find(t => t.Kind == SectorSite[ru.Sector!]) is not { } home)
            {
                AuthoredRuins.Add(new AuthoredRuin { Type = ru, Placed = false, Reason = "its sector was not placed" });
                continue;
            }
            bool isReef = ru.Role == "reef", onFlat = isReef && rm.List["reefFlat"].Count > 0; // P4b: on the reef flat, walked at low tide
            uint js = GenHash.Stream(w.Params.Seed, "ruins7." + ru.Id);
            List<int> l = rm.List[onFlat ? "reefFlat" : ru.Role];
            List<Site> others = Sites.Where(t => t.Sector != ru.Sector).ToList(); // a ruin prefers ground nearer its own sector than any other site
            // RUIN-06's landing: the nearest main-island cell within 700 m (reef round an islet has none)
            (int J, double D)? Landing(int i)
            {
                int x0 = i % nx, y0 = i / nx, rr = (int)Math.Ceiling(700 / cell), bj = -1;
                double bd = 1e30;
                for (int yy = Math.Max(0, y0 - rr); yy <= Math.Min(ny - 1, y0 + rr); yy++)
                {
                    for (int xx = Math.Max(0, x0 - rr); xx <= Math.Min(nx - 1, x0 + rr); xx++)
                    {
                        int j = yy * nx + xx;
                        if (!OnMain(j)) continue;
                        double d = GenMath.Hyp(CxOf(j) - CxOf(i), CyOf(j) - CyOf(i));
                        if (d < bd && d <= 700) { bd = d; bj = j; }
                    }
                }
                return bj < 0 ? null : (bj, bd);
            }
            int best = -1, inBand = 0, byPaddock = 0, bySite = 0, byRuin = 0, byLanding = 0;
            double bs = -1e9;
            (int J, double D)? bestLand = null;
            foreach (int i in l)
            {
                double x = CxOf(i), y = CyOf(i), d = GenMath.Hyp(x - home.X, y - home.Y);
                if (d < BandLo || d > BandHi) continue;
                inBand++;
                if (PaddockCell[i] >= 0) { byPaddock++; continue; }
                if (Sites.Exists(t => GenMath.Hyp(t.X - x, t.Y - y) < t.R + 300)) { bySite++; continue; }
                if (ruinSites.Exists(q => GenMath.Hyp(q.X - x, q.Y - y) < 1000 * s)) { byRuin++; continue; }
                double v = ru.Role switch
                {
                    "basin" => (rm.BlurBig[i] - h[i]) / 100 + (region[i] == (byte)Region.Crater ? 0.5 : 0),
                    "ravine" => (rm.BlurMid[i] - h[i]) / 100,
                    "lowland" => rm.Mask["lowland"][i] / 2.0 + (w.IsRiver[i] != 0 ? 0 : 0.3),
                    "ridge" => h[i] / 1000 + crest[i],
                    "cliff" => h[i] / 200,
                    _ => 0, // the reef is scored on its landing below
                };
                (int J, double D)? land = null;
                if (isReef)
                {
                    if (onFlat && !rm.FlatJoined(i)) { byLanding++; continue; }
                    land = Landing(i);
                    if (land == null) { byLanding++; continue; }
                    (int lj, double ld) = land.Value;
                    v = onFlat
                        ? -Math.Abs(w.DistSea[i] - 450 * s) / 1000 - GenMath.Hyp(CxOf(lj) - home.X, CyOf(lj) - home.Y) / (6000 * s) // out on the flat, 300–700 m from shore
                        : -ld / 1000 - GenMath.Hyp(CxOf(lj) - home.X, CyOf(lj) - home.Y) / (6000 * s); // a short causeway landing near the sector
                }
                double dOther = 1e30;
                foreach (Site t in others) dOther = Math.Min(dOther, GenMath.Hyp(t.X - x, t.Y - y));
                v -= 0.5 * Math.Max(0, d / dOther - 1);
                v += 0.3 * GenHash.Hash2(js, i, 1) / 4294967296.0;
                if (v > bs) { bs = v; best = i; bestLand = land; }
            }
            if (best < 0)
            {
                string why = inBand > 0
                    ? $"all {inBand} {ru.Role} cells 1.5–6 km away are taken (paddocks {byPaddock}, sites {bySite}, ruins {byRuin}, no landing {byLanding})"
                    : $"no {ru.Role} ground 1.5–6 km from {home.KindName}";
                AuthoredRuins.Add(new AuthoredRuin { Type = ru, Placed = false, Reason = why, InBand = inBand });
                Program.Failures.Add(new PlacementFailure("ruin:" + ru.Id, why));
                continue;
            }
            bool lowTide = isReef && rm.Mask["reefFlat"][best] != 0 && rm.FlatJoined(best);
            var placed = new AuthoredRuin
            {
                Type = ru, Placed = true, X = CxOf(best), Y = CyOf(best), R = 80, Cell = best,
                FromSector = GenMath.Hyp(CxOf(best) - home.X, CyOf(best) - home.Y), SectorSite = home.Id, InBand = inBand,
                // reached across the reef flat at low tide, or else by a causeway from the main island (P4.7)
                LowTide = lowTide,
                Approach = isReef ? (lowTide ? "reef flat at low tide" : "causeway") : null,
                Landing = isReef && bestLand is { } bl ? (CxOf(bl.J), CyOf(bl.J)) : null,
                CausewayM = isReef && !lowTide && bestLand is { } bl2 ? bl2.D : 0,
            };
            ruinSites.Add((placed.X, placed.Y));
            AuthoredRuins.Add(placed);
        }
    }

    /// <summary>Four ley lines through the Court, and an ADM survey post on each.</summary>
    private void PlaceLeyLines()
    {
        if (Sites.Find(t => t.Kind == "court") is not { } court) return;
        IslandShape w = _w;
        IslandAnchors a = w.Anchors;
        float[] slopeD = _c.SlopeDeg;
        double cell = _cell, s = _s;
        (double X, double Y)? At(string key)
        {
            if (key.StartsWith("RUIN-", StringComparison.Ordinal))
            {
                AuthoredRuin? q = AuthoredRuins.Find(u => u.Id == key && u.Placed);
                return q == null ? null : (q.X, q.Y);
            }
            Site? t = Sites.Find(u => u.Kind == key);
            return t == null ? null : (t.X, t.Y);
        }
        double big = a.W + a.H;
        FacilityType f = Fac("survey");
        int rS = FacilityPrograms.Layout("survey").R;
        foreach ((string key, string name, string[] toward) in LeyLineTypes)
        {
            var left = new List<(double X, double Y)>();
            foreach (string k in toward) if (At(k) is { } pt) left.Add(pt);
            var pts = new List<(double X, double Y)> { (court.X, court.Y) };
            while (left.Count > 0)
            {
                (double X, double Y) cur = pts[^1];
                left = left.OrderBy(u => GenMath.Hyp(u.X - cur.X, u.Y - cur.Y)).ToList();
                pts.Add(left[0]);
                left.RemoveAt(0);
            }
            if (pts.Count < 2) { Program.Failures.Add(new PlacementFailure("ley:" + key, "none of its sites were placed")); continue; }
            static (double X, double Y) Unit((double X, double Y) p0, (double X, double Y) p1)
            {
                double d = GenMath.Hyp(p1.X - p0.X, p1.Y - p0.Y);
                if (d == 0) d = 1;
                return ((p1.X - p0.X) / d, (p1.Y - p0.Y) / d);
            }
            (double X, double Y) d0 = Unit(pts[0], pts[1]), dn = Unit(pts[^2], pts[^1]);
            var xs = new List<double> { court.X - d0.X * big };
            var ys = new List<double> { court.Y - d0.Y * big };
            foreach ((double px, double py) in pts) { xs.Add(px); ys.Add(py); }
            xs.Add(pts[^1].X + dn.X * big); ys.Add(pts[^1].Y + dn.Y * big);
            for (int k = 0; k + 1 < xs.Count; k++) LeyLines.Add(new LeyLine(xs[k], ys[k], xs[k + 1], ys[k + 1], key));
            var line = new LeyLineRoute { Key = key, Name = name, Xs = xs.ToArray(), Ys = ys.ToArray(), Through = pts.Count - 1 };
            // the survey post: on the line 3–12 km out from the Court, on flat dry ground outside the paddocks
            uint js = GenHash.Stream(w.Params.Seed, "survey." + key);
            bool found = false;
            double bx = 0, by = 0, bs = -1e9;
            for (int k = 0; k + 1 < xs.Count; k++)
            {
                double ax = xs[k], ay = ys[k], ex = xs[k + 1], ey = ys[k + 1], len = GenMath.Hyp(ex - ax, ey - ay);
                int steps = Math.Min(4000, (int)Math.Floor(len / (cell / 2)));
                for (int m = 0; m <= steps; m++)
                {
                    double x = ax + (ex - ax) * m / Math.Max(1, steps), y = ay + (ey - ay) * m / Math.Max(1, steps), dc = GenMath.Hyp(x - court.X, y - court.Y);
                    if (dc < 3000 * s || dc > 12000 * s || x < 0 || y < 0 || x >= a.W || y >= a.H) continue;
                    int i = Idx(x, y);
                    if (!OnMain(i) || slopeD[i] >= 18 || PaddockCell[i] >= 0) continue;
                    if (pts.Exists(q => GenMath.Hyp(q.X - x, q.Y - y) < 1500 * s)) continue;
                    if (AuthoredRuins.Exists(q => q.Placed && GenMath.Hyp(q.X - x, q.Y - y) < rS + 300)) continue; // the ruins' 300 m clearance (A9)
                    double v = -slopeD[i] / 18.0 + 0.3 * GenHash.Hash2(js, i, k) / 4294967296.0;
                    if (v > bs && !TooClose(x, y, rS, "survey", 0)) { bs = v; found = true; bx = GenMath.Round(x); by = GenMath.Round(y); }
                }
            }
            if (found) line.Survey = AddSite(f, bx, by, line: key).Id;
            else Program.Failures.Add(new PlacementFailure("survey:" + key, "no flat dry ground on the line 3–12 km from the Court"));
            Program.Lines.Add(line);
        }
    }
}
