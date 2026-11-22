using BlueHarvest.Sim.World.Generation;
using Xunit;

namespace BlueHarvest.Sim.Tests;

/// <summary>
/// Stage 5 of the world-generator port: 1 m chunk detail, checked tile for tile against the island lab
/// (tools/island-lab/golden-chunks.js). The chunks were picked to cover every kind of feature: compounds,
/// docks, the runway, paddock gates and moats, rivers, lakes, caves, wrecks, ruins, transit, cliffs, the
/// pali, reefs and open sea, and the P4b anchors (the reef flat's shore, the floodplain). If earlier generator
/// tests fail too, fix those first.
/// </summary>
public class IslandChunkTests
{
    public sealed record Expected(int WideX, int WideY, ulong WideHash, (int Cx, int Cy, ulong Hash)[] Chunks);

    // node tools/island-lab/golden-chunks.js
    private static readonly Dictionary<string, Expected> Golden = new()
    {
        ["Default"] = new(597, 153, 0x7D3B905B98442DDCUL,
        [
            (0, 0, 0x13700D52B6611D5FUL),
            (671, 484, 0x06775F7FFC761AE0UL),
            (597, 153, 0x5F42CFACF7568D12UL),
            (603, 151, 0x3586622C7EFE493CUL),
            (683, 471, 0xE2959CDBFFB7B2C9UL),
            (851, 417, 0x5E625175136DFBD8UL),
            (761, 375, 0xC51E1CBD33D857DBUL),
            (845, 653, 0xFCC42EC76E90E86CUL),
            (643, 769, 0x32C80B49F7EDE363UL),
            (1081, 666, 0xB397056E2E5AD30AUL),
            (1081, 665, 0x256EA4B0B3A5D5FDUL),
            (709, 525, 0x851D95FD1EA999FCUL),
            (596, 140, 0xA1A615CA54FC6BFBUL),
            (573, 176, 0x30C30CE3C74C2B6CUL),
            (567, 181, 0x3443626F6494B63BUL),
            (782, 569, 0xB2D1E1D9F9C0500BUL),
            (831, 763, 0x66C1C3582CE2FFF1UL),
            (551, 590, 0x409570F6275BBF0FUL),
            (651, 779, 0xF62E8EC8C7708D8BUL),
            (819, 644, 0x3B655DE16E05BC4BUL),
            (565, 167, 0x260BA1A1B10D6E0BUL),
            (553, 182, 0x7F733797F7D2E8F4UL),
            (1105, 555, 0xE7CF80E858D12EF0UL),
            (1116, 538, 0x4D747B7057233CFCUL),
            (412, 138, 0x73607A6D2BCD8403UL),
            (695, 133, 0x7016378E07C8C39FUL),
            (733, 611, 0xE5D3E7B54D427F06UL),
            (529, 549, 0x29DD31CA35543A3AUL),
            (583, 143, 0xAC3F0C9D8399AED4UL),
            (145, 285, 0x0F679492E32DD933UL),
            (676, 483, 0xF45E47C75E293A78UL),
            (709, 523, 0x58929275FDD1D441UL),
            (682, 469, 0xB19ACD2936700948UL),
            (1202, 641, 0xE9645A0D78D82375UL),
            (540, 486, 0x6727ED1947C7651CUL),
            (533, 103, 0xFC22387778DD8AA3UL),
            (533, 87, 0x057F88AC1B322372UL),
            (407, 175, 0x22FB9512E6C2C25AUL),
            (687, 135, 0xF4DEC4EFB10DF1FBUL),
            (575, 157, 0xC50E07CC345CEAD5UL),
        ]), // 40 chunks, 38 of 42 covers seen
        ["Seed12"] = new(739, 361, 0x163B853CEF15D170UL,
        [
            (0, 0, 0xC596B420B947D8F8UL),
            (503, 363, 0xA2E1E49401E344C0UL),
            (739, 361, 0xEC1F1DCBE408D0C9UL),
            (737, 355, 0x49934BFD62CF197CUL),
            (469, 359, 0xD9E072ABF3A20C3BUL),
            (541, 215, 0xAAE20EC56E51266FUL),
            (571, 249, 0x296FA003B95CF6B0UL),
            (515, 419, 0xF6D3EA770E97E2FAUL),
            (475, 649, 0x5A890B01F7B1601BUL),
            (791, 540, 0x50EF39DFF14D40CAUL),
            (791, 539, 0xEC015DC66905D97FUL),
            (469, 323, 0xA9E9B74B1D0B37A4UL),
            (748, 360, 0xA4B2137BC4527EA2UL),
            (659, 358, 0xE7A41635DCBD03C3UL),
            (649, 355, 0x742546622F62E68FUL),
            (474, 305, 0x5C06C29478BFFEBAUL),
            (415, 280, 0x4DA5B516157A795CUL),
            (363, 468, 0x10088A1A7B93CE4BUL),
            (489, 651, 0x74C82F2C41D6D4B5UL),
            (503, 427, 0xCCDE03251334972EUL),
        ]), // 20 chunks, 34 of 42 covers seen
        ["SmallNoCliffs"] = new(425, 303, 0xAD588993CE0A9BBEUL,
        [
            (0, 0, 0xA8F42DA9AE204A69UL),
            (335, 242, 0xD209A392F64F2051UL),
            (425, 303, 0x3D3B9957A443E11FUL),
            (419, 299, 0xB763171CC7301B17UL),
            (339, 253, 0x6A4B748C7C121947UL),
            (375, 209, 0xBA1BD505E76C93C1UL),
            (357, 183, 0x06A6816093FE90A9UL),
            (215, 183, 0x0F6104F76B2259B6UL),
            (449, 323, 0x2938E8116631BA6BUL),
            (250, 382, 0x4E89C7EB0FC8BC12UL),
            (251, 381, 0x6DBC5BB7C30A51B9UL),
            (349, 289, 0xB81EA100B9A5F2CAUL),
            (436, 300, 0xE8AAEA0A9C248277UL),
            (385, 285, 0x3657976C61462B57UL),
            (379, 281, 0x354EEE7C52B6175CUL),
            (255, 201, 0x1233C745F2B5C2EAUL),
            (163, 177, 0x7F085D88D2A5E30CUL),
            (239, 272, 0x92BD925DA3A7F5EDUL),
            (457, 325, 0x80729F3B698073F1UL),
            (226, 191, 0xE5BCA34D936CA4A8UL),
        ]), // 20 chunks, 32 of 42 covers seen
        ["CoolWet"] = new(421, 133, 0xD31A84C7C9043100UL,
        [
            (0, 0, 0x93F85708B6401F2CUL),
            (403, 290, 0x9CF32C2BDB7BED4AUL),
            (421, 133, 0xB1C5083F815DCF6BUL),
            (425, 139, 0x592DB26861319873UL),
            (441, 301, 0xD244099DCA05DD64UL),
            (487, 229, 0xB543A8CAABC49C6AUL),
            (511, 239, 0xE7D8B275D294879DUL),
            (351, 237, 0x3517446770C11CAEUL),
            (429, 521, 0xCC31E0E9C2227CB5UL),
            (573, 438, 0xD9F510FC6652767BUL),
            (573, 437, 0xEA5651FC48269C7BUL),
            (407, 307, 0xF53A025091E4EECCUL),
            (432, 134, 0xF288534538B6E1E6UL),
            (401, 238, 0xB32490C59A9FEC54UL),
            (401, 245, 0x8F20FBCB7123A837UL),
            (458, 348, 0x57BDE0CAC147025BUL),
            (510, 395, 0xB3631E3B7CA0D2CBUL),
            (348, 381, 0x017234E5BD3696EBUL),
            (441, 523, 0x41E7D5D6394A6815UL),
            (348, 232, 0xB0ACCB1315954443UL),
        ]), // 20 chunks, 35 of 42 covers seen
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
    public void TheMenagerie_HasWallsDoorsAndNamedRooms()
    {
        IslandWorks works = TestIslands.Works("Default");
        Site gen = works.Sites.First(t => t.Kind == "genetics");
        IslandChunk c = TestIslands.Chunker("Default").Build((int)Math.Floor(gen.X / 64), (int)Math.Floor(gen.Y / 64));
        Assert.Contains((byte)Cover.Wall, c.Cover);
        Assert.Contains((byte)Cover.Floor, c.Cover);
        Assert.Contains((byte)Cover.Door, c.Cover);
        Assert.Contains(c.Label, l => l >= 0 && works.Labels[l].StartsWith(gen.Name + " · ", StringComparison.Ordinal));
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
        int expected = name switch { "Default" => 365, "Seed12" => 413, "SmallNoCliffs" => 413, _ => 381 }; // from the island lab
        Assert.Equal(expected, doors);
    }

    /// <summary>
    /// Every building stands on level ground (step 25a-4): each one's tiles, walls and doors included, are
    /// all at one height, inside compounds and out. Buildings that overlap another are left out: since the
    /// Crosswake program (P4c) none do on these islands (polish item P1, the stations sharing ground at the old
    /// Control centre, is gone).
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
        Assert.Equal(0, skipped);
    }
    /// <summary>
    /// Step 25b-3b: every outside door of a building opens level onto dry ground, within the half metre a
    /// person steps up or down, so a building can always be walked into and out of. Buildings that overlap
    /// another are left out (polish item P1). On SmallNoCliffs the monorail station at the Vitality Institute
    /// (the old Orgone research station) still opens into the lake from both doors (polish item P2).
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
