using BlueHarvest.Sim.World;
using Xunit;

namespace BlueHarvest.Sim.Tests;

public class MapFileTests
{
    private const string Small =
        "bhmap 1\n" +
        "size 4 2\n" +
        "heights\n" +
        "00ag\n" +
        "0123\n" +
        "cover\n" +
        ".&F#\n" +
        "==+V\n" +
        "markers\n" +
        "player 0 1 Survivor\n" +
        "npc 1 1\n" +
        "light 3 0\n" +
        "end\n";

    [Fact]
    public void Parse_ReadsHeightsCoverAndMarkers()
    {
        var data = MapFile.Parse(Small);

        Assert.Equal(4, data.Map.Width);
        Assert.Equal(2, data.Map.Height);
        Assert.Equal(50, data.Map.Heights[new TilePos(2, 0)]); // 'a' = 5.0 m
        Assert.Equal(80, data.Map.Heights[new TilePos(3, 0)]); // 'g' = 8.0 m
        Assert.Equal(CoverType.DenseJungle, data.Map.Cover[new TilePos(1, 0)]);
        Assert.Equal(CoverType.Fence, data.Map.Cover[new TilePos(2, 0)]);
        Assert.Equal(CoverType.Door, data.Map.Cover[new TilePos(2, 1)]);

        Assert.Equal(3, data.Markers.Count);
        Assert.Equal(new MapMarker(MarkerKind.Player, new TilePos(0, 1), "Survivor"), data.Markers[0]);
        Assert.Null(data.Markers[1].Name);
        Assert.Equal(MarkerKind.Light, data.Markers[2].Kind);
    }

    [Fact]
    public void Serialize_RoundTripsExactly()
    {
        Assert.Equal(Small, MapFile.Serialize(MapFile.Parse(Small)));
    }

    [Fact]
    public void Parse_AcceptsWindowsLineEndingsAndComments()
    {
        string text = "; testbed\r\n" + Small.Replace("\n", "\r\n").Replace("markers\r\n", "markers\r\n; spawns\r\n");
        Assert.Equal(Small, MapFile.Serialize(MapFile.Parse(text)));
    }

    [Fact]
    public void Ambient_IsOptional_AndRoundTrips()
    {
        Assert.Equal(100, MapFile.Parse(Small).AmbientLight);

        string night = Small.Replace("size 4 2\n", "size 4 2\nambient 6\n");
        var data = MapFile.Parse(night);
        Assert.Equal(6, data.AmbientLight);
        Assert.Equal(night, MapFile.Serialize(data));
    }

    [Fact]
    public void Headlight_IsAMarkerKind()
    {
        var data = MapFile.Parse(Small.Replace("light 3 0", "headlight 3 1"));
        Assert.Equal(MarkerKind.Headlight, data.Markers[2].Kind);
    }

    [Theory]
    [InlineData("size 4 2\n", "size 4 2\nambient 300\n")] // ambient out of range
    [InlineData("00ag\n", "00a\n")]               // short height row
    [InlineData(".&F#\n", ".&F?\n")]              // unknown cover
    [InlineData("npc 1 1\n", "dragon 1 1\n")]     // unknown marker kind
    [InlineData("npc 1 1\n", "npc 9 1\n")]        // marker outside the map
    [InlineData("end\n", "")]                     // missing end
    [InlineData("bhmap 1\n", "bhmap 2\n")]        // unsupported version
    public void Parse_RejectsBrokenFiles(string find, string replace)
    {
        Assert.Throws<FormatException>(() => MapFile.Parse(Small.Replace(find, replace)));
    }
}
