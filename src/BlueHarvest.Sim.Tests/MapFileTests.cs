using BlueHarvest.Sim.World;
using Xunit;

namespace BlueHarvest.Sim.Tests;

public class MapFileTests
{
    /// <summary>A version 1 file: heights as 0.5 m level characters, no structure sections.</summary>
    private const string SmallV1 =
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

    /// <summary>The same map as written now: decimeter heights, and the doorway listed.</summary>
    private const string Small =
        "bhmap 2\n" +
        "size 4 2\n" +
        "heights\n" +
        "0 0 50 80\n" +
        "0 5 10 15\n" +
        "cover\n" +
        ".&F#\n" +
        "==+V\n" +
        "doors\n" +
        "2 1 open\n" +
        "markers\n" +
        "player 0 1 Survivor\n" +
        "npc 1 1\n" +
        "light 3 0\n" +
        "end\n";

    /// <summary>A v2 file using every section.</summary>
    private const string Full =
        "bhmap 2\n" +
        "size 5 2\n" +
        "ambient 6\n" +
        "heights\n" +
        "-20 0 1234 80 80\n" +
        "0 0 0 80 80\n" +
        "cover\n" +
        "VV#+_\n" +
        "VV#__\n" +
        "roofs\n" +
        "....6\n" +
        "...66\n" +
        "doors\n" +
        "3 0 locked\n" +
        "vehicles\n" +
        "1 1 40 Jeep A\n" +
        "markers\n" +
        "player 4 1\n" +
        "end\n";

    [Fact]
    public void ParseV1_ReadsHeightsCoverAndMarkers()
    {
        var data = MapFile.Parse(SmallV1);

        Assert.Equal(4, data.Map.Width);
        Assert.Equal(2, data.Map.Height);
        Assert.Equal(50, data.Map.Heights[new TilePos(2, 0)]); // 'a' = 5.0 m
        Assert.Equal(80, data.Map.Heights[new TilePos(3, 0)]); // 'g' = 8.0 m
        Assert.Equal(CoverType.DenseJungle, data.Map.Cover[new TilePos(1, 0)]);
        Assert.Equal(CoverType.Fence, data.Map.Cover[new TilePos(2, 0)]);
        Assert.Equal(CoverType.Door, data.Map.Cover[new TilePos(2, 1)]);
        Assert.Equal(DoorState.Open, data.Map.Structures.DoorAt(new TilePos(2, 1)));

        Assert.Equal(3, data.Markers.Count);
        Assert.Equal(new MapMarker(MarkerKind.Player, new TilePos(0, 1), "Survivor"), data.Markers[0]);
        Assert.Null(data.Markers[1].Name);
        Assert.Equal(MarkerKind.Light, data.Markers[2].Kind);
    }

    [Fact]
    public void V1_IsWrittenBackAsV2()
    {
        Assert.Equal(Small, MapFile.Serialize(MapFile.Parse(SmallV1)));
    }

    [Fact]
    public void V1Floors_GetARoof()
    {
        var data = MapFile.Parse(SmallV1.Replace(".&F#\n", "._F#\n"));
        Assert.True(data.Map.IsIndoor(new TilePos(1, 0)));
        Assert.Equal(StructureMap.DefaultRoofDm, data.Map.Structures.RoofDm(new TilePos(1, 0)));
        Assert.False(data.Map.IsIndoor(new TilePos(0, 0)));
    }

    [Fact]
    public void V2_RoundTripsExactly()
    {
        Assert.Equal(Small, MapFile.Serialize(MapFile.Parse(Small)));
        Assert.Equal(Full, MapFile.Serialize(MapFile.Parse(Full)));
    }

