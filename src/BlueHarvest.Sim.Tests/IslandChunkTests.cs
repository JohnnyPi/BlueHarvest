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
            (561, 279, 0x79E2C1CD7109E9B2UL),
            (315, 458, 0x4A79F5B8DDC193D2UL),
            (315, 457, 0x8DB3A6277A35E243UL),
            (533, 401, 0xFCC4157A7A73D4A8UL),
            (458, 107, 0x990A5216DCA78640UL),
            (448, 160, 0xEC9CDC046A080CE9UL),
            (449, 167, 0x3171E8FFE0C50611UL),
            (585, 431, 0xFDD6CE43339CF14AUL),
            (621, 572, 0x61F3BE97313F3BC9UL),
            (413, 442, 0x481A4161ABF8627FUL),
            (418, 132, 0x036399692D8EF766UL),
            (408, 140, 0xC7BDC27B6E2E5115UL),
            (575, 307, 0xACBA6ADD77FCA6BAUL),
            (574, 360, 0x512AF40F8DCAD253UL),
            (202, 194, 0x3456D0DEEED5577EUL),
            (747, 333, 0x79BD3107E075DBE6UL),
            (571, 551, 0x9037B2D106113F60UL),
            (679, 327, 0xA42A102A8F7DF6D5UL),
            (871, 519, 0x9487187656DA9829UL),
            (103, 213, 0x8FD09325815AC614UL),
            (493, 569, 0x1EE95B6EF7B10E7BUL),
            (527, 271, 0xDA53F7EDDE418CF1UL),
            (559, 279, 0xD91037275DE2ED40UL),
            (451, 114, 0x00424476E313E6C4UL),
            (562, 269, 0xC006A7C71707CB68UL),
            (776, 485, 0xEAFA2B6F6BFE222DUL),
            (391, 221, 0x4459ECEB7594347BUL),
            (393, 75, 0x20D81CD3620781D8UL),
            (393, 59, 0x033DEE7346082A22UL),
            (631, 133, 0xDB9C3F24A0A598B0UL),
            (309, 115, 0xACA809D19271FD7FUL),
            (435, 121, 0x5E4FF86F81A1918DUL),
        ]), // 38 chunks, 40 of 42 covers seen
        ["Seed12"] = new(357, 87, 0x8BF5FDA3CBB115B2UL,
        [
            (0, 0, 0xC596B420B947D8F8UL),
            (503, 363, 0xA2E1E49401E344C0UL),
            (357, 87, 0xEAABB179E855EBB3UL),
            (363, 89, 0x26623EC9114015BEUL),
            (559, 441, 0xA84CE249BFEC206EUL),
            (545, 225, 0x951BB6A55748ACFDUL),
            (571, 249, 0x6F8E38F4EEA9D5F7UL),
            (791, 540, 0xD7E5EFAEE252C4B2UL),
            (791, 539, 0x366D50732BDD76EDUL),
            (469, 323, 0xB08F337897FD21EDUL),
            (372, 90, 0xF3F2608AC37D97E8UL),
            (331, 162, 0xC4021F9A84712A4EUL),
            (327, 169, 0x71A55A0CE96AD8DAUL),
            (474, 305, 0x4EE7E470182EFD51UL),
            (415, 280, 0xBCC77E8C5279530EUL),
            (363, 468, 0x10088A1A7B93CE4BUL),
        ]), // 16 chunks, 32 of 42 covers seen
        ["SmallNoCliffs"] = new(419, 333, 0x6E30405E4F9D0397UL,
        [
            (0, 0, 0xA8F42DA9AE204A69UL),
            (335, 242, 0xD209A392F64F2051UL),
            (419, 333, 0x47F483110EE5642EUL),
            (429, 335, 0x8E3D38B210CA152BUL),
            (181, 175, 0xAA242ACDE0AA20E8UL),
            (367, 205, 0x2B14A028678A4F76UL),
            (357, 183, 0x4D80690E0BA28F16UL),
            (250, 382, 0x4A8B4C00E83A7079UL),
            (251, 381, 0x9A736B4163784BF3UL),
            (349, 289, 0x19640DBCB104F0FCUL),
            (416, 319, 0x0C59F2704ECD758CUL),
            (376, 277, 0xF62A690346C1145BUL),
            (371, 271, 0x648124AF4BE62441UL),
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
            (537, 271, 0x692877D7A75AF461UL),
            (511, 239, 0x65A1FACE9BE4E9C5UL),
            (573, 438, 0x211CBEF2FBEAC1E3UL),
            (573, 437, 0x2CFDFAE68DD5153AUL),
            (407, 307, 0xF53A025091E4EECCUL),
            (432, 132, 0x32321436D96B7A53UL),
            (403, 236, 0xBCBFB6E16FB6060FUL),
            (403, 243, 0x7C30B542FE478AF8UL),
            (458, 348, 0x3D9C4DACE942AAB7UL),
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
}
