namespace BlueHarvest.Sim.World.Generation;

/// <summary>How many of one facility kind the island wanted, and how many it got.</summary>
public sealed record FacilityCount(string Key, string Name, bool Major, int Wanted, int Placed)
{
    public int Short => Math.Max(0, Wanted - Placed);
}

/// <summary>
/// The numbers seed validation judges an island by (step 24): land, water, terrain, coast and the works.
/// Only reads the generated island, so measuring never changes it. Shares are 0–1.
/// "Dry land" is land that is not under a lake.
/// </summary>
public sealed record IslandStats
{
    /// <summary>Dry land below this height counts as lowland.</summary>
    public const double LowlandBelowM = 150;

    /// <summary>A piece of land apart from the main island counts as an islet from this size (smaller ones are sea stacks).</summary>
    public const double MinIsletKm2 = 0.1;

    /// <summary>Upper edges of the slope histogram's bins, degrees; the last bin is everything steeper.</summary>
    public static IReadOnlyList<double> SlopeBinEdgesDeg { get; } = [5, 10, 20, 30, 38];

    public required uint Seed { get; init; }
    public required double Scale { get; init; }

    // ---------- land ----------
    /// <summary>Cells in the island frame, land and sea.</summary>
    public required int Cells { get; init; }
    public required double CellKm2 { get; init; }
    public required int LandCells { get; init; }
    public required int DryCells { get; init; }
    public required double MainKm2 { get; init; }
    /// <summary>Every piece of land apart from the main island, largest first (sea stacks included).</summary>
    public required IReadOnlyList<double> OtherLandKm2 { get; init; }

    public double LandFraction => Cells == 0 ? 0 : LandCells / (double)Cells;
    public double LandKm2 => LandCells * CellKm2;
    public int Islets => OtherLandKm2.Count(a => a >= MinIsletKm2);
    public double LargestIsletKm2 => OtherLandKm2.Count > 0 ? OtherLandKm2[0] : 0;

    // ---------- water ----------
    public required int Rivers { get; init; }
    /// <summary>
    /// Rivers whose water reaches the sea: through a mouth, or into a coastal pit (a coast cell below sea level,
    /// where the drainage map records no mouth but the sea is next to it). Step 24b counts the coastal pits.
    /// </summary>
    public required int RiversToSea { get; init; }
    /// <summary>The largest mean discharge on any river cell, m³/s.</summary>
    public required double LargestRiverQ { get; init; }
    /// <summary>River cells too deep to wade at base flow (see <see cref="DeepAtBaseFlow"/>).</summary>
    public required int DeepRiverCells { get; init; }
    public required int Lakes { get; init; }
    public required double LakeKm2 { get; init; }
    /// <summary>Land cells the drainage map gives no receiver and no mouth (all pits, coastal or not).</summary>
    public required int PitCells { get; init; }
    /// <summary>Pits on the coast: below sea level, with the sea beside them. Their water does reach the sea.</summary>
    public required int CoastalPitCells { get; init; }

    public double RiversToSeaShare => Rivers == 0 ? 0 : RiversToSea / (double)Rivers;
    /// <summary>Pits away from the sea: truly enclosed basins that aren't lakes. Expected to be 0.</summary>
    public int InlandPitCells => PitCells - CoastalPitCells;

    // ---------- terrain ----------
    /// <summary>Dry land cells per slope bin (<see cref="SlopeBinEdgesDeg"/>).</summary>
    public required IReadOnlyList<int> SlopeCells { get; init; }
    public required double HighestM { get; init; }
    public required int LowlandCells { get; init; }
    /// <summary>Dry land cells in a walking-grid cell people can walk.</summary>
    public required int WalkableCells { get; init; }
    /// <summary>Of those, the cells in the largest connected patch of walkable ground.</summary>
    public required int WalkableConnectedCells { get; init; }

    public double LowlandShare => DryCells == 0 ? 0 : LowlandCells / (double)DryCells;
    public double WalkableShare => DryCells == 0 ? 0 : WalkableCells / (double)DryCells;
    public double WalkableConnectedShare => DryCells == 0 ? 0 : WalkableConnectedCells / (double)DryCells;
    /// <summary>Share of dry land steeper than 30°.</summary>
    public double SteepShare => DryCells == 0 ? 0 : (SlopeCells[SlopeCells.Count - 1] + SlopeCells[SlopeCells.Count - 2]) / (double)DryCells;

