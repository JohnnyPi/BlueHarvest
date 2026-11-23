using BlueHarvest.Sim.World.Generation;
using Xunit;

namespace BlueHarvest.Sim.Tests;

/// <summary>
/// Stage 5 of the world-generator port: 1 m chunk detail, checked tile for tile against the island lab
/// (tools/island-lab/golden-chunks.js). The chunks were picked to cover every kind of feature: compounds,
/// docks, the runway, paddock gates and moats, rivers, lakes, caves, wrecks, ruins, transit, cliffs, the
/// pali, reefs and open sea, the P4b anchors (the reef flat's shore, the floodplain), and the six authored ruins'
/// perimeters (P4d). The hash covers the hidden ley-line field too. If earlier generator tests fail too, fix those first.
/// </summary>
public class IslandChunkTests
{
    public sealed record Expected(int WideX, int WideY, ulong WideHash, (int Cx, int Cy, ulong Hash)[] Chunks);

    // node tools/island-lab/golden-chunks.js
    private static readonly Dictionary<string, Expected> Golden = new()
    {
        ["Default"] = new(597, 153, 0x9A34750C072BD04DUL,
        [
            (0, 0, 0xBF93D0C8CD4E6CEFUL),
            (671, 484, 0xF5DD84F1347B6490UL),
            (597, 153, 0x786D63401DC435A2UL),
            (603, 151, 0xF695728655D19B0CUL),
            (683, 471, 0x5F4B3DB725ED0F7CUL),
            (851, 417, 0xCE1963C21F88F289UL),
            (761, 375, 0xA3DA44964450CEEBUL),
            (845, 653, 0x128A34B34CAAC69EUL),
            (643, 769, 0x4AF378908A1D8893UL),
            (1081, 666, 0xA54FD2F99A02EA9AUL),
            (1081, 665, 0x84F0DDFF47C5E54DUL),
            (709, 525, 0x466EB21A84FE83CCUL),
            (596, 140, 0x4DB3CF2AC1E8A70BUL),
            (573, 176, 0xF43F29937FA7FE9CUL),
            (567, 181, 0x491A61E6E040B94BUL),
            (782, 569, 0xCDB59C9F1CEB51BBUL),
            (831, 763, 0x4DB8B8A3447CD5C1UL),
            (551, 590, 0x2278A1EAC9CFC93FUL),
            (651, 779, 0x37656B2597EA3F3BUL),
            (819, 644, 0x466120C6C368C5FBUL),
            (667, 213, 0x9306B30100DFF955UL),
            (521, 503, 0x32448896992E31B2UL),
            (845, 625, 0xC2066547331EE20BUL),
            (917, 453, 0x9F9597465967CC40UL),
            (545, 105, 0xBDC4D0F854431B50UL),
            (673, 785, 0x3B9B5AA8943A5E7BUL),
            (565, 167, 0xFD227B7DC6882FBBUL),
            (553, 182, 0x5DBF631AD59D61C4UL),
            (1105, 555, 0x13E7563C4B9879E1UL),
            (1116, 538, 0xDA211F56A8CA86CCUL),
            (412, 138, 0x5B0A26A9181CB4B3UL),
            (695, 133, 0xC8AE04CBF3F95B2FUL),
            (733, 611, 0x1102937B5AD2EE16UL),
            (529, 549, 0xEE307D1AE167732AUL),
            (583, 143, 0x87CB42413F4FA3A4UL),
            (145, 285, 0x3C4D67746B91EB43UL),
            (676, 483, 0x944E70B493CC6728UL),
            (709, 523, 0x1F00099196BAF8B1UL),
            (682, 469, 0x7D4313B1E8C98DD8UL),
            (1202, 641, 0x8774CCA7159E91C5UL),
            (540, 486, 0x37F29A15BFACF2ECUL),
            (533, 103, 0x0CA15A969DB7CF53UL),
            (533, 87, 0xD3B3BAE0CEEA5802UL),
            (407, 175, 0x44018FB8B2203F4AUL),
            (687, 135, 0xE733496056E6ED0BUL),
            (575, 157, 0x5E3AA5A31CBE0525UL),
        ]), // 46 chunks, 38 of 42 covers seen
        ["Seed12"] = new(739, 361, 0xEFADBC121509B9E1UL,
        [
            (0, 0, 0xF109D551430FD5A8UL),
            (503, 363, 0x3F1DD7E689888A70UL),
            (739, 361, 0x8F8FF2161C133199UL),
            (737, 355, 0x7505B0FFCC2EF34CUL),
            (469, 359, 0x4023CC9575716FCDUL),
            (541, 215, 0x1EEAE075B464C3DEUL),
            (571, 249, 0xA47B5DAA535D7F00UL),
            (515, 419, 0xB59BC673C06A2CAAUL),
            (475, 649, 0xCA2E4DC8D2AF42ABUL),
            (791, 540, 0x4429C5FBF0BC905AUL),
            (791, 539, 0xA3AC6831650BED0FUL),
            (469, 323, 0xECE8D0C7C188C1D4UL),
            (748, 360, 0x0E9F3E63F3DC5492UL),
            (659, 358, 0x96202922B5A9AC73UL),
            (649, 355, 0x5D90F2F0B3A6E0BFUL),
            (474, 305, 0xBF3E77CD1598C7AAUL),
            (415, 280, 0xB33A2D20C7DD0F2CUL),
            (363, 468, 0xBEF238C505ED17FBUL),
            (489, 651, 0xAB07A8C9EAEFEB05UL),
            (503, 427, 0xDC831AB146E5F69EUL),
            (685, 321, 0x23D16B0204B7F975UL),
            (379, 453, 0x1DD1C421FF3EC401UL),
            (475, 431, 0xEBA4404E31838EF1UL),
            (497, 211, 0xEB910F7706503C3BUL),
            (341, 79, 0x0D33EE3AC3250F06UL),
            (491, 657, 0xD91BD470D4D01AC5UL),
        ]), // 26 chunks, 36 of 42 covers seen
        ["SmallNoCliffs"] = new(425, 303, 0x0AA4868A519706AFUL,
        [
            (0, 0, 0xD147EE3669E21F39UL),
            (335, 242, 0xC64EBA857F28C221UL),
            (425, 303, 0x548899B29BDF28AFUL),
            (419, 299, 0x998CD464A051F647UL),
            (339, 253, 0xFDA3A6D97C50F617UL),
            (375, 209, 0x10E40533ABCB7B91UL),
            (357, 183, 0x7FCC28769E17AD79UL),
            (215, 183, 0x0D0D44C9F913B1A6UL),
            (449, 323, 0x7BB5FD108F717F1FUL),
            (250, 382, 0xA363360DC51244A2UL),
            (251, 381, 0xFC90A2575F3FD529UL),
            (349, 289, 0x41853250799F825AUL),
            (436, 300, 0x8B7C495AD31509A7UL),
            (385, 285, 0xFDA02484DA1C8E87UL),
            (379, 281, 0xD33BEB8C26386D2CUL),
            (255, 201, 0xA74EAA2B2CF2967AUL),
            (163, 177, 0x87F4E9BF3AECEA3CUL),
            (239, 272, 0x843AFC54D8FD07DDUL),
            (457, 325, 0xCDBB497CCC50C9C1UL),
            (226, 191, 0xEC41B9DD08249BF8UL),
            (423, 259, 0xFFB13C37E7F1DFAFUL),
            (265, 279, 0x49E73115997019D2UL),
            (235, 179, 0xF32EA70C88FF5847UL),
            (417, 211, 0x77535B68347B181AUL),
            (461, 331, 0xD2EB9C2B7DC763D8UL),
        ]), // 25 chunks, 34 of 42 covers seen
        ["CoolWet"] = new(421, 133, 0x35809B257468F611UL,
        [
            (0, 0, 0xDC43221E994FEA5CUL),
            (403, 290, 0x873811B3631DCCDAUL),
            (421, 133, 0xAE67F039D06A7D1BUL),
            (425, 139, 0x47076A58BA961283UL),
            (441, 301, 0x1745DBF42FB52352UL),
            (487, 229, 0x8DD6445F26B713FAUL),
            (511, 239, 0x90C42ACD73830AEDUL),
            (351, 237, 0xC2FEBF8660E04D5CUL),
            (429, 521, 0x93CD0FF160C31F81UL),
            (573, 438, 0x235720A89F89018BUL),
            (573, 437, 0xCAE170A454DDE78BUL),
            (407, 307, 0xB22C0F107B32EDFCUL),
            (432, 134, 0x697EB4F4D6486CF6UL),
            (401, 238, 0x6D316CD30A649124UL),
            (401, 245, 0x40905E9962A26767UL),
            (458, 348, 0xADC375A4DF5DC96BUL),
            (510, 395, 0x259759B38867AC7BUL),
            (348, 381, 0xC919720AA087349BUL),
            (441, 523, 0x9BC2DD1ABECFAA65UL),
            (348, 232, 0xAB5622FF5977F073UL),
            (431, 167, 0xD92FE549E24982ACUL),
            (357, 339, 0xB80B156F4FA5726AUL),
            (337, 241, 0x105DFD595E882B18UL),
            (445, 197, 0xF1515CBCADAB36C4UL),
            (335, 63, 0xDA9575322FE3F087UL),
            (443, 527, 0x1DE41F2ACD757B8DUL),
        ]), // 26 chunks, 37 of 42 covers seen
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
        h.Is(c.Ley); // the hidden ley-line field (P4d)
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
                        Assert.Equal(part.Ley[a], big.Ley[b]);
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

    // ---------- the authored ruins and the ley lines (P4d) ----------

    /// <summary>Every tile of a square round a point, from whichever chunks cover it.</summary>
    private static List<(int X, int Y, Cover Cover, short Label, float Height, float Water)> TilesAround(string name, double cx, double cy, double half)
    {
        IslandChunker chunker = TestIslands.Chunker(name);
        var chunks = new Dictionary<(int Cx, int Cy), IslandChunk>();
        var tiles = new List<(int, int, Cover, short, float, float)>();
        for (int y = (int)Math.Floor(cy - half); y < cy + half; y++)
        {
            for (int x = (int)Math.Floor(cx - half); x < cx + half; x++)
            {
                (int Cx, int Cy) key = ((int)Math.Floor(x / 64.0), (int)Math.Floor(y / 64.0));
                if (!chunks.TryGetValue(key, out IslandChunk? c)) chunks[key] = c = chunker.Build(key.Cx, key.Cy);
                int i = (y - c.Y0) * c.Size + (x - c.X0);
                tiles.Add((x, y, (Cover)c.Cover[i], c.Label[i], c.Height[i], c.Water[i]));
            }
        }
        return tiles;
    }

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void TheAuthoredRuins_AreDrawnAndNamed_AndTheirLabelsComeLast(string name)
    {
        IslandWorks works = TestIslands.Works(name);
        var drawn = works.AuthoredRuins.Where(q => q.Placed && q.Id != "RUIN-07").ToList();
        Assert.True(drawn.Count >= 5, $"only {drawn.Count} authored ruins drawn");
        // their names close the label list, in order, so no earlier label moved
        Assert.Equal(drawn.Select(q => q.Name), works.Labels.Skip(works.RuinLabelStart).Where(l => !l.EndsWith(" · Causeway", StringComparison.Ordinal)));
        foreach (AuthoredRuin q in drawn)
        {
            Assert.Equal(q.Name, works.Labels[q.Label]);
            int walls = 0, inside = 0, named = 0;
            foreach (var t in TilesAround(name, q.X, q.Y, q.R + 20))
            {
                if (t.Label == q.Label && t.Cover is Cover.Ruin or Cover.Rubble) walls++;
                double dx = t.X + 0.5 - q.X, dy = t.Y + 0.5 - q.Y;
                if (Math.Sqrt(dx * dx + dy * dy) <= q.R)
                {
                    inside++;
                    if (t.Label == q.Label) named++;
                }
            }
            // every perimeter is a real structure (the stair, the smallest, has about 420 wall tiles), named all through
            Assert.True(walls >= 250, $"{q.Id} has only {walls} wall tiles");
            Assert.True(named >= inside * 0.95, $"{q.Id}: only {named} of the {inside} tiles inside it are named");
        }
    }

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void Ruin06_StandsDryAndWholeOnTheReefFlat(string name)
    {
        AuthoredRuin q = TestIslands.Works(name).AuthoredRuins.Single(r => r.Id == "RUIN-06");
        Assert.True(q.Placed);
        int stones = 0, flat = 0;
        foreach (var t in TilesAround(name, q.X, q.Y, q.R + 4))
        {
            if (t.Label != q.Label) continue;
            if (t.Cover is Cover.Ruin or Cover.Rubble or Cover.Floor)
            {
                stones++;
                Assert.True(float.IsNaN(t.Water), $"a stone of RUIN-06 at ({t.X}, {t.Y}) is under water");
                Assert.True(t.Height >= 1.6f, $"a stone of RUIN-06 at ({t.X}, {t.Y}) stands only {t.Height:0.00} m");
            }
            else if (!float.IsNaN(t.Water)) flat++;
        }
        Assert.True(stones >= 500, $"only {stones} stone tiles");
        Assert.True(flat > stones, "the reef flat should still lie between the rings, to be waded at low tide");
    }

    /// <summary>A point's distance to a ley line, as the chunker measures it.</summary>
    private static double LeyDistance(LeyLineRoute line, double x, double y)
    {
        double best = double.MaxValue;
        for (int k = 0; k + 1 < line.Xs.Length; k++)
        {
            double ax = line.Xs[k], ay = line.Ys[k], dx = line.Xs[k + 1] - ax, dy = line.Ys[k + 1] - ay, l2 = dx * dx + dy * dy;
            if (l2 == 0) l2 = 1;
            double t = ((x - ax) * dx + (y - ay) * dy) / l2;
            t = t < 0 ? 0 : t > 1 ? 1 : t;
            best = Math.Min(best, GenMath.Hyp(ax + dx * t - x, ay + dy * t - y));
        }
        return best;
    }

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void TheLeyField_IsTheNearestLineWithin16m(string name)
    {
        IslandWorks works = TestIslands.Works(name);
        IslandChunker chunker = TestIslands.Chunker(name);
        IslandAnchors frame = TestIslands.Shape(name).Anchors;
        List<LeyLineRoute> lines = works.Program.Lines;
        Assert.Equal(4, lines.Count);
        var chunks = new Dictionary<(int, int), IslandChunk>();
        int checkedTiles = 0, onTheirLine = 0;
        for (int n = 0; n < lines.Count; n++)
        {
            LeyLineRoute line = lines[n];
            for (int k = 0; k + 1 < line.Xs.Length; k++)
            {
                double ax = line.Xs[k], ay = line.Ys[k], len = GenMath.Hyp(line.Xs[k + 1] - ax, line.Ys[k + 1] - ay);
                double ux = (line.Xs[k + 1] - ax) / len, uy = (line.Ys[k + 1] - ay) / len;
                for (double along = 500; along < len; along += 1499)
                {
                    double px = ax + ux * along, py = ay + uy * along;
                    if (px < 64 || py < 64 || px > frame.W - 64 || py > frame.H - 64) continue;
                    foreach (double across in new[] { 0.0, 9, 15, 17, 30 })
                    {
                        int tx = (int)Math.Floor(px - uy * across), ty = (int)Math.Floor(py + ux * across);
                        (int, int) key = ((int)Math.Floor(tx / 64.0), (int)Math.Floor(ty / 64.0));
                        if (!chunks.TryGetValue(key, out IslandChunk? c)) chunks[key] = c = chunker.Build(key.Item1, key.Item2);
                        // what the field should say: the nearest line within 16 m of the tile's centre, the lower number on a tie
                        double best = IslandChunker.LeyHalf;
                        byte want = 0;
                        for (int m = 0; m < lines.Count; m++)
                        {
                            double d = LeyDistance(lines[m], tx + 0.5, ty + 0.5);
                            if (d < best) { best = d; want = (byte)(m + 1); }
                        }
                        if (Math.Abs(best - IslandChunker.LeyHalf) < 1e-6) continue; // right on the edge
                        Assert.Equal(want, c.Ley[(ty - c.Y0) * c.Size + (tx - c.X0)]);
                        checkedTiles++;
                        if (across == 0 && want == n + 1) onTheirLine++;
                    }
                }
            }
        }
        Assert.True(checkedTiles >= 200, $"only {checkedTiles} tiles checked");
        Assert.True(onTheirLine >= 30, $"only {onTheirLine} tiles found on their own line");
    }
}
