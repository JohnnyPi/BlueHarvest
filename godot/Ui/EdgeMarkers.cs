#nullable enable
using System;
using System.Collections.Generic;
using BlueHarvest.Sim.World;
using Godot;

namespace BlueHarvest.Game.Ui;

/// <summary>Something worth pointing at when it's off screen: an exit, the Rex, or where you last saw it.</summary>
public readonly record struct EdgeTarget(TilePos At, string Text, Color Color);

/// <summary>
/// Arrows at the edge of the map area pointing at targets that are off screen, each with a short label
/// ("floor hatch 47 m", "Rex 14 m", "heavy footfalls"). Kept clear of the log panel. Never catches clicks.
/// </summary>
public partial class EdgeMarkers : Control
{
    private const float Inset = 26f;
    private const int LabelSize = 13;

    /// <summary>Turns a tile into a screen position (Main supplies the map's transform and the raised view's lift).</summary>
    public Func<TilePos, Vector2>? ToScreen { get; set; }

    public IReadOnlyList<EdgeTarget> Targets { get; set; } = Array.Empty<EdgeTarget>();

    /// <summary>The part of the screen showing the map.</summary>
    public Rect2 Bounds { get; set; }

    /// <summary>Screen areas the markers move out of (the log panel).</summary>
    public List<Rect2> Avoid { get; } = new();

    public EdgeMarkers()
    {
        AnchorRight = 1f;
        AnchorBottom = 1f;
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public override void _Draw()
    {
        if (ToScreen is null || Bounds.Size.X < 2 * Inset || Bounds.Size.Y < 2 * Inset)
            return;

        var font = GetThemeDefaultFont();
        var inner = Bounds.Grow(-Inset);
        var center = inner.GetCenter();
        foreach (var target in Targets)
        {
            var at = ToScreen(target.At);
            if (inner.HasPoint(at))
                continue;

            // Where the line from the centre to the target leaves the inner rectangle.
            var delta = at - center;
            float scale = float.MaxValue;
            if (Mathf.Abs(delta.X) > 0.001f)
                scale = Mathf.Min(scale, (delta.X > 0 ? inner.End.X - center.X : inner.Position.X - center.X) / delta.X);
            if (Mathf.Abs(delta.Y) > 0.001f)
                scale = Mathf.Min(scale, (delta.Y > 0 ? inner.End.Y - center.Y : inner.Position.Y - center.Y) / delta.Y);
            if (scale == float.MaxValue)
                continue;
            var edge = center + delta * scale;

            foreach (var avoid in Avoid)
            {
                var grown = avoid.Grow(10f);
                if (!grown.HasPoint(edge))
                    continue;
                edge.Y = grown.GetCenter().Y > center.Y ? grown.Position.Y - 4f : grown.End.Y + 4f;
            }

            float angle = delta.Angle();
            var arrow = new[]
            {
                edge + new Vector2(10f, 0f).Rotated(angle),
                edge + new Vector2(-4f, -7f).Rotated(angle),
                edge + new Vector2(-1f, 0f).Rotated(angle),
                edge + new Vector2(-4f, 7f).Rotated(angle),
            };
            DrawColoredPolygon(arrow, target.Color);

            var size = font.GetStringSize(target.Text, HorizontalAlignment.Left, -1, LabelSize);
            var direction = Vector2.FromAngle(angle);
            var labelCenter = edge - direction * (size.X / 2f + 14f) - new Vector2(0f, direction.Y * 6f);
            var box = new Rect2(labelCenter.X - size.X / 2f - 5f, labelCenter.Y - size.Y / 2f - 2f, size.X + 10f, size.Y + 4f);
            box.Position = new Vector2(
                Mathf.Clamp(box.Position.X, Bounds.Position.X + 4f, Bounds.End.X - box.Size.X - 4f),
                Mathf.Clamp(box.Position.Y, Bounds.Position.Y + 4f, Bounds.End.Y - box.Size.Y - 4f));
            DrawRect(box, new Color(0.03f, 0.04f, 0.035f, 0.85f));
            DrawString(font, new Vector2(box.Position.X + 5f, box.Position.Y + 2f + font.GetAscent(LabelSize)), target.Text,
                HorizontalAlignment.Left, -1, LabelSize, target.Color);
        }
    }
}
