#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BlueHarvest.Sim.World;
using Godot;

namespace BlueHarvest.Game.Tools;

/// <summary>
/// Editor-only map tool. Put it on the root of a scene, click "Import from file" in the Inspector,
/// paint the Heights, Cover and Markers layers with Godot's TileMap tools, then "Export to file".
/// The .bhmap file is the source of truth; this scene is just a way to edit it.
/// </summary>
[Tool]
public partial class MapEditor : Node2D
{
    private const string HeightsLayer = "Heights";
    private const string CoverLayer = "Cover";
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

    /// <summary>Marker names by tile, kept so names survive a round trip. A moved marker loses its name.</summary>
    [Export]
    public Godot.Collections.Dictionary<Vector2I, string> MarkerNames { get; set; } = new();

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

        var heights = EnsureLayer(HeightsLayer, MapTileSets.Heights());
        var cover = EnsureLayer(CoverLayer, MapTileSets.Cover());
        var markers = EnsureLayer(MarkersLayer, MapTileSets.Markers());
        heights.Clear();
        cover.Clear();
        markers.Clear();
        MarkerNames.Clear();

        for (int y = 0; y < map.Height; y++)
        {
            for (int x = 0; x < map.Width; x++)
            {
                var p = new TilePos(x, y);
                var cell = new Vector2I(x, y);
                heights.SetCell(cell, 0, new Vector2I(map.Heights.LevelAt(p), 0));
                var type = map.Cover[p];
                if (type != CoverType.Grass)
                    cover.SetCell(cell, 0, new Vector2I((int)type, 0));
            }
        }

        foreach (var marker in data.Markers)
        {
            var cell = new Vector2I(marker.Position.X, marker.Position.Y);
            markers.SetCell(cell, 0, new Vector2I((int)marker.Kind, 0));
            if (marker.Name is not null)
                MarkerNames[cell] = marker.Name;
        }

        QueueRedraw();
        GD.Print($"Imported {MapPath}: {map.Width}x{map.Height}, {data.Markers.Count} markers.");
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

        var map = new WorldMap(MapSize.X, MapSize.Y);
        int coverCount = Covers.All.Count;

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
            }
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

        string path = ProjectSettings.GlobalizePath(MapPath);
        File.WriteAllText(path, MapFile.Serialize(new MapData(map, sorted, Math.Clamp(AmbientLight, 0, 100))));
        GD.Print($"Exported {MapPath}: {map.Width}x{map.Height}, {sorted.Count} markers.");
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