    // ---------- coast ----------
    public required int CoastCells { get; init; }
    public required int BeachCells { get; init; }
    public required int RockyCells { get; init; }
    public required int CliffCells { get; init; }
    public required int MainCoastCells { get; init; }
    public required int MainBeachCells { get; init; }

    /// <summary>Beach share of the main island's coast: the one the "at least 15%" decision is about.</summary>
    public double BeachShare => MainCoastCells == 0 ? 0 : MainBeachCells / (double)MainCoastCells;
    /// <summary>Beach share of every coast, islets included.</summary>
    public double BeachShareAll => CoastCells == 0 ? 0 : BeachCells / (double)CoastCells;

    // ---------- the works ----------
    /// <summary>One entry per kind in <see cref="IslandWorks.Facilities"/>, in table order.</summary>
    public required IReadOnlyList<FacilityCount> Facilities { get; init; }
    public required int PaddocksWanted { get; init; }
    public required int PaddocksPlaced { get; init; }
    public required double RoadKm { get; init; }
    public required double MainRoadKm { get; init; }
    public required int Bridges { get; init; }
    public required int Tunnels { get; init; }

    public int FacilitiesPlaced => Facilities.Sum(f => f.Placed);
    public int FacilitiesShort => Facilities.Sum(f => f.Short);
    public int PaddocksShort => Math.Max(0, PaddocksWanted - PaddocksPlaced);

    /// <summary>
    /// Whether a river of this mean discharge (m³/s) is too deep to wade at base flow: its bed on the play map
    /// (<see cref="IslandChunker.RiverDepth"/>) is deeper than <see cref="WaterLevels.WadeDepthDm"/>.
    /// That is about 32 m³/s; rain raises rivers further.
    /// </summary>
    public static bool DeepAtBaseFlow(double q) => IslandChunker.RiverDepth(q) * 10 > WaterLevels.WadeDepthDm;

    /// <summary>Which slope bin a slope in degrees falls in.</summary>
    public static int SlopeBin(double degrees)
    {
        int k = 0;
        while (k < SlopeBinEdgesDeg.Count && degrees >= SlopeBinEdgesDeg[k]) k++;
        return k;
    }

