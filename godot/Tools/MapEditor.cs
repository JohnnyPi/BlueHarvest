#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BlueHarvest.Sim;
using BlueHarvest.Sim.World;
using Godot;

namespace BlueHarvest.Game.Tools;

/// <summary>
/// Editor-only map tool. Put it on the root of a scene, click "Import from file" in the Inspector,
/// paint the Heights, Cover, Roofs, Decks, Ladders, Doors and Markers layers with Godot's TileMap tools, then
/// "Export to file". The .bhmap file is the source of truth; this scene is just a way to edit it.
/// Export always writes map format v2; Import reads v1 and v2.
/// </summary>
[Tool]
public partial class MapEditor : Node2D
{
    private const string HeightsLayer = "Heights";
    private const string CoverLayer = "Cover";
    private const string RoofsLayer = "Roofs";
    private const string DecksLayer = "Decks";
    private const string LaddersLayer = "Ladders";
    private const string DoorsLayer = "Doors";
    private const string MarkersLayer = "Markers";

    private Vector2I _mapSize = new(96, 64);

    [Export(PropertyHint.File, "*.bhmap")]
    public string MapPath { get; set; } = "res://Maps/testbed.bhmap";

    /// <summary>Map size in tiles. Set by Import; change it to grow or shrink the map before exporting.</summary>
    [Export]
    public Vector2I MapSize
    {
        get => _mapSize;
        set
        {
            _mapSize = new Vector2I(Math.Max(1, value.X), Math.Max(1, value.Y));
            if (IsInsideTree())
                QueueRedraw();
        }
    }

    /// <summary>Outdoor light before any lights, 0-100: 100 is daylight, about 5 a stormy night.</summary>
    [Export(PropertyHint.Range, "0,100")]
    public int AmbientLight { get; set; } = 100;

    /// <summary>Starting rain, 0-100. Masks sound, washes out scent, shortens sight.</summary>
    [Export(PropertyHint.Range, "0,100")]
    public int Rain { get; set; }

    /// <summary>Where the wind blows from (it carries scent the other way).</summary>
    [Export(PropertyHint.Enum, "N,NE,E,SE,S,SW,W,NW")]
    public int WindFrom { get; set; } = (int)BlueHarvest.Sim.World.Direction.East;

    /// <summary>Wind speed in m/s, 0 calm to 20.</summary>
    [Export(PropertyHint.Range, "0,20")]
    public int WindSpeed { get; set; }

    /// <summary>Fog, 0-100. Shortens sight; sound is unaffected.</summary>
    [Export(PropertyHint.Range, "0,100")]
    public int Fog { get; set; }

    /// <summary>Lightning and thunder.</summary>
    [Export]
    public bool Storm { get; set; }

    /// <summary>Marker names by tile, kept so names survive a round trip. A moved marker loses its name.</summary>
    [Export]
    public Godot.Collections.Dictionary<Vector2I, string> MarkerNames { get; set; } = new();

    /// <summary>Vehicle names, keyed by any one tile of the vehicle. Vehicles without one are numbered.</summary>
    [Export]
    public Godot.Collections.Dictionary<Vector2I, string> VehicleNames { get; set; } = new();

    /// <summary>Vehicle starting integrity (0-100), keyed by any one tile of the vehicle. Default 100.</summary>
    [Export]
    public Godot.Collections.Dictionary<Vector2I, int> VehicleIntegrity { get; set; } = new();

    [ExportToolButton("Import from file")]
    public Callable ImportButton => Callable.From(Import);

    [ExportToolButton("Export to file")]
    public Callable ExportButton => Callable.From(Export);

    public override void _Draw()
    {
        // Map boundary, slightly outside the tiles so the layers don't cover it.
        var size = new Vector2(MapSize.X * MapTileSets.TilePx, MapSize.Y * MapTileSets.TilePx);
        DrawRect(new Rect2(new Vector2(-2, -2), size + new Vector2(4, 4)), new Color(1f, 1f, 1f, 0.8f), false, 2f);
    }

