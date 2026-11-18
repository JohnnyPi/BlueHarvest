using BlueHarvest.Sim.World.Generation;
using Xunit;

namespace BlueHarvest.Sim.Tests;

/// <summary>
/// Stage 5 of the world-generator port: 1 m chunk detail, checked tile for tile against the island lab
/// (tools/island-lab/golden-chunks.js). The chunks were picked to cover every kind of feature: compounds,
/// docks, the runway, paddock gates and moats, rivers, lakes, caves, wrecks, ruins, transit, cliffs, the
/// pali, reefs and open sea. If earlier generator tests fail too, fix those first.
/// </summary>
public class IslandChunkTests
{
    public sealed record Expected(int WideX, int WideY, ulong WideHash, (int Cx, int Cy, ulong Hash)[] Chunks);

    // node tools/island-lab/golden-chunks.js
    private static readonly Dictionary<string, Expected> Golden = new()
    {
        ["Default"] = new(451, 113, 0x49033EFB4E39C13EUL,
        [
            (0, 0, 0x2716470B97C233F5UL),
            (503, 363, 0xF0AF376A92598D34UL),
            (451, 113, 0x8F537A814FDBFC13UL),
            (447, 107, 0x623F3E50CE226B96UL),
            (481, 551, 0x2CECCD2BD7B484A9UL),
            (643, 313, 0xCFB1E7422A246713UL),
            (561, 279, 0x69B582C38BB74AF5UL),
            (315, 458, 0x002E2C825EB18145UL),
            (315, 457, 0xC223BCA782090B22UL),
            (533, 401, 0xFCC4157A7A73D4A8UL),
            (458, 107, 0xEA5D3FEDFD186FB3UL),
            (448, 160, 0xCE55D31A1A342F82UL),
            (449, 167, 0xD3B144194B280EA7UL),
            (585, 431, 0x00F6FE28A64D6D99UL),
            (621, 572, 0x61F3BE97313F3BC9UL),
            (413, 442, 0x481A4161ABF8627FUL),
            (418, 132, 0x036399692D8EF766UL),
            (408, 140, 0x733BBF1974E56D65UL),
            (575, 307, 0xEF5D710242D82C5FUL),
            (574, 360, 0xB748A020D254ED8CUL),
            (202, 194, 0xEF2C8309C9478E26UL),
            (747, 333, 0x79BD3107E075DBE6UL),
            (571, 551, 0x9037B2D106113F60UL),
            (679, 327, 0xA42A102A8F7DF6D5UL),
            (871, 519, 0x9487187656DA9829UL),
            (103, 213, 0x6ADDC51A57F43EA8UL),
            (493, 569, 0x1EE95B6EF7B10E7BUL),
            (527, 271, 0xDA53F7EDDE418CF1UL),
            (559, 279, 0x6265BFD52C5D4658UL),
            (451, 114, 0x5C6038A281B2F84EUL),
            (562, 269, 0x9373D0F76CDC805BUL),
            (776, 485, 0xEAFA2B6F6BFE222DUL),
            (391, 221, 0x4459ECEB7594347BUL),
            (393, 75, 0x14731293458363D1UL),
            (393, 59, 0x033DEE7346082A22UL),
            (631, 133, 0xDB9C3F24A0A598B0UL),
            (309, 115, 0xD4BFE2BB81256DDBUL),
            (435, 121, 0x5E4FF86F81A1918DUL),
        ]), // 38 chunks, 40 of 42 covers seen
        ["Seed12"] = new(357, 87, 0x8BF5FDA3CBB115B2UL,
        [
            (0, 0, 0xC596B420B947D8F8UL),
            (503, 363, 0xA2E1E49401E344C0UL),
            (357, 87, 0xEAABB179E855EBB3UL),
            (363, 89, 0x26623EC9114015BEUL),
            (559, 441, 0xA84CE249BFEC206EUL),
            (545, 225, 0xEED58B8CA6A94DBEUL),
            (571, 249, 0xD4C735081D2CA2A0UL),
            (791, 540, 0x50EF39DFF14D40CAUL),
            (791, 539, 0xFF518D445B411F35UL),
            (469, 323, 0xA9E9B74B1D0B37A4UL),
            (372, 90, 0xEEE30AFD0460EEDCUL),
            (331, 162, 0xF6250C626831641FUL),
            (327, 169, 0xBAC089CCD7D72C00UL),
            (474, 305, 0x5C06C29478BFFEBAUL),
            (415, 280, 0x4DA5B516157A795CUL),
            (363, 468, 0x10088A1A7B93CE4BUL),
        ]), // 16 chunks, 32 of 42 covers seen
        ["SmallNoCliffs"] = new(419, 333, 0x6E30405E4F9D0397UL,
        [
            (0, 0, 0xA8F42DA9AE204A69UL),
            (335, 242, 0xD209A392F64F2051UL),
            (419, 333, 0x47F483110EE5642EUL),
            (429, 335, 0x8E3D38B210CA152BUL),
            (181, 175, 0xAA242ACDE0AA20E8UL),
            (367, 205, 0x33970A2602917379UL),
            (357, 183, 0x253C6A9ADFCE53D9UL),
            (250, 382, 0x4E89C7EB0FC8BC12UL),
            (251, 381, 0x9A736B4163784BF3UL),
            (349, 289, 0x0A483E12575267EAUL),
            (416, 319, 0x0D94CFAB2CF4DEC1UL),
            (376, 277, 0xA75874DFE49CD023UL),
            (371, 271, 0xDD14DEE75551FF86UL),
            (255, 201, 0xF179A018DA2C49F5UL),
            (163, 177, 0x7F085D88D2A5E30CUL),
            (239, 272, 0x92BD925DA3A7F5EDUL),
        ]), // 16 chunks, 31 of 42 covers seen
        ["CoolWet"] = new(421, 133, 0xA72EA2AE98749C64UL,
        [
            (0, 0, 0x93F85708B6401F2CUL),
            (403, 290, 0x9CF32C2BDB7BED4AUL),
            (421, 133, 0x54F5DC147355722FUL),
            (425, 139, 0xE071559F387DFBB3UL),
            (315, 235, 0xA0CBB0BDDA8F5B9CUL),
            (537, 271, 0xA368835EAB401F1AUL),
            (511, 239, 0x404A8DC974995C5EUL),
            (573, 438, 0xD9F510FC6652767BUL),
            (573, 437, 0xF63527B032043220UL),
            (407, 307, 0xF53A025091E4EECCUL),
            (432, 132, 0x7287300BC0FCDE1FUL),
            (403, 236, 0xC7BFA7DC96C5E58AUL),
            (403, 243, 0x9800AF44D963BBB4UL),
            (458, 348, 0x57BDE0CAC147025BUL),
            (510, 395, 0xB3631E3B7CA0D2CBUL),
            (348, 381, 0x017234E5BD3696EBUL),
        ]), // 16 chunks, 33 of 42 covers seen
    };