    /// <summary>Measures a generated island. Reads only.</summary>
    public static IslandStats Measure(IslandWorld world)
    {
        IslandShape s = world.Shape;
        IslandClimate c = world.Climate;
        IslandWorks w = world.Works;
        int nx = s.Nx, ny = s.Ny, n = s.N;
        double cell = s.Cell, cellKm2 = cell * cell / 1e6;
        byte[] land = s.Land;

        // the pieces of land (8-connected, as the works see them); the largest is the main island
        var label = new int[n];
        List<int> pieces = GenGrid.Components(land, nx, ny, label);
        int main = -1;
        for (int k = 0; k < pieces.Count; k++) if (main < 0 || pieces[k] > pieces[main]) main = k;
        var others = new List<double>();
        for (int k = 0; k < pieces.Count; k++) if (k != main) others.Add(pieces[k] * cellKm2);
        others.Sort((a, b) => b.CompareTo(a));

        // the walking grid's connected patches; the largest is "the" walkable island
        int wn = w.WalkNx * w.WalkNy;
        var walkMask = new byte[wn];
        for (int k = 0; k < wn; k++) walkMask[k] = w.WalkPassable(k) ? (byte)1 : (byte)0;
        var walkLabel = new int[wn];
        List<int> patches = GenGrid.Components(walkMask, w.WalkNx, w.WalkNy, walkLabel);
        int walkMain = -1;
        for (int k = 0; k < patches.Count; k++) if (walkMain < 0 || patches[k] > patches[walkMain]) walkMain = k;

        int landCells = 0, dryCells = 0, lowland = 0, walkable = 0, walkConnected = 0;
        int coast = 0, beach = 0, rocky = 0, cliff = 0, mainCoast = 0, mainBeach = 0;
        int pits = 0, coastalPits = 0, deep = 0;
        double highest = 0, largestQ = 0;
        var slopes = new int[SlopeBinEdgesDeg.Count + 1];
        DrainageTree tree = s.Tree;
        for (int i = 0; i < n; i++)
        {
            if (land[i] == 0) continue;
            landCells++;
            bool onMain = label[i] == main;
            byte cc = s.Coast[i];
            if (cc != CoastClass.None)
            {
                coast++;
                if (onMain) mainCoast++;
                if (cc == CoastClass.Beach) { beach++; if (onMain) mainBeach++; }
                else if (cc == CoastClass.Rocky) rocky++;
                else if (cc == CoastClass.Cliff) cliff++;
            }
            if (tree.Receiver[i] == DrainageTree.Outlet && tree.Mouth[i] < 0)
            {
                pits++;
                if (BesideSea(land, nx, ny, i)) coastalPits++;
            }
            if (s.IsRiver[i] != 0)
            {
                double q = s.Flow[i];
                if (q > largestQ) largestQ = q;
                if (DeepAtBaseFlow(q)) deep++;
            }
            if (!float.IsNaN(s.LakeLevel[i])) continue;
            dryCells++;
            double h = s.Height[i];
            if (h > highest) highest = h;
            if (h < LowlandBelowM) lowland++;
            slopes[SlopeBin(c.SlopeDeg[i])]++;
            int wc = w.WalkCellAt((i % nx + 0.5) * cell, (i / nx + 0.5) * cell);
            if (w.WalkPassable(wc))
            {
                walkable++;
                if (walkLabel[wc] == walkMain) walkConnected++;
            }
        }

        int toSea = 0;
        foreach (River r in s.Rivers) if (DrainsToSea(tree, land, nx, ny, r.Head)) toSea++;

        var facilities = new List<FacilityCount>();
        foreach (FacilityType f in IslandWorks.Facilities)
        {
            int placed = 0;
            foreach (Site t in w.Sites) if (t.Kind == f.Key) placed++;
            facilities.Add(new FacilityCount(f.Key, f.Name, f.Major, f.Count, placed));
        }

        return new IslandStats
        {
            Seed = s.Params.Seed,
            Scale = s.Params.Scale,
            Cells = n,
            CellKm2 = cellKm2,
            LandCells = landCells,
            DryCells = dryCells,
            MainKm2 = main < 0 ? 0 : pieces[main] * cellKm2,
            OtherLandKm2 = others,
            Rivers = s.Rivers.Count,
            RiversToSea = toSea,
            LargestRiverQ = largestQ,
            DeepRiverCells = deep,
            Lakes = s.Lakes.Count,
            LakeKm2 = s.Lakes.Sum(l => l.AreaKm2),
            PitCells = pits,
            CoastalPitCells = coastalPits,
            SlopeCells = slopes,
            HighestM = highest,
            LowlandCells = lowland,
            WalkableCells = walkable,
            WalkableConnectedCells = walkConnected,
            CoastCells = coast,
            BeachCells = beach,
            RockyCells = rocky,
            CliffCells = cliff,
            MainCoastCells = mainCoast,
            MainBeachCells = mainBeach,
            Facilities = facilities,
            PaddocksWanted = s.Params.PaddockSize > 0 ? IslandWorks.PaddockSpeciesList.Length : 0,
            PaddocksPlaced = w.Paddocks.Count,
            RoadKm = w.RoadKm,
            MainRoadKm = w.MainKm,
            Bridges = w.Bridges,
            Tunnels = w.Tunnels,
        };
    }

    /// <summary>Follows the drainage tree down from a cell: true if the water leaves through a mouth or a coastal pit.</summary>
    private static bool DrainsToSea(DrainageTree tree, byte[] land, int nx, int ny, int start)
    {
        int i = start;
        for (int steps = 0; steps < tree.N; steps++)
        {
            int r = tree.Receiver[i];
            if (r == DrainageTree.Outlet) return tree.Mouth[i] >= 0 || BesideSea(land, nx, ny, i);
            if (r < 0) return false; // a sea cell: not a river head
            i = r;
        }
        return false; // a loop, which the tree should never have
    }

    /// <summary>Whether a cell has sea (or the edge of the frame) among its eight neighbours.</summary>
    private static bool BesideSea(byte[] land, int nx, int ny, int i)
    {
        int x = i % nx, y = i / nx;
        for (int k = 0; k < 8; k++)
        {
            int xx = x + GenGrid.DX[k], yy = y + GenGrid.DY[k];
            if (xx < 0 || yy < 0 || xx >= nx || yy >= ny || land[yy * nx + xx] == 0) return true;
        }
        return false;
    }
}
