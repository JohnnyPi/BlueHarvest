#nullable enable
using System;
using System.Collections.Generic;
using BlueHarvest.Game.Ui;
using BlueHarvest.Sim.World.Generation;
using Godot;

namespace BlueHarvest.Game.Island;

/// <summary>
/// Draws what stands on the island over the map picture: rivers, roads, paddocks, power, transit,
/// caves, wrecks, ruins and the facilities. One unit is one grid cell, like the picture under it.
/// Debug view only; the basic view shows none of it.
/// </summary>
public partial class IslandOverlay : Node2D
{
    private IslandWorld? _world;
    private Font _font = null!;
    private float _zoom = 1f;

    public bool Rivers = true, Roads = true, Sites = true, Paddocks = true, Power, Transit = true, Relics = true, LeyLines;

    private bool _tileView;

    /// <summary>While the 1 m tiles are showing, facilities are outlined only, so their buildings stay visible.</summary>
    public void SetTileView(bool on)
    {
        if (_tileView == on) return;
        _tileView = on;
        QueueRedraw();
    }

    public void Bind(Font font) => _font = font;

    public void SetWorld(IslandWorld? world)
    {
        _world = world;
        QueueRedraw();
    }

    /// <summary>Screen pixels per cell, so lines and labels keep their size on screen.</summary>
    public void SetZoom(float zoom)
    {
        _zoom = zoom;
        QueueRedraw();
    }

    private static readonly Color RiverColor = new(0.55f, 0.82f, 0.95f);
    private static readonly Color MainRoad = new(0.93f, 0.88f, 0.74f);
    private static readonly Color Track = new(0.72f, 0.66f, 0.52f);
    private static readonly Color Fence = new(0.95f, 0.75f, 0.30f);
    private static readonly Color PowerColor = new(0.95f, 0.85f, 0.35f, 0.55f);
    private static readonly Color LiftColor = new(0.85f, 0.95f, 1f);
    private static readonly Color TramColor = new(1f, 0.55f, 0.25f);
    private static readonly Color MonoColor = new(0.90f, 0.45f, 0.95f);
    private static readonly Color CaveColor = new(0.62f, 0.55f, 0.80f);
    private static readonly Color WreckColor = new(0.85f, 0.55f, 0.35f);
    private static readonly Color RuinColor = new(0.78f, 0.72f, 0.60f);
    private static readonly Color LeyColor = new(0.75f, 0.6f, 0.95f, 0.35f);