    public static IEnumerable<object[]> CaseNames => Golden.Keys.Select(k => new object[] { k });

    public static IEnumerable<object[]> AllChunks =>
        Golden.SelectMany(kv => kv.Value.Chunks.Select(c => new object[] { kv.Key, c.Cx, c.Cy }));

    private static ulong Hash(IslandChunk c)
    {
        var h = new GenHasher().I(c.Size).I(c.X0).I(c.Y0).Ds(c.Height).Is(c.Cover);
        h.I(c.Water.Length);
        foreach (float v in c.Water) h.Nan(v);
        h.Ds(c.Shade).Is(c.Biome).I(c.Label.Length);
        foreach (short v in c.Label) h.I(v);
        return h.Value;
    }

    [Theory]
    [MemberData(nameof(AllChunks))]
    public void Chunk_MatchesReference(string name, int cx, int cy)
    {
        ulong expected = Golden[name].Chunks.First(c => c.Cx == cx && c.Cy == cy).Hash;
        Assert.Equal(expected, Hash(TestIslands.Chunker(name).Build(cx, cy)));
    }

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void WideChunk_MatchesReference(string name)
    {
        Expected e = Golden[name];
        Assert.Equal(e.WideHash, Hash(TestIslands.Chunker(name).Build(e.WideX, e.WideY, 65)));
    }

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void NeighbouringChunks_AgreeExactlyAtTheirSeams(string name)
    {
        // a 128 × 128 chunk must be exactly its four 64 × 64 chunks side by side
        Expected e = Golden[name];
        IslandChunker chunker = TestIslands.Chunker(name);
        IslandChunk big = chunker.Build(e.WideX, e.WideY, 128);
        for (int qy = 0; qy < 2; qy++)
        {
            for (int qx = 0; qx < 2; qx++)
            {
                IslandChunk part = chunker.Build(e.WideX + qx, e.WideY + qy);
                for (int y = 0; y < 64; y++)
                {
                    for (int x = 0; x < 64; x++)
                    {
                        int a = y * 64 + x, b = (qy * 64 + y) * 128 + qx * 64 + x;
                        Assert.Equal(part.Height[a], big.Height[b]);
                        Assert.Equal(part.Cover[a], big.Cover[b]);
                        Assert.Equal(part.Shade[a], big.Shade[b]);
                        Assert.Equal(part.Label[a], big.Label[b]);
                    }
                }
            }
        }
    }

