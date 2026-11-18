#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using BlueHarvest.Game.Ui;
using BlueHarvest.Sim.World;
using BlueHarvest.Sim.World.Generation;
using Godot;

namespace BlueHarvest.Game.Island;

/// <summary>
/// Draws what stands on the island over the map picture: rivers, roads, paddocks, power, transit,
/// caves, wrecks, ruins and the facilities. One unit is one grid cell, like the picture under it.
/// With no map knowledge set it shows everything the toggles allow (the explorer's debug view). With a
/// survivor's knowledge set (the in-game map, step 25b-3) the toggles are ignored and it shows only what
/// they know: what lies on ground they have seen, the park's own features (roads, paddocks, facilities,
/// transit) fainter on ground they only have from a park map, the ruins, wrecks and caves they've found,
/// the facilities they've been in by name, their trail, their notes and where they are. Never power or
/// ley lines.
/// </summary>
public partial class IslandOverlay : Node2D
{
    private IslandWorld? _world;
    private Font _font = null!;
    private float _zoom = 1f;

    public bool Rivers = true, Roads = true, Sites = true, Paddocks = true, Power, Transit = true, Relics = true, LeyLines;

    private bool _tileView;

    private MapKnowledge? _known;
    private bool[]? _charted;
    private Vector2? _here;
    private List<Prim> _buildings = new();

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
        if (world != _world)
        {
            _buildings = world is null ? new List<Prim>() : world.Works.Prims.InOrder().Where(p => p.Kind == PrimKind.Building).ToList();
            _known = null;
            _charted = null;
            _here = null;
        }
        _world = world;
        QueueRedraw();
    }

    /// <summary>
    /// Shows only what a survivor knows (null shows everything again), and where they are, in cells.
    /// Call after <see cref="SetWorld"/>, and again whenever the knowledge may have changed.
    /// </summary>
    public void SetKnowledge(MapKnowledge? known, Vector2? here)
    {
        _known = known;
        _charted = known is not null && _world is not null ? IslandMapImage.ChartedCells(_world, known) : null;
        _here = known is null ? null : here;
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
    private static readonly Color BuildingColor = new(0.86f, 0.84f, 0.78f);
    private static readonly Color TrailColor = new(0.95f, 0.80f, 0.35f, 0.9f);
    private static readonly Color OldTrailColor = new(0.95f, 0.80f, 0.35f, 0.35f);
    private static readonly Color NoteColor = new(0.55f, 0.85f, 0.95f);

    /// <summary>How park-map features look on ground known only from paper.</summary>
    private static Color Faint(Color c) => new(c.R, c.G, c.B, c.A * 0.45f);

    public override void _Draw()
    {
        if (_world == null) return;
        IslandWorld w = _world;
        IslandWorks k = w.Works;
        float cell = (float)w.Shape.Cell;
        float px = 1f / _zoom; // one screen pixel in cells

        Vector2 P(double x, double y) => new((float)(x / cell), (float)(y / cell));
        MapKnowledge? known = _known;
        bool filtered = known is not null;
        int nx = w.Shape.Nx, ny = w.Shape.Ny;

        // How well the survivor knows a spot (in cells): 2 seen, 1 from a park map, 0 not at all. Always 2 unfiltered.
        int Known(Vector2 at)
        {
            if (known is null) return 2;
            int cx = Mathf.FloorToInt(at.X), cy = Mathf.FloorToInt(at.Y);
            if (cx < 0 || cy < 0 || cx >= nx || cy >= ny) return 0;
            int i = cy * nx + cx;
            return known.HasSeen(i) ? 2 : _charted is { } c && c[i] ? 1 : 0;
        }

        void Poly(List<Vector2> run, Color color, float widthPx)
        {
            if (run.Count >= 2) DrawPolyline(run.ToArray(), color, widthPx * px);
        }

        // A line drawn where it's known: full where seen, faint where only on a park map (if it's a park
        // feature, onPaper), left out elsewhere. Each stretch goes by the cell its middle is in.
        void Line(IReadOnlyList<Vector2> pts, Color color, float widthPx, bool onPaper = true)
        {
            if (pts.Count < 2) return;
            if (!filtered)
            {
                Poly(pts as List<Vector2> ?? pts.ToList(), color, widthPx);
                return;
            }
            var run = new List<Vector2>();
            int was = -1;
            for (int i = 0; i + 1 < pts.Count; i++)
            {
                int now = Known((pts[i] + pts[i + 1]) / 2f);
                if (now == 1 && !onPaper) now = 0;
                if (now != was)
                {
                    if (was > 0) Poly(run, was == 2 ? color : Faint(color), widthPx);
                    run = new List<Vector2> { pts[i] };
                    was = now;
                }
                run.Add(pts[i + 1]);
            }
            if (was > 0) Poly(run, was == 2 ? color : Faint(color), widthPx);
        }

        void Floats(float[] xs, float[] ys, Color color, float widthPx, bool onPaper = true)
        {
            if (xs.Length < 2) return;
            var a = new List<Vector2>(xs.Length);
            for (int i = 0; i < xs.Length; i++) a.Add(new Vector2(xs[i] / cell, ys[i] / cell));
            Line(a, color, widthPx, onPaper);
        }

        // A mark's colour where it's known (full seen, faint on paper), or null to leave it out.
        Color? Shown(Vector2 at, Color color, bool onPaper = true) => Known(at) switch
        {
            2 => color,
            1 when onPaper => Faint(color),
            _ => null,
        };

        void Caption(Vector2 at, string text, Color color, int size = 13)
        {
            DrawSetTransform(at, 0f, new Vector2(px, px));
            DrawString(_font, new Vector2(7, 4) + Vector2.One, text, HorizontalAlignment.Left, -1f, size, new Color(0, 0, 0, 0.85f));
            DrawString(_font, new Vector2(7, 4), text, HorizontalAlignment.Left, -1f, size, color);
            DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
        }

        if (LeyLines && !filtered)
            foreach (LeyLine l in k.LeyLines) DrawLine(P(l.X1, l.Y1), P(l.X2, l.Y2), LeyColor, 1.5f * px);

        if (Rivers || filtered)
        {
            foreach (River r in w.Shape.Rivers)
            {
                float q = r.Qs.Length > 0 ? r.Qs[^1] : 0f;
                Floats(r.Xs, r.Ys, RiverColor, Mathf.Clamp(0.8f + Mathf.Sqrt(q) * 0.5f, 1f, 3.5f), onPaper: false);
            }
        }

        if (filtered)
        {
            // buildings on seen ground, at least a couple of pixels so a compound reads as one
            foreach (Prim b in _buildings)
            {
                Vector2 lo = P(b.X0, b.Y0), hi = P(b.X1 + 1, b.Y1 + 1);
                if (Known((lo + hi) / 2f) != 2) continue;
                Vector2 size = new(Mathf.Max(hi.X - lo.X, 2f * px), Mathf.Max(hi.Y - lo.Y, 2f * px));
                DrawRect(new Rect2((lo + hi) / 2f - size / 2f, size), BuildingColor);
            }
        }

        if (Paddocks || filtered)
        {
            foreach (Paddock q in k.Paddocks)
            {
                var pts = new List<Vector2>();
                for (int v = 0; v < q.Poly.Length; v += 2) pts.Add(P(q.Poly[v], q.Poly[v + 1]));
                if (pts.Count > 0) pts.Add(pts[0]);
                Line(pts, Fence, q.Species.Fence == FenceKind.Heavy ? 2.5f : 1.5f);
                if (Shown(P(q.X, q.Y), Fence) is { } label) Caption(P(q.X, q.Y), q.Name, label, 12);
            }
        }

        if (Power && !filtered)
            foreach (PowerLine l in k.Power) DrawLine(P(l.X1, l.Y1), P(l.X2, l.Y2), PowerColor, 1f * px);

        if (Roads || filtered)
        {
            foreach (Road rd in k.Roads)
                if (rd.Class == RoadClass.Track) Floats(rd.Xs, rd.Ys, Track, 1.2f);
            foreach (Road rd in k.Roads)
                if (rd.Class == RoadClass.Main) Floats(rd.Xs, rd.Ys, MainRoad, 2.2f);
        }

        if (filtered && known is not null)
        {
            // only what's been found, by name
            foreach (Cave c in k.Caves)
            {
                if (!known.Knows(FeatureKind.Cave, c.Id)) continue;
                foreach ((double x, double y) in c.Mouths) DrawCircle(P(x, y), 3f * px, CaveColor);
                if (c.Mouths.Count > 0) Caption(P(c.Mouths[0].X, c.Mouths[0].Y), c.Name, CaveColor, 12);
            }
            foreach (Wreck q in k.Wrecks)
            {
                if (!known.Knows(FeatureKind.Wreck, q.Id)) continue;
                Vector2 at = P(q.X, q.Y);
                DrawLine(at + new Vector2(-4, -4) * px, at + new Vector2(4, 4) * px, WreckColor, 1.5f * px);
                DrawLine(at + new Vector2(-4, 4) * px, at + new Vector2(4, -4) * px, WreckColor, 1.5f * px);
                Caption(at, q.Name, WreckColor, 12);
            }
            foreach (Ruin q in k.Ruins)
            {
                if (!known.Knows(FeatureKind.Ruin, q.Id)) continue;
                Vector2 at = P(q.X, q.Y);
                DrawRect(new Rect2(at - new Vector2(3, 3) * px, new Vector2(6, 6) * px), RuinColor, false, 1.5f * px);
                Caption(at, q.Name, RuinColor, 12);
            }
        }
        else if (Relics)
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

        if (Transit || filtered)
        {
            foreach (Lift l in k.Lifts)
            {
                Line(new List<Vector2> { P(l.X1, l.Y1), P(l.X2, l.Y2) }, LiftColor, 1.5f);
                Vector2 mid = P((l.X1 + l.X2) / 2, (l.Y1 + l.Y2) / 2);
                if (Shown(mid, LiftColor) is { } label) Caption(mid, l.Name, label, 12);
            }
            if (k.Tram is { } tram)
            {
                Floats(tram.Xs, tram.Ys, TramColor, 2f);
                foreach (TramStop st in tram.Stops)
                    if (Shown(P(st.X, st.Y), TramColor) is { } stop) DrawCircle(P(st.X, st.Y), 3f * px, stop);
            }
            if (k.Monorail is { } mono)
            {
                var pts = new List<Vector2>();
                foreach (MonorailStation st in mono.Stations) pts.Add(P(st.X, st.Y));
                Line(pts, MonoColor, 1.5f);
                foreach (MonorailStation st in mono.Stations)
                    if (Shown(P(st.X, st.Y), MonoColor) is { } station)
                        DrawRect(new Rect2(P(st.X, st.Y) - new Vector2(3, 3) * px, new Vector2(6, 6) * px), station);
            }
        }

        if (filtered && known is not null)
        {
            // a facility shows where its ground is known; its name once it's on a park map or you've been in
            for (int a = 0; a < k.Sites.Count; a++)
            {
                Site t = k.Sites[a];
                Vector2 at = P(t.X, t.Y);
                bool entered = known.Sites.Contains(a);
                int how = entered ? 2 : Known(at);
                if (how == 0) continue;
                Color ring = t.Major ? HudTheme.Amber : HudTheme.Ink;
                if (how == 1) ring = Faint(ring);
                if (t.Runway is { } rw && how == 2) DrawLine(P(rw.X1, rw.Y1), P(rw.X2, rw.Y2), HudTheme.Ink, 3f * px);
                DrawCircle(at, Mathf.Max(t.R / cell, 5f * px), ring, false, 1.5f * px);
                if (entered || _charted is { } c && c[Mathf.Clamp(Mathf.FloorToInt(at.Y), 0, ny - 1) * nx + Mathf.Clamp(Mathf.FloorToInt(at.X), 0, nx - 1)])
                    Caption(at, t.Name, ring);
            }

            // the trail, and fainter the trails of earlier survivors whose maps were merged in; a jump of more than two cells breaks it
            void Trail(IReadOnlyList<TrailStep> steps, Color color)
            {
                var run = new List<Vector2>();
                int last = -1;
                foreach (TrailStep step in steps)
                {
                    int cx = step.Cell % nx, cy = step.Cell / nx;
                    if (last >= 0 && Math.Max(Math.Abs(cx - last % nx), Math.Abs(cy - last / nx)) > 2)
                    {
                        Poly(run, color, 1.5f);
                        run = new List<Vector2>();
                    }
                    run.Add(new Vector2(cx + 0.5f, cy + 0.5f));
                    last = step.Cell;
                }
                Poly(run, color, 1.5f);
            }
            foreach (IReadOnlyList<TrailStep> older in known.OlderTrails) Trail(older, OldTrailColor);
            Trail(known.Trail, TrailColor);

            foreach (MapNote note in known.Notes)
            {
                Vector2 at = P(note.X, note.Y);
                Vector2[] diamond = { at + new Vector2(0, -5) * px, at + new Vector2(5, 0) * px, at + new Vector2(0, 5) * px, at + new Vector2(-5, 0) * px };
                DrawColoredPolygon(diamond, NoteColor);
                Caption(at, note.Text, NoteColor, 12);
            }

            if (_here is { } here)
            {
                DrawCircle(here, 8f * px, new Color(0, 0, 0, 0.85f), false, 4f * px);
                DrawCircle(here, 8f * px, Colors.White, false, 2f * px);
                DrawCircle(here, 3.5f * px, HudTheme.Danger);
                Caption(here + new Vector2(4, -18) * px, "You are here", Colors.White, 13);
            }
        }
        else if (Sites)
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