    public override void _Draw()
    {
        if (_world == null) return;
        IslandWorld w = _world;
        IslandWorks k = w.Works;
        float cell = (float)w.Shape.Cell;
        float px = 1f / _zoom; // one screen pixel in cells

        Vector2 P(double x, double y) => new((float)(x / cell), (float)(y / cell));

        void Line(IReadOnlyList<Vector2> pts, Color color, float widthPx)
        {
            if (pts.Count < 2) return;
            var a = new Vector2[pts.Count];
            for (int i = 0; i < a.Length; i++) a[i] = pts[i];
            DrawPolyline(a, color, widthPx * px);
        }

        void Floats(float[] xs, float[] ys, Color color, float widthPx)
        {
            if (xs.Length < 2) return;
            var a = new Vector2[xs.Length];
            for (int i = 0; i < a.Length; i++) a[i] = new Vector2(xs[i] / cell, ys[i] / cell);
            DrawPolyline(a, color, widthPx * px);
        }

        void Caption(Vector2 at, string text, Color color, int size = 13)
        {
            DrawSetTransform(at, 0f, new Vector2(px, px));
            DrawString(_font, new Vector2(7, 4) + Vector2.One, text, HorizontalAlignment.Left, -1f, size, new Color(0, 0, 0, 0.85f));
            DrawString(_font, new Vector2(7, 4), text, HorizontalAlignment.Left, -1f, size, color);
            DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
        }

        if (LeyLines)
            foreach (LeyLine l in k.LeyLines) DrawLine(P(l.X1, l.Y1), P(l.X2, l.Y2), LeyColor, 1.5f * px);

        if (Rivers)
        {
            foreach (River r in w.Shape.Rivers)
            {
                float q = r.Qs.Length > 0 ? r.Qs[^1] : 0f;
                Floats(r.Xs, r.Ys, RiverColor, Mathf.Clamp(0.8f + Mathf.Sqrt(q) * 0.5f, 1f, 3.5f));
            }
        }

        if (Paddocks)
        {
            foreach (Paddock q in k.Paddocks)
            {
                var pts = new List<Vector2>();
                for (int v = 0; v < q.Poly.Length; v += 2) pts.Add(P(q.Poly[v], q.Poly[v + 1]));
                if (pts.Count > 0) pts.Add(pts[0]);
                Line(pts, Fence, q.Species.Fence == FenceKind.Heavy ? 2.5f : 1.5f);
                Caption(P(q.X, q.Y), q.Name, Fence, 12);
            }
        }

        if (Power)
            foreach (PowerLine l in k.Power) DrawLine(P(l.X1, l.Y1), P(l.X2, l.Y2), PowerColor, 1f * px);

        if (Roads)
        {
            foreach (Road rd in k.Roads)
                if (rd.Class == RoadClass.Track) Floats(rd.Xs, rd.Ys, Track, 1.2f);
            foreach (Road rd in k.Roads)
                if (rd.Class == RoadClass.Main) Floats(rd.Xs, rd.Ys, MainRoad, 2.2f);
        }

        if (Relics)
        {
            foreach (Cave c in k.Caves)
            {
                var pts = new List<Vector2>();
                for (int i = 0; i < c.Xs.Count; i++) pts.Add(P(c.Xs[i], c.Ys[i]));
                Line(pts, CaveColor, 1.5f);
                foreach ((double x, double y) in c.Mouths) DrawCircle(P(x, y), 3f * px, CaveColor);
            }
            foreach (Wreck q in k.Wrecks)
            {
                Vector2 at = P(q.X, q.Y);
                DrawLine(at + new Vector2(-4, -4) * px, at + new Vector2(4, 4) * px, WreckColor, 1.5f * px);
                DrawLine(at + new Vector2(-4, 4) * px, at + new Vector2(4, -4) * px, WreckColor, 1.5f * px);
            }
            foreach (Ruin q in k.Ruins)
            {
                Vector2 at = P(q.X, q.Y);
                DrawRect(new Rect2(at - new Vector2(3, 3) * px, new Vector2(6, 6) * px), RuinColor, false, 1.5f * px);
            }
        }

        if (Transit)
        {
            foreach (Lift l in k.Lifts)
            {
                DrawLine(P(l.X1, l.Y1), P(l.X2, l.Y2), LiftColor, 1.5f * px);
                Caption(P((l.X1 + l.X2) / 2, (l.Y1 + l.Y2) / 2), l.Name, LiftColor, 12);
            }
            if (k.Tram is { } tram)
            {
                Floats(tram.Xs, tram.Ys, TramColor, 2f);
                foreach (TramStop st in tram.Stops) DrawCircle(P(st.X, st.Y), 3f * px, TramColor);
            }
            if (k.Monorail is { } mono)
            {
                var pts = new List<Vector2>();
                foreach (MonorailStation st in mono.Stations) pts.Add(P(st.X, st.Y));
                Line(pts, MonoColor, 1.5f);
                foreach (MonorailStation st in mono.Stations) DrawRect(new Rect2(P(st.X, st.Y) - new Vector2(3, 3) * px, new Vector2(6, 6) * px), MonoColor);
            }
        }

        if (Sites)
        {
            foreach (Site t in k.Sites)
            {
                if (t.Runway is { } rw) DrawLine(P(rw.X1, rw.Y1), P(rw.X2, rw.Y2), HudTheme.Ink, 3f * px);
                Vector2 at = P(t.X, t.Y);
                float r = Mathf.Max(t.R / cell, 5f * px);
                if (!_tileView) DrawCircle(at, r, new Color(0.05f, 0.06f, 0.06f, 0.75f));
                DrawCircle(at, r, t.Major ? HudTheme.Amber : HudTheme.Ink, false, 1.5f * px);
                Caption(at, t.Name, t.Major ? HudTheme.Amber : HudTheme.Ink);
            }
            foreach (Objective o in k.Objectives)
            {
                Vector2 at = P(o.X, o.Y);
                DrawCircle(at, 4f * px, HudTheme.Danger);
                Caption(at + new Vector2(0, 14) * px, o.Name, HudTheme.Danger, 12);
            }
        }
    }
}