    [Fact]
    public void GeneticsComplex_HasWallsDoorsAndNamedRooms()
    {
        IslandWorks works = TestIslands.Works("Default");
        Site gen = works.Sites.First(t => t.Kind == "genetics");
        IslandChunk c = TestIslands.Chunker("Default").Build((int)Math.Floor(gen.X / 64), (int)Math.Floor(gen.Y / 64));
        Assert.Contains((byte)Cover.Wall, c.Cover);
        Assert.Contains((byte)Cover.Floor, c.Cover);
        Assert.Contains((byte)Cover.Door, c.Cover);
        Assert.Contains(c.Label, l => l >= 0 && works.Labels[l].StartsWith("Genetics complex · ", StringComparison.Ordinal));
    }

    [Fact]
    public void ChunkPicture_IsOneOpaquePixelPerTile()
    {
        IslandChunk c = TestIslands.Chunker("Default").Build(0, 0);
        byte[] px = IslandMapImage.RenderChunk(c);
        Assert.Equal(64 * 64 * 4, px.Length);
        for (int i = 3; i < px.Length; i += 4) Assert.Equal(255, px[i]);
        Assert.All(c.Cover, v => Assert.True(v <= (byte)Cover.Reef)); // the frame's corner is open sea
    }

    /// <summary>
    /// Every door of every building is on the map: stations far from roads and sites are drawn too
    /// (the skyway's far end used to be missing), and tram stop platforms lie beneath buildings rather
    /// than wiping out their doors (step 25a-3).
    /// </summary>
    [Theory]
    [MemberData(nameof(CaseNames))]
    public void EveryBuildingsDoors_AreDrawn(string name)
    {
        IslandWorks works = TestIslands.Works(name);
        IslandChunker chunker = TestIslands.Chunker(name);
        var chunks = new Dictionary<(int X, int Y), IslandChunk>();
        IslandChunk ChunkAt(int x, int y)
        {
            var key = ((int)Math.Floor(x / 64.0), (int)Math.Floor(y / 64.0));
            if (!chunks.TryGetValue(key, out IslandChunk? c))
                chunks[key] = c = chunker.Build(key.Item1, key.Item2);
            return c;
        }
        var missing = new List<string>();
        int doors = 0;
        foreach (Prim b in works.Prims.InOrder())
        {
            if (b.Kind != PrimKind.Building || b.Doors is null)
                continue;
            foreach ((int x, int y) in b.Doors)
            {
                doors++;
                IslandChunk c = ChunkAt(x, y);
                var cover = (Cover)c.Cover[(y - c.Y0) * c.Size + (x - c.X0)];
                if (cover != Cover.Door)
                    missing.Add($"{works.Labels[b.Label]} at ({x}, {y}): {cover}");
            }
        }
        Assert.True(missing.Count == 0, $"{missing.Count} of {doors} doors aren't drawn: {string.Join("; ", missing)}");
        Assert.Equal(name == "SmallNoCliffs" ? 323 : 315, doors); // from the island lab
    }