    private void Import()
    {
        string path = ProjectSettings.GlobalizePath(MapPath);
        if (!File.Exists(path))
        {
            GD.PushError($"Map file not found: {path}");
            return;
        }

        MapData data;
        try
        {
            data = MapFile.Parse(File.ReadAllText(path));
        }
        catch (FormatException e)
        {
            GD.PushError($"Could not read {MapPath}: {e.Message}");
            return;
        }

        var map = data.Map;
        MapSize = new Vector2I(map.Width, map.Height);
        AmbientLight = data.AmbientLight;
        var weather = data.StartingWeather;
        Rain = weather.RainPercent;
        WindFrom = (int)weather.WindFrom;
        WindSpeed = weather.WindSpeed;
        Fog = weather.FogPercent;
        Storm = weather.Storm;

        var heights = EnsureLayer(HeightsLayer, MapTileSets.Heights());
        var cover = EnsureLayer(CoverLayer, MapTileSets.Cover());
        var roofs = EnsureLayer(RoofsLayer, MapTileSets.Roofs());
        var decks = EnsureLayer(DecksLayer, MapTileSets.Decks());
        var ladders = EnsureLayer(LaddersLayer, MapTileSets.Ladders());
        var doors = EnsureLayer(DoorsLayer, MapTileSets.Doors());
        var markers = EnsureLayer(MarkersLayer, MapTileSets.Markers());
        OrderLayers();
        heights.Clear();
        cover.Clear();
        roofs.Clear();
        decks.Clear();
        ladders.Clear();
        doors.Clear();
        markers.Clear();
        MarkerNames.Clear();
        VehicleNames.Clear();
        VehicleIntegrity.Clear();

        int rounded = 0;
        for (int y = 0; y < map.Height; y++)
        {
            for (int x = 0; x < map.Width; x++)
            {
                var p = new TilePos(x, y);
                var cell = new Vector2I(x, y);

                // The palette holds 0.5 m levels from 0 to 17.5 m; other heights are rounded to fit.
                int dm = map.Heights[p];
                int level = Math.Clamp((dm + HeightMap.DecimetersPerLevel / 2) / HeightMap.DecimetersPerLevel, 0, HeightMap.MaxLevel);
                if (level * HeightMap.DecimetersPerLevel != dm)
                    rounded++;
                heights.SetCell(cell, 0, new Vector2I(level, 0));

                var type = map.Cover[p];
                if (type != CoverType.Grass)
                    cover.SetCell(cell, 0, new Vector2I((int)type, 0));

                int roofDm = map.Structures.RoofDm(p);
                if (roofDm > 0)
                {
                    int roofLevel = Math.Clamp((roofDm + HeightMap.DecimetersPerLevel / 2) / HeightMap.DecimetersPerLevel, 1, HeightMap.MaxLevel);
                    roofs.SetCell(cell, 0, new Vector2I(roofLevel, 0));
                }

                int deckDm = map.Structures.DeckDm(p);
                if (deckDm > 0)
                {
                    int deckLevel = Math.Clamp((deckDm + HeightMap.DecimetersPerLevel / 2) / HeightMap.DecimetersPerLevel, 1, HeightMap.MaxLevel);
                    decks.SetCell(cell, 0, new Vector2I(deckLevel, 0));
                }
                if (map.Structures.HasLadder(p))
                    ladders.SetCell(cell, 0, Vector2I.Zero);

                if (type == CoverType.Door)
                    doors.SetCell(cell, 0, new Vector2I((int)map.Structures.DoorAt(p), 0));
            }
        }

        foreach (var marker in data.Markers)
        {
            var cell = new Vector2I(marker.Position.X, marker.Position.Y);
            markers.SetCell(cell, 0, new Vector2I((int)marker.Kind, 0));
            if (marker.Name is not null)
                MarkerNames[cell] = marker.Name;
        }

        foreach (var vehicle in data.VehicleSpecs)
        {
            var cell = new Vector2I(vehicle.At.X, vehicle.At.Y);
            VehicleIntegrity[cell] = vehicle.Integrity;
            if (vehicle.Name is not null)
                VehicleNames[cell] = vehicle.Name;
        }

        if (rounded > 0)
            GD.PushWarning($"{rounded} heights weren't whole 0.5 m levels from 0 to 17.5 m; they were rounded to fit the palette.");

        QueueRedraw();
        GD.Print($"Imported {MapPath}: {map.Width}x{map.Height}, {data.Markers.Count} markers, {data.VehicleSpecs.Count} named vehicles.");
    }

