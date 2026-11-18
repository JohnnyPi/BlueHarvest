#nullable enable
using Godot;

namespace BlueHarvest.Game.Island;

/// <summary>
/// The spot picked on the island explorer's map: a bright outline round its map cell and a crosshair on
/// the spot itself, so it's clear where Enter will start the walk. Drawn in both the basic and the
/// debug view, in cell units like the map under it; lines keep their size on screen.
/// </summary>
public partial class SpawnMarker : Node2D
{
    private static readonly Color Bright = new(1f, 0.92f, 0.2f);
    private static readonly Color Edge = new(0f, 0f, 0f, 0.85f);

    private Vector2? _spot;
    private float _zoom = 1f;

    /// <summary>The picked spot in cells, or null for none.</summary>
    public Vector2? Spot => _spot;

    public void SetSpot(Vector2? spot)
    {
        _spot = spot;
        QueueRedraw();
    }

    /// <summary>Screen pixels per cell.</summary>
    public void SetZoom(float zoom)
    {
        _zoom = zoom;
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (_spot is not { } at)
            return;
        float px = 1f / _zoom; // one screen pixel in cells
        var cell = new Rect2(Mathf.Floor(at.X), Mathf.Floor(at.Y), 1f, 1f);
        // a dark edge under a bright line, so it shows on sea, sand and jungle alike
        DrawRect(cell.Grow(px), Edge, filled: false, width: 4f * px);
        DrawRect(cell, Bright, filled: false, width: 2f * px);
        float arm = 8f * px;
        foreach (var (w, color) in new[] { (4f, Edge), (2f, Bright) })
        {
            DrawLine(at + new Vector2(-arm, 0), at + new Vector2(arm, 0), color, w * px);
            DrawLine(at + new Vector2(0, -arm), at + new Vector2(0, arm), color, w * px);
        }
    }
}