    [Fact]
    public void V2_ReadsDecimetersRoofsDoorsAndVehicles()
    {
        var data = MapFile.Parse(Full);
        var map = data.Map;

        Assert.Equal(-20, map.Heights[new TilePos(0, 0)]);
        Assert.Equal(1234, map.Heights[new TilePos(2, 0)]);
        Assert.Equal(30, map.Structures.RoofDm(new TilePos(4, 0)));
        Assert.False(map.IsIndoor(new TilePos(2, 1)));
        Assert.Equal(DoorState.Locked, map.Structures.DoorAt(new TilePos(3, 0)));
        Assert.Equal("a locked door", map.CoverAt(new TilePos(3, 0)).Name);
        Assert.Equal(new VehicleSpec(new TilePos(1, 1), 40, "Jeep A"), Assert.Single(data.VehicleSpecs));
        Assert.Equal(6, data.AmbientLight);
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
    [InlineData("0 0 50 80\n", "0 0 50\n")]           // short height row
    [InlineData("0 0 50 80\n", "0 0 5x 80\n")]        // not a number
    [InlineData(".&F#\n", ".&F?\n")]                  // unknown cover
    [InlineData("npc 1 1\n", "dragon 1 1\n")]         // unknown marker kind
    [InlineData("npc 1 1\n", "npc 9 1\n")]            // marker outside the map
    [InlineData("end\n", "")]                         // missing end
    [InlineData("bhmap 2\n", "bhmap 3\n")]            // unsupported version
    [InlineData("2 1 open\n", "2 1 ajar\n")]          // unknown door state
    [InlineData("2 1 open\n", "1 1 open\n")]          // door not on a doorway
    [InlineData("doors\n", "dooors\n")]              // unknown section
    public void Parse_RejectsBrokenFiles(string find, string replace)
    {
        Assert.Throws<FormatException>(() => MapFile.Parse(Small.Replace(find, replace)));
    }

    [Theory]
    [InlineData("1 1 40 Jeep A\n", "2 1 40 Jeep A\n")]    // vehicle entry not on a vehicle
    [InlineData("1 1 40 Jeep A\n", "1 1 140 Jeep A\n")]   // integrity out of range
    [InlineData("....6\n", "....?\n")]                     // bad roof character
    [InlineData("roofs\n....6\n...66\ndoors\n3 0 locked\n", "doors\n3 0 locked\nroofs\n....6\n...66\n")] // out of order
    public void Parse_RejectsBrokenStructureSections(string find, string replace)
    {
        Assert.Throws<FormatException>(() => MapFile.Parse(Full.Replace(find, replace)));
    }

    /// <summary>A small v2 map with a raised platform (decks), a ladder and a support post.</summary>
    private const string WithPlatform =
        "bhmap 2\n" +
        "size 4 2\n" +
        "heights\n" +
        "0 0 0 0\n" +
        "0 0 0 0\n" +
        "cover\n" +
        ".|..\n" +
        "..x.\n" +
        "decks\n" +
        ".cc.\n" +
        "....\n" +
        "ladders\n" +
        "2 0\n" +
        "markers\n" +
        "player 0 1\n" +
        "end\n";

    [Fact]
    public void V2_DecksAndLadders_RoundTrip()
    {
        var data = MapFile.Parse(WithPlatform);

        Assert.Equal(60, data.Map.Structures.DeckDm(new TilePos(1, 0)));
        Assert.Equal(60, data.Map.DeckTop(new TilePos(2, 0)));
        Assert.True(data.Map.Structures.HasLadder(new TilePos(2, 0)));
        Assert.Equal(CoverType.Support, data.Map.Cover[new TilePos(1, 0)]);
        Assert.Equal(CoverType.Rubble, data.Map.Cover[new TilePos(2, 1)]);
        Assert.Equal(WithPlatform, MapFile.Serialize(data));
    }

    [Fact]
    public void Ladder_NeedsADeckAboveIt()
    {
        Assert.Throws<FormatException>(() => MapFile.Parse(WithPlatform.Replace("ladders\n2 0\n", "ladders\n3 0\n")));
    }

    [Fact]
    public void StructureSections_NeedVersion2()
    {
        Assert.Throws<FormatException>(() => MapFile.Parse(SmallV1.Replace("markers\n", "doors\n2 1 open\nmarkers\n")));
    }
}