    private void Export()
    {
        var heights = GetNodeOrNull<TileMapLayer>(HeightsLayer);
        var cover = GetNodeOrNull<TileMapLayer>(CoverLayer);
        var markers = GetNodeOrNull<TileMapLayer>(MarkersLayer);
        if (heights is null || cover is null || markers is null)
        {
            GD.PushError("Map layers are missing. Click 'Import from file' first.");
            return;
        }
        // Maps imported before v2 have no Roofs or Doors layer yet; create them empty.
        var roofs = GetNodeOrNull<TileMapLayer>(RoofsLayer) ?? EnsureLayer(RoofsLayer, MapTileSets.Roofs());
        var doors = GetNodeOrNull<TileMapLayer>(DoorsLayer) ?? EnsureLayer(DoorsLayer, MapTileSets.Doors());
        var decks = GetNodeOrNull<TileMapLayer>(DecksLayer) ?? EnsureLayer(DecksLayer, MapTileSets.Decks());
        var ladders = GetNodeOrNull<TileMapLayer>(LaddersLayer) ?? EnsureLayer(LaddersLayer, MapTileSets.Ladders());
        OrderLayers();

        var map = new WorldMap(MapSize.X, MapSize.Y);
        int coverCount = Covers.All.Count;
        int doorStates = Enum.GetValues<DoorState>().Length;

        for (int y = 0; y < map.Height; y++)
        {
            for (int x = 0; x < map.Width; x++)
            {
                var p = new TilePos(x, y);
                var cell = new Vector2I(x, y);

                if (heights.GetCellSourceId(cell) != -1)
                    map.Heights.SetLevel(p, Math.Clamp(heights.GetCellAtlasCoords(cell).X, 0, HeightMap.MaxLevel));

                if (cover.GetCellSourceId(cell) != -1)
                {
                    int index = cover.GetCellAtlasCoords(cell).X;
                    if (index >= 0 && index < coverCount)
                        map.Cover[p] = (CoverType)index;
                }

                if (roofs.GetCellSourceId(cell) != -1)
                {
                    int level = Math.Clamp(roofs.GetCellAtlasCoords(cell).X, 0, HeightMap.MaxLevel);
                    map.Structures.SetRoof(p, level * HeightMap.DecimetersPerLevel);
                }

                if (decks.GetCellSourceId(cell) != -1)
                {
                    int level = Math.Clamp(decks.GetCellAtlasCoords(cell).X, 0, HeightMap.MaxLevel);
                    map.Structures.SetDeck(p, level * HeightMap.DecimetersPerLevel);
                }
            }
        }

        foreach (var cell in ladders.GetUsedCells())
        {
            var p = new TilePos(cell.X, cell.Y);
            if (!map.InBounds(p) || !map.Structures.HasDeck(p))
            {
                GD.PushWarning($"Skipping ladder at {p}: there's no deck above it.");
                continue;
            }
            map.Structures.SetLadder(p, true);
        }

        foreach (var cell in doors.GetUsedCells())
        {
            var p = new TilePos(cell.X, cell.Y);
            int state = doors.GetCellAtlasCoords(cell).X;
            if (!map.InBounds(p) || map.Cover[p] != CoverType.Door || state < 0 || state >= doorStates)
            {
                GD.PushWarning($"Skipping door at {p}: not on a doorway tile ('+').");
                continue;
            }
            map.Structures.SetDoor(p, (DoorState)state);
        }

        var list = new List<MapMarker>();
        int kindCount = Enum.GetValues<MarkerKind>().Length;
        foreach (var cell in markers.GetUsedCells())
        {
            var position = new TilePos(cell.X, cell.Y);
            int kind = markers.GetCellAtlasCoords(cell).X;
            if (!map.InBounds(position) || kind < 0 || kind >= kindCount)
            {
                GD.PushWarning($"Skipping marker at {position}: outside the map or not a marker tile.");
                continue;
            }
            list.Add(new MapMarker((MarkerKind)kind, position, MarkerNames.TryGetValue(cell, out var name) ? name : null));
        }

        // Stable order so the file only changes when the map does.
        var sorted = list
            .OrderBy(m => m.Kind)
            .ThenBy(m => m.Position.Y)
            .ThenBy(m => m.Position.X)
            .ToList();

        int players = sorted.Count(m => m.Kind == MarkerKind.Player);
        if (players != 1)
            GD.PushWarning($"The map has {players} player markers; the game needs exactly one.");

        var vehicles = VehicleSpecs(map);
        string path = ProjectSettings.GlobalizePath(MapPath);
        var weather = new WeatherSettings(Rain, (BlueHarvest.Sim.World.Direction)Math.Clamp(WindFrom, 0, 7), WindSpeed, Fog, Storm).Clamped();
        File.WriteAllText(path, MapFile.Serialize(new MapData(map, sorted, Math.Clamp(AmbientLight, 0, 100), vehicles, weather)));
        GD.Print($"Exported {MapPath} (format v{MapFile.FormatVersion}): {map.Width}x{map.Height}, {sorted.Count} markers, {vehicles.Count} named vehicles.");
    }