    /// <summary>
    /// Every building stands on level ground (step 25a-4): each one's tiles, walls and doors included, are
    /// all at one height, inside compounds and out. Buildings that overlap another are left out: on
    /// SmallNoCliffs the monorail and summit lift stations at the Control centre share ground (polish item P1).
    /// </summary>
    [Theory]
    [MemberData(nameof(CaseNames))]
    public void EveryBuilding_StandsOnLevelGround(string name)
    {
        IslandWorks works = TestIslands.Works(name);
        IslandChunker chunker = TestIslands.Chunker(name);
        var chunks = new Dictionary<(int X, int Y), IslandChunk>();
        double HeightAt(int x, int y)
        {
            var key = ((int)Math.Floor(x / 64.0), (int)Math.Floor(y / 64.0));
            if (!chunks.TryGetValue(key, out IslandChunk? c))
                chunks[key] = c = chunker.Build(key.Item1, key.Item2);
            return c.Height[(y - c.Y0) * c.Size + (x - c.X0)];
        }
        List<Prim> buildings = works.Prims.InOrder().Where(p => p.Kind == PrimKind.Building).ToList();
        bool Overlaps(Prim a) => buildings.Any(b => b != a && a.X0 <= b.X1 && a.X1 >= b.X0 && a.Y0 <= b.Y1 && a.Y1 >= b.Y0);
        var uneven = new List<string>();
        int skipped = 0;
        foreach (Prim b in buildings)
        {
            if (Overlaps(b)) { skipped++; continue; }
            double lo = double.MaxValue, hi = double.MinValue;
            for (int y = b.Y0; y <= b.Y1; y++)
            {
                for (int x = b.X0; x <= b.X1; x++)
                {
                    double h = HeightAt(x, y);
                    lo = Math.Min(lo, h); hi = Math.Max(hi, h);
                }
            }
            if (hi - lo > 0.001)
                uneven.Add($"{works.Labels[b.Label]}: {hi - lo:0.00} m");
        }
        Assert.True(uneven.Count == 0, $"{uneven.Count} buildings aren't level: {string.Join("; ", uneven)}");
        Assert.Equal(name == "SmallNoCliffs" ? 2 : 0, skipped);
    }
    /// <summary>
    /// Step 25b-3b: every outside door of a building opens level onto dry ground, within the half metre a
    /// person steps up or down, so a building can always be walked into and out of. Buildings that overlap
    /// another are left out (polish item P1). On SmallNoCliffs the monorail station at the Orgone research
    /// station still opens into the lake from both doors (polish item P2).
    /// </summary>
    [Theory]
    [MemberData(nameof(CaseNames))]
    public void EveryOuterDoor_OpensOntoLevelGround(string name)
    {
        IslandWorks works = TestIslands.Works(name);
        IslandChunker chunker = TestIslands.Chunker(name);
        var chunks = new Dictionary<(int X, int Y), IslandChunk>();
        (double Height, bool Wet) TileAt(int x, int y)
        {
            var key = ((int)Math.Floor(x / 64.0), (int)Math.Floor(y / 64.0));
            if (!chunks.TryGetValue(key, out IslandChunk? c))
                chunks[key] = c = chunker.Build(key.Item1, key.Item2);
            int i = (y - c.Y0) * c.Size + (x - c.X0);
            return (c.Height[i], !float.IsNaN(c.Water[i]));
        }
        List<Prim> buildings = works.Prims.InOrder().Where(p => p.Kind == PrimKind.Building).ToList();
        bool Overlaps(Prim a) => buildings.Any(b => b != a && a.X0 <= b.X1 && a.X1 >= b.X0 && a.Y0 <= b.Y1 && a.Y1 >= b.Y0);
        var bad = new List<string>();
        int doors = 0;
        foreach (Prim b in buildings)
        {
            if (Overlaps(b)) continue;
            foreach ((int x, int y) in b.Doors!)
            {
                int ox = x, oy = y;
                if (x == b.X0) ox--;
                else if (x == b.X1) ox++;
                else if (y == b.Y0) oy--;
                else if (y == b.Y1) oy++;
                else continue; // a door between rooms
                doors++;
                (double inside, _) = TileAt(x, y);
                (double outside, bool wet) = TileAt(ox, oy);
                if (wet || Math.Abs(outside - inside) > 0.5)
                    bad.Add($"{works.Labels[b.Label]} ({x}, {y}): {outside - inside:+0.00;-0.00} m{(wet ? ", water" : "")}");
            }
        }
        Assert.True(doors > 100, $"{doors} outer doors");
        Assert.True(bad.Count == (name == "SmallNoCliffs" ? 2 : 0), $"{bad.Count} of {doors} outer doors don't open level: {string.Join("; ", bad)}");
    }
}