    /// <summary>
    /// One spec per vehicle that has a name or integrity set, keyed by its first tile so the file is
    /// stable. Entries that no longer sit on a vehicle are dropped with a warning.
    /// </summary>
    private List<VehicleSpec> VehicleSpecs(WorldMap map)
    {
        var probe = new Simulation(map, seed: 0); // finds the vehicles; nothing runs
        var specs = new Dictionary<int, VehicleSpec>();
        foreach (var cell in VehicleNames.Keys.Concat(VehicleIntegrity.Keys).Distinct())
        {
            var p = new TilePos(cell.X, cell.Y);
            if (probe.VehicleAt(p) is not { } vehicle)
            {
                GD.PushWarning($"Vehicle entry at {p} isn't on a vehicle tile any more; it was dropped.");
                continue;
            }
            var current = specs.TryGetValue(vehicle.Id, out var existing) ? existing : new VehicleSpec(vehicle.Anchor, Vehicle.MaxIntegrity);
            if (VehicleIntegrity.TryGetValue(cell, out int integrity))
                current = current with { Integrity = Math.Clamp(integrity, 0, Vehicle.MaxIntegrity) };
            if (VehicleNames.TryGetValue(cell, out var name) && !string.IsNullOrWhiteSpace(name))
                current = current with { Name = name.Trim() };
            specs[vehicle.Id] = current;
        }
        return specs.OrderBy(s => s.Key).Select(s => s.Value).ToList();
    }

    /// <summary>Keeps the layers stacked heights, cover, roofs, decks, ladders, doors, markers (markers on top).</summary>
    private void OrderLayers()
    {
        int index = 0;
        foreach (string name in new[] { HeightsLayer, CoverLayer, RoofsLayer, DecksLayer, LaddersLayer, DoorsLayer, MarkersLayer })
        {
            if (GetNodeOrNull<TileMapLayer>(name) is { } layer)
                MoveChild(layer, index++);
        }
    }

    private TileMapLayer EnsureLayer(string name, TileSet tileSet)
    {
        var layer = GetNodeOrNull<TileMapLayer>(name);
        if (layer is null)
        {
            layer = new TileMapLayer { Name = name };
            AddChild(layer);
            layer.Owner = Owner ?? this; // saved with the scene
        }
        layer.TileSet = tileSet;
        return layer;
    }
}
