#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using BlueHarvest.Sim;
using BlueHarvest.Sim.Actors;
using BlueHarvest.Sim.Ai;
using BlueHarvest.Sim.Perception;
using BlueHarvest.Sim.World;
using Godot;

namespace BlueHarvest.Game.Rendering;

/// <summary>One remembered contact, as the [6] overlay draws it.</summary>
/// <param name="Radius">Uncertainty in tiles: the ring's size.</param>
public readonly record struct ContactMark(TilePos At, int Radius, int Confidence, ContactClass Class, bool Focus);

/// <summary>How the map is drawn, and the debug overlays on it. Main fills it after every turn.</summary>
public sealed class OverlayState
{
    /// <summary>The raised 3/4 view: higher ground drawn shifted up, drops shown as faces, walls standing up.</summary>
    public bool Raised = true;

    /// <summary>Glyph variants, colour jitter, joined walls and fence posts.</summary>
    public bool Varied = true;

    /// <summary>Fog of war: only what you see now is live; the rest is memory, the park plan, or black.</summary>
    public bool Fog = true;

    /// <summary>Roofs over buildings you're not inside.</summary>
    public bool Roofs = true;

    /// <summary>Arrows at the screen edge for off-screen exits and the Rex (drawn by the HUD).</summary>
    public bool EdgeMarkers = true;

    /// <summary>Water that moves on its own: glints, river streaks, surf (drawn by the AmbientView).</summary>
    public bool Animation = true;

    /// <summary>[1] Darken tiles by the simulation's light grid.</summary>
    public bool Lighting = true;

    /// <summary>[2] Fog out tiles the player can't see and hide actors the player hasn't spotted.</summary>
    public bool PlayerView;

    /// <summary>[3] Tint tiles by how well the Rex would see a moving human there.</summary>
    public bool RexView;

    /// <summary>[4] Rings for noises made since the player's last turn.</summary>
    public bool Noise = true;

    /// <summary>[5] The Rex's planned route, when it's hunting.</summary>
    public bool RexPath = true;

    /// <summary>[6] What the Rex remembers: a ring per contact (red prey, yellow unknown, grey ignored), thick for its focus.</summary>
    public bool RexMemory = true;

    /// <summary>[7] Scent on the ground: green dots for body scent, pink for blood, fading as they wash out.</summary>
    public bool Scent;

    /// <summary>[8] Ground vibrations since the player's last turn: a brown ring as far as a person would feel it.</summary>
    public bool Vibration = true;

    public IReadOnlyList<VibrationEvent> Vibrations = Array.Empty<VibrationEvent>();

    public IReadOnlyList<TilePos> RexPathTiles = Array.Empty<TilePos>();
    public IReadOnlyList<ContactMark> Contacts = Array.Empty<ContactMark>();

    /// <summary>The throw cursor while aiming, the thrower's range, and where they stand.</summary>
    public TilePos? Cursor;
    public int CursorRange;
    public TilePos CursorFrom;

    /// <summary>The tile under the mouse, outlined; null when the mouse is off the map or over a panel.</summary>
    public TilePos? Hover;

    /// <summary>The walk the mouse would start: a dot per step, and a label at the end ("14 steps · 9.8 s").</summary>
    public IReadOnlyList<TilePos> PathPreview = Array.Empty<TilePos>();
    public string? PathLabel;

    public bool[]? PlayerVisibleTiles;
    public HashSet<int> PlayerSpottedActors = new();
    public int[]? RexScores;
    public int RexThreshold;
    public int PlayerId = -1;
    public IReadOnlyList<NoiseEvent> Noises = Array.Empty<NoiseEvent>();
}

/// <summary>
/// Draws the simulation as coloured ASCII cells, row by row from north to south. In the raised view each
/// tile is shifted up by its height and a drop shows as a face below it, so later (southern) rows hide
/// what stands behind them, as in a 3/4 view. Then the fog of war, the light grid, overlays and actors.
/// Purely visual: it reads simulation state and never changes it.
/// </summary>
public partial class AsciiMapView : Node2D
{
    public const int CellSize = 24;
    private const int FontSize = 20;
    private const float ShadowWidth = 4f;

    /// <summary>Raised view: screen pixels per decimetre of height (an 8 m drop is about 1.8 tiles).</summary>
    private const float LiftPerDm = CellSize * 0.022f;

    /// <summary>Raised view: how high the Rex's body is drawn above the ground, so a fence doesn't swallow it.</summary>
    private const int LargeBodyLiftDm = 15;

    private static readonly Color CliffLine = new(0.02f, 0.02f, 0.02f);
    private static readonly Color CliffShadow = new(0f, 0f, 0f, 0.35f);
    private static readonly Color ActorBacking = new(0f, 0f, 0f, 0.5f);
    private static readonly Color LightGlow = new(1f, 0.72f, 0.3f, 0.12f);
    private static readonly Color LightGlyph = new(1f, 0.8f, 0.35f);
    private static readonly Color RememberedLamp = new(0.67f, 0.6f, 0.45f, 0.5f);
    private static readonly Color RexSeen = new(1f, 0.15f, 0.1f, 0.45f);
    private static readonly Color RexGlimpse = new(1f, 0.15f, 0.1f, 0.14f);
    private static readonly Color PlayerNoise = new(1f, 1f, 1f, 0.7f);
    private static readonly Color OtherNoise = new(1f, 0.6f, 0.2f, 0.7f);
    private static readonly Color PathDot = new(1f, 0.3f, 0.25f, 0.8f);
    private static readonly Color RexCore = new(0.35f, 0.06f, 0.05f, 0.85f);
    private static readonly Color RexSoft = new(0.30f, 0.05f, 0.05f, 0.55f);
    private static readonly Color GhostEdge = new(1f, 0.55f, 0.47f, 0.55f);
    private static readonly Color GhostInk = new(1f, 0.67f, 0.59f);
    private static readonly Color LockedInk = new(1f, 0.35f, 0.3f);
    private static readonly Color DamagedInk = new(1f, 0.3f, 0.25f);
    private static readonly Color DeckFill = new(0.45f, 0.62f, 0.85f, 0.35f);
    private static readonly Color DeckRim = new(0.05f, 0.08f, 0.15f, 0.9f);
    private static readonly Color DeckPlank = new(0.50f, 0.40f, 0.28f);
    private static readonly Color DeckLip = new(0.30f, 0.23f, 0.15f);
    private static readonly Color PostColor = new(0.55f, 0.42f, 0.28f);
    private static readonly Color LadderInk = new(0.95f, 0.80f, 0.45f);
    private static readonly Color DeckActorBacking = new(0.10f, 0.20f, 0.40f, 0.8f);
    private static readonly Color BloodColor = new(0.55f, 0.02f, 0.02f, 0.85f);
    private static readonly Color CorpseInk = new(0.75f, 0.25f, 0.2f, 0.9f);
    private static readonly Color RopeColor = new(0.85f, 0.75f, 0.45f, 0.9f);
    private static readonly Color FlareInk = new(1f, 0.35f, 0.25f);
    private static readonly Color CursorColor = new(1f, 0.9f, 0.3f, 0.85f);
    private static readonly Color VibrationRing = new(0.80f, 0.55f, 0.30f, 0.65f);
    private static readonly Color SparkInk = new(0.85f, 0.95f, 1f);
    private static readonly Color SparkDim = new(0.45f, 0.65f, 1f, 0.8f);
    private static readonly Color ExitInk = new(0.45f, 1f, 0.55f);
    private static readonly Color ExitBacking = new(0.05f, 0.35f, 0.12f, 0.85f);
    private static readonly Color PreviewDot = new(1f, 1f, 0.94f, 0.75f);
    private static readonly Color HoverEdge = new(1f, 1f, 1f, 0.8f);
    private static readonly Color LabelBacking = new(0.03f, 0.04f, 0.035f, 0.85f);
    private static readonly Color LabelInk = new(0.95f, 0.94f, 0.86f);
    private static readonly Color FaceStripe = new(0f, 0f, 0f, 0.35f);
    private static readonly Color RimLight = new(1f, 1f, 1f, 0.13f);
    private static readonly Color SideShadow = new(0f, 0f, 0f, 0.45f);
    private const int LabelFontSize = 14;

    private Simulation? _sim;
    private Font _font = null!;
    private int _cliffThresholdDm;
    private int _minHeightDm;
    private int _heightRangeDm = 1;
    private float[] _hillshade = Array.Empty<float>();
    private float _baseline;

    public OverlayState Overlays { get; } = new();

    // ---- Culling: which tiles get drawn -------------------------------------------------------

    /// <summary>How many tiles beyond the view are drawn, so small camera moves need no redraw.</summary>
    private const int CullMargin = 10;

    /// <summary>The tiles drawn by the last redraw request. Everything, until <see cref="SetView"/> is called.</summary>
    private Rect2I _drawn = new(0, 0, int.MaxValue / 2, int.MaxValue / 2);
    private int _x0, _y0, _x1, _y1;

    /// <summary>How many tiles the last draw covered: for the debug readout.</summary>
    public int DrawnTiles => (_x1 - _x0 + 1) * (_y1 - _y0 + 1);

    /// <summary>
    /// Tells the view what part of it is on screen (in this node's pixels), so it draws only those
    /// tiles plus a margin. Call it every frame; it asks for a redraw only when the screen has moved
    /// past what was drawn last time. In the raised view, ground south of the screen can be lifted
    /// into it, so the drawn area reaches further south by the tallest lift on the map.
    /// </summary>
    public void SetView(Rect2 onScreen)
    {
        if (_sim is null) return;
        float lift = MaxLift;
        int x0 = Mathf.FloorToInt(onScreen.Position.X / CellSize) - 1, y0 = Mathf.FloorToInt(onScreen.Position.Y / CellSize) - 1;
        int x1 = Mathf.CeilToInt(onScreen.End.X / CellSize) + 1, y1 = Mathf.CeilToInt((onScreen.End.Y + lift) / CellSize) + 1;
        var needed = new Rect2I(x0, y0, x1 - x0, y1 - y0);
        if (_drawn.Encloses(needed)) return;
        _drawn = needed.Grow(CullMargin);
        QueueRedraw();
    }

    /// <summary>
    /// The furthest any ground is drawn above its flat position, in pixels (0 in the flat view): how
    /// far south of the screen a tile can be and still show on it.
    /// </summary>
    public float MaxLift => Overlays.Raised ? _heightRangeDm * LiftPerDm + CellSize : 0f;

    /// <summary>The player's map memory. Null until <see cref="Bind"/>.</summary>
    public FogOfWar? Fog { get; private set; }

    /// <summary>Pixel centre of a tile on flat ground, in this node's coordinates.</summary>
    public static Vector2 TileCenter(TilePos p) => new((p.X + 0.5f) * CellSize, (p.Y + 0.5f) * CellSize);

    /// <summary>
    /// Connects the view to a simulation. In the flat view a height step bigger than
    /// <paramref name="cliffThresholdDm"/> between neighbours is drawn as a cliff edge.
    /// </summary>
    /// <param name="plan">Whether the player starts with the site plan; see <see cref="FogOfWar"/>.</param>
    public void Bind(Simulation sim, Font font, int cliffThresholdDm, bool plan = true)
    {
        _sim = sim;
        _font = font;
        _cliffThresholdDm = cliffThresholdDm;
        Fog = new FogOfWar(sim, plan);
        MeasureTerrain(sim);
        QueueRedraw();
    }

    /// <summary>
    /// The streamed map scrolled by (dx, dy) tiles: moves the player's map memory with it and measures
    /// the new terrain (height range and hillshade).
    /// </summary>
    public void MapScrolled(int dx, int dy)
    {
        if (_sim is null) return;
        Fog?.Shift(dx, dy, _sim);
        MeasureTerrain(_sim);
        QueueRedraw();
    }

    private void MeasureTerrain(Simulation sim)
    {
        var terrain = sim.Terrain;
        int min = int.MaxValue, max = int.MinValue;
        for (int y = 0; y < terrain.Height; y++)
        {
            for (int x = 0; x < terrain.Width; x++)
            {
                int h = terrain[new TilePos(x, y)];
                min = Math.Min(min, h);
                max = Math.Max(max, h);
            }
        }
        _minHeightDm = min;
        _heightRangeDm = Math.Max(1, max - min);
        _hillshade = Hillshade(sim);
    }

    // ---- Where things are drawn ------------------------------------------------------------

    /// <summary>The tile's centre as drawn now (raised by its height in the raised view).</summary>
    public Vector2 CenterOf(TilePos p) => TileCenter(p) - new Vector2(0f, Lift(p));

    /// <summary>The centre of a small actor as drawn now: on the ground, a vehicle's roof, or a deck.</summary>
    public Vector2 ActorCenter(Actor actor) =>
        TileCenter(actor.Position) - new Vector2(0f, Overlays.Raised ? (StandDm(actor) - _minHeightDm) * LiftPerDm : 0f);

    /// <summary>
    /// The tile drawn under a point in this node's coordinates. In the raised view a tile is drawn
    /// shifted up, with its south face below it, so this looks down the column for the nearest
    /// (southernmost) tile whose top or face covers the point.
    /// </summary>
    public TilePos? TileAt(Vector2 local)
    {
        if (_sim is null)
            return null;
        var map = _sim.Map;
        int x = Mathf.FloorToInt(local.X / CellSize);
        if (x < 0 || x >= map.Width)
            return null;
        if (!Overlays.Raised)
        {
            int flatY = Mathf.FloorToInt(local.Y / CellSize);
            return flatY >= 0 && flatY < map.Height ? new TilePos(x, flatY) : null;
        }

        float maxLift = (_heightRangeDm + 70) * LiftPerDm;
        int from = Math.Min(map.Height - 1, Mathf.FloorToInt((local.Y + maxLift) / CellSize));
        int to = Math.Max(0, Mathf.FloorToInt(local.Y / CellSize) - 1);
        for (int y = from; y >= to; y--)
        {
            var p = new TilePos(x, y);
            float top = y * CellSize - Lift(p);
            if (local.Y >= top && local.Y < top + CellSize + FaceHeight(p))
                return p;
        }
        return null;
    }

    /// <summary>How the fog draws this tile (always Visible with the fog off).</summary>
    public FogState FogAt(TilePos p) =>
        Overlays.Fog && Fog is not null ? Fog.StateAt(p, RoofShown(p)) : FogState.Visible;

    /// <summary>Whether a roof is drawn over this tile: roofs on, a roofed tile, and not the building you're in.</summary>
    public bool RoofShown(TilePos p)
    {
        if (_sim is null || !Overlays.Roofs || !_sim.Map.IsIndoor(p))
            return false;
        if (Fog is null)
            return true;
        int group = Fog.RoofGroupAt(p);
        int playerGroup = Overlays.PlayerId >= 0 && Overlays.PlayerId < _sim.Actors.Count
            ? PlayerGroup()
            : -1;
        return group != playerGroup;
    }

    private int PlayerGroup()
    {
        var player = _sim!.Actors[Overlays.PlayerId];
        return _sim.Map.IsIndoor(player.Position) ? Fog!.RoofGroupAt(player.Position) : -1;
    }

    /// <summary>The height the raised view draws a tile at: ground, plus a standing thing or a roof.</summary>
    private int VisualHeight(TilePos p)
    {
        var sim = _sim!;
        int ground = sim.Terrain[p];
        if (RoofShown(p))
            return ground + Math.Max(30, sim.Map.Structures.RoofDm(p));
        return ground + MapLook.StandsUpDm(sim.Cover[p]);
    }

    private float Lift(TilePos p) => Overlays.Raised ? (VisualHeight(p) - _minHeightDm) * LiftPerDm : 0f;

    /// <summary>How tall the south face under a tile is (0 when the tile to the south is as high or higher).</summary>
    private float FaceHeight(TilePos p)
    {
        if (!Overlays.Raised)
            return 0f;
        var south = new TilePos(p.X, p.Y + 1);
        int below = _sim!.Map.InBounds(south) ? VisualHeight(south) : VisualHeight(p);
        return Math.Max(0, VisualHeight(p) - below) * LiftPerDm;
    }

    /// <summary>Where a small actor stands, in decimetres: a deck, the roof of a vehicle they're in, or the ground.</summary>
    private int StandDm(Actor actor)
    {
        var sim = _sim!;
        var p = actor.Position;
        if (actor.OnDeck)
            return sim.Map.DeckTop(p);
        return sim.Terrain[p] + (sim.Cover[p] == CoverType.Vehicle ? MapLook.StandsUpDm(CoverType.Vehicle) : 0);
    }

    private float LargeBodyLift(TilePos p) =>
        Overlays.Raised ? (_sim!.Terrain[p] + LargeBodyLiftDm - _minHeightDm) * LiftPerDm : 0f;

    // ---- Drawing ---------------------------------------------------------------------------

    public override void _Draw()
    {
        if (_sim is null)
            return;

        var sim = _sim;
        var o = Overlays;
        int width = sim.Map.Width, height = sim.Map.Height;
        float ascent = _font.GetAscent(FontSize);
        float descent = _font.GetDescent(FontSize);
        _baseline = (CellSize - (ascent + descent)) / 2f + ascent;

        // Large bodies are drawn tile by tile in their rows, so they sit behind what's south of them.
        var bodyRows = new Dictionary<int, List<(BodyTile Tile, Actor Actor)>>();
        foreach (var actor in sim.Actors)
        {
            if (!actor.IsLarge || actor.LeftBehind || !Shown(actor))
                continue;
            foreach (var tile in actor.Body)
            {
                if (!sim.Map.InBounds(tile.Position))
                    continue;
                if (!bodyRows.TryGetValue(tile.Position.Y, out var row))
                    bodyRows[tile.Position.Y] = row = new List<(BodyTile, Actor)>();
                row.Add((tile, actor));
            }
        }

        // only the tiles in (and a little around) the view are drawn; see SetView
        _x0 = Math.Clamp(_drawn.Position.X, 0, width - 1);
        _y0 = Math.Clamp(_drawn.Position.Y, 0, height - 1);
        _x1 = Math.Clamp(_drawn.End.X - 1, _x0, width - 1);
        _y1 = Math.Clamp(_drawn.End.Y - 1, _y0, height - 1);
        if (o.Raised)
        {
            for (int y = _y0; y <= _y1; y++)
            {
                for (int x = _x0; x <= _x1; x++)
                    DrawGround(new TilePos(x, y));
                DrawRow(y, bodyRows);
            }
        }
        else
        {
            for (int y = _y0; y <= _y1; y++)
                for (int x = _x0; x <= _x1; x++)
                    DrawGround(new TilePos(x, y));
            DrawFlatCliffs();
            for (int y = _y0; y <= _y1; y++)
                DrawRow(y, bodyRows);
        }

        DrawOverlays();
    }

    /// <summary>One tile of ground: its cover (or roof) with the fog, light and height applied, and in the raised view its face.</summary>
    private void DrawGround(TilePos p)
    {
        var sim = _sim!;
        var o = Overlays;
        var state = FogAt(p);
        if (state == FogState.Unknown)
            return;

        bool roofed = RoofShown(p);
        float top = p.Y * CellSize - Lift(p);
        var cell = new Rect2(p.X * CellSize, top, CellSize, CellSize);
        var fog = Fog;
        var cover = state switch
        {
            FogState.Remembered when fog is not null => fog.RememberedCover(p),
            FogState.Plan when fog is not null => fog.PlanCover(p),
            _ => sim.Cover[p],
        };

        if (state == FogState.Plan)
        {
            DrawRect(cell, MapLook.PlanGround);
            char planGlyph = roofed ? '·' : o.Varied ? MapLook.VariedGlyph(cover, p.X, p.Y, PlanCoverAt) : MapLook.Cover[(int)cover].Glyph;
            DrawGlyphAt(planGlyph, p.X * CellSize, top, MapLook.PlanInk);
            return;
        }

        var look = MapLook.Cover[(int)cover];
        float t = (float)(sim.Terrain[p] - _minHeightDm) / _heightRangeDm;
        float shade = o.Raised ? _hillshade[p.Y * sim.Map.Width + p.X] * 0.92f + 0.12f * t : 0.55f + 0.65f * t;
        var background = roofed ? MapLook.Roof : look.Background;
        if (o.Varied && !roofed)
        {
            float jitter = MapLook.Jitter(p.X, p.Y);
            if (cover == CoverType.Floor && (p.X + p.Y) % 2 == 1)
                jitter *= 0.9f;
            background = ScaleColor(background, jitter);
        }
        int light = roofed ? sim.EffectiveAmbientPercent : sim.Light.LevelAt(p);
        bool dimmed = o.PlayerView && o.PlayerVisibleTiles is { } visible && !visible[p.Y * sim.Map.Width + p.X];

        DrawRect(cell, Tone(background, shade, light, state, dimmed));
        if (!roofed)
        {
            var (glyph, ink) = GlyphFor(p, cover, look.Glyph, look.Ink, state);
            if (glyph != ' ')
                DrawGlyphAt(glyph, p.X * CellSize, top, Tone(ink, Math.Min(1.25f, shade * 1.2f), light, state, dimmed));
        }
        else if (o.Varied && p.Y % 2 == 0)
        {
            DrawLine(new Vector2(cell.Position.X, top + CellSize - 0.5f), new Vector2(cell.End.X, top + CellSize - 0.5f),
                Tone(MapLook.RoofSeam, 1f, light, state, dimmed), 1f);
        }

        if (!o.Raised)
            return;

        // The raised view: the south face, dark edges where the ground drops away east or west, a rim light on top.
        float face = FaceHeight(p);
        if (face > 0.5f)
        {
            var south = new TilePos(p.X, p.Y + 1);
            int faceLight = sim.Map.InBounds(south) ? Math.Max(light, sim.Light.LevelAt(south)) : light;
            var faceColor = roofed ? MapLook.RoofFace : MapLook.Face(cover);
            var faceRect = new Rect2(p.X * CellSize, top + CellSize, CellSize, face);
            DrawRect(faceRect, Tone(faceColor, 1f, faceLight, state, dimmed));
            DrawFaceTexture(p, cover, roofed, faceRect);
            DrawLine(faceRect.Position, new Vector2(faceRect.End.X, faceRect.Position.Y), RimLight, 1f);
        }
        int here = VisualHeight(p);
        var map = sim.Map;
        var west = new TilePos(p.X - 1, p.Y);
        var east = new TilePos(p.X + 1, p.Y);
        var north = new TilePos(p.X, p.Y - 1);
        if (map.InBounds(west) && VisualHeight(west) < here - 8)
            DrawRect(new Rect2(p.X * CellSize, top, 1.5f, CellSize), SideShadow);
        if (map.InBounds(east) && VisualHeight(east) < here - 8)
            DrawRect(new Rect2(p.X * CellSize + CellSize - 1.5f, top, 1.5f, CellSize), SideShadow);
        if (map.InBounds(north) && VisualHeight(north) < here - 8)
            DrawRect(new Rect2(p.X * CellSize, top, CellSize, 1.5f), RimLight);

        // A support post stands up to the deck it holds (or 3 m without one).
        if (cover == CoverType.Support)
        {
            int up = map.Structures.HasDeck(p) ? map.DeckTop(p) - sim.Terrain[p] : 30;
            float pole = up * LiftPerDm;
            DrawRect(new Rect2(p.X * CellSize + CellSize / 2f - 2f, top + CellSize / 2f - pole, 4f, pole),
                Tone(PostColor, 1f, light, state, dimmed));
        }
    }

    /// <summary>Stripes on a face: courses on walls and roofs, wires on fences, cracks in earth and rock.</summary>
    private void DrawFaceTexture(TilePos p, CoverType cover, bool roofed, Rect2 face)
    {
        if (roofed || cover == CoverType.Wall)
        {
            for (float y = face.Position.Y + 4f; y < face.End.Y; y += 5f)
                DrawLine(new Vector2(face.Position.X, y), new Vector2(face.End.X, y), FaceStripe, 1f);
        }
        else if (cover is CoverType.Fence or CoverType.WeakFence)
        {
            for (float x = face.Position.X + 2f; x < face.End.X; x += 4f)
                DrawLine(new Vector2(x, face.Position.Y), new Vector2(x, face.End.Y), FaceStripe, 1f);
        }
        else if (cover != CoverType.Vehicle)
        {
            float x = face.Position.X + 3f + MapLook.Hash(p.X, p.Y, 5) % 4;
            while (x < face.End.X)
            {
                DrawLine(new Vector2(x, face.Position.Y + 1f), new Vector2(x, face.End.Y - 1f), FaceStripe, 1f);
                x += 5f + MapLook.Hash((int)x, p.Y, 6) % 3;
            }
        }
    }

    /// <summary>Everything that stands in one row, after its ground: decks, lamps, flares, sparks, exits, people, the Rex.</summary>
    private void DrawRow(int y, Dictionary<int, List<(BodyTile Tile, Actor Actor)>> bodyRows)
    {
        var sim = _sim!;
        var map = sim.Map;

        foreach (var spot in sim.BloodSpots)
        {
            if (spot.Position.Y == y && Seen(spot.Position))
                DrawCircle(CenterOf(spot.Position) + new Vector2(3f, 4f), 2.5f, BloodColor);
        }

        for (int x = _x0; x <= _x1; x++)
        {
            var p = new TilePos(x, y);
            if (map.Structures.HasDeck(p) && FogAt(p) is FogState.Visible or FogState.Remembered)
                DrawDeck(p);
        }

        foreach (var light in sim.Lights)
        {
            var p = light.Position;
            if (!light.On || p.Y != y || light.Facing is not null)
                continue;
            var state = FogAt(p);
            bool flare = light.HeightDm < 5;
            if (flare && state != FogState.Visible)
                continue;
            if (state is FogState.Plan or FogState.Unknown)
                continue;
            DrawGlyphAt('*', p.X * CellSize, p.Y * CellSize - Lift(p),
                flare ? FlareInk : state == FogState.Remembered ? RememberedLamp : LightGlyph);
        }

        // A sparking fence section flickers between a bright and a dim crackle while it's live.
        foreach (var sparks in sim.Sparks)
        {
            var p = sparks.Position;
            if (!sparks.Live || p.Y != y || FogAt(p) != FogState.Visible)
                continue;
            bool bright = (sim.CurrentTick / 30 + p.X) % 2 == 0;
            DrawGlyphAt(bright ? '*' : '+', p.X * CellSize, p.Y * CellSize - Lift(p), bright ? SparkInk : SparkDim);
        }

        // Exits: green hatches, always shown (the survivor knows the park).
        foreach (var exit in sim.Exits)
        {
            var p = exit.Position;
            if (p.Y != y)
                continue;
            float top = p.Y * CellSize - Lift(p);
            DrawRect(new Rect2(p.X * CellSize + 1, top + 1, CellSize - 2, CellSize - 2), ExitBacking);
            DrawGlyphAt('>', p.X * CellSize, top, ExitInk);
        }

        // People: on the ground first, then up on a deck.
        foreach (bool onDeck in new[] { false, true })
        {
            foreach (var actor in sim.Actors)
            {
                if (actor.IsLarge || actor.HasEscaped || actor.LeftBehind || actor.OnDeck != onDeck || actor.Position.Y != y)
                    continue;
                var p = actor.Position;
                var center = ActorCenter(actor);
                float left = p.X * CellSize, top = center.Y - CellSize / 2f;
                if (actor.IsDead)
                {
                    if (Seen(p))
                        DrawGlyphAt('%', left, top, CorpseInk);
                    continue;
                }
                if (!Shown(actor))
                    continue;
                DrawRect(new Rect2(left + 2, top + 2, CellSize - 4, CellSize - 4), onDeck ? DeckActorBacking : ActorBacking);
                DrawGlyphAt(actor.Glyph, left, top, ColorFor(actor.Glyph));
                DrawFacing(center, actor.Facing);
            }
        }

        if (bodyRows.TryGetValue(y, out var body))
        {
            foreach (var (tile, actor) in body)
                DrawLargeBodyTile(tile, actor);
        }
    }

    /// <summary>A deck tile: planks 6 m up in the raised view (with a lip and the ladder's rails), a translucent overlay flat.</summary>
    private void DrawDeck(TilePos p)
    {
        var sim = _sim!;
        var o = Overlays;
        var structures = sim.Map.Structures;
        int light = sim.Light.LevelAt(p);
        var state = FogAt(p);
        if (o.Raised)
        {
            float top = p.Y * CellSize - (sim.Map.DeckTop(p) - _minHeightDm) * LiftPerDm;
            var cell = new Rect2(p.X * CellSize, top, CellSize, CellSize);
            DrawRect(cell, Tone(DeckPlank, 1f, light, state, false));
            for (float x = cell.Position.X + 4f; x < cell.End.X; x += 6f)
                DrawLine(new Vector2(x, top), new Vector2(x, cell.End.Y), FaceStripe, 1f);
            var south = new TilePos(p.X, p.Y + 1);
            if (!sim.Map.InBounds(south) || !structures.HasDeck(south))
                DrawRect(new Rect2(cell.Position.X, cell.End.Y, CellSize, CellSize * 0.22f), Tone(DeckLip, 1f, light, state, false));
            if (structures.HasLadder(p))
            {
                float groundTop = p.Y * CellSize - (sim.Terrain[p] - _minHeightDm) * LiftPerDm;
                var rail = Tone(LadderInk, 1f, Math.Max(light, 40), state, false);
                DrawLine(new Vector2(cell.Position.X + CellSize * 0.35f, cell.End.Y), new Vector2(cell.Position.X + CellSize * 0.35f, groundTop + CellSize), rail, 1.5f);
                DrawLine(new Vector2(cell.Position.X + CellSize * 0.65f, cell.End.Y), new Vector2(cell.Position.X + CellSize * 0.65f, groundTop + CellSize), rail, 1.5f);
                DrawGlyphAt('H', cell.Position.X, top, rail);
            }
            return;
        }

        float lit = o.Lighting ? 0.25f + 0.75f * light / 100f : 1f;
        var flat = new Rect2(p.X * CellSize, p.Y * CellSize, CellSize, CellSize);
        DrawRect(flat, ScaleColor(DeckFill, lit));
        foreach (var d in Directions.Cardinal)
        {
            var n = p.Offset(d);
            if (sim.Map.InBounds(n) && structures.HasDeck(n))
                continue;
            var (dx, dy) = d.Delta();
            var a = TileCenter(p) + new Vector2(dx, dy) * (CellSize / 2f) + new Vector2(-dy, dx) * (CellSize / 2f);
            var b = TileCenter(p) + new Vector2(dx, dy) * (CellSize / 2f) - new Vector2(-dy, dx) * (CellSize / 2f);
            DrawLine(a, b, DeckRim, 2f);
        }
        if (structures.HasLadder(p))
            DrawGlyphAt('H', flat.Position.X, flat.Position.Y, ScaleColor(LadderInk, lit));
    }

    /// <summary>One tile of a large body: core, neck and tail, with its glyph on the head tip.</summary>
    private void DrawLargeBodyTile(BodyTile tile, Actor actor)
    {
        var sim = _sim!;
        var p = tile.Position;
        float top = p.Y * CellSize - LargeBodyLift(p);
        var rect = new Rect2(p.X * CellSize + 1, top + 1, CellSize - 2, CellSize - 2);
        var tip = sim.EyeTile(actor);
        var ink = ColorFor(actor.Glyph);
        bool varied = Overlays.Varied;
        switch (tile.Part)
        {
            case BodyPart.Core:
                DrawRect(rect, RexCore);
                DrawGlyphAt(varied ? (MapLook.Hash(p.X, p.Y, 9) % 2 == 0 ? '▓' : '▒') : 'O', p.X * CellSize, top, ink);
                break;
            case BodyPart.Head:
                DrawRect(rect, RexSoft);
                DrawGlyphAt(p == tip ? actor.Glyph : varied ? '▒' : 'o', p.X * CellSize, top, ink);
                break;
            default:
                DrawRect(rect, RexSoft);
                DrawGlyphAt(varied ? '≈' : '~', p.X * CellSize, top, ink);
                break;
        }
        if (p == tip)
            DrawFacing(new Vector2((p.X + 0.5f) * CellSize, top + CellSize / 2f), actor.Facing);
    }

    /// <summary>The flat view's cliffs: a dark line on the shared edge, with a shadow on the low side.</summary>
    private void DrawFlatCliffs()
    {
        var terrain = _sim!.Terrain;
        for (int y = _y0; y <= _y1; y++)
        {
            for (int x = _x0; x <= _x1; x++)
            {
                var p = new TilePos(x, y);
                if (FogAt(p) is FogState.Unknown or FogState.Plan)
                    continue;
                if (x + 1 < terrain.Width)
                    DrawEastEdge(x, y, terrain.StepDm(p, new TilePos(x + 1, y)));
                if (y + 1 < terrain.Height)
                    DrawSouthEdge(x, y, terrain.StepDm(p, new TilePos(x, y + 1)));
            }
        }
    }

    /// <summary>Debug overlays, the last-seen Rex, aiming, the walk preview and the hovered tile, on top of everything.</summary>
    private void DrawOverlays()
    {
        var sim = _sim!;
        var o = Overlays;
        int width = sim.Map.Width;

        // Rex view: where the Rex would notice a moving human (strong red) or only glimpse one (faint red).
        if (o.RexView && o.RexScores is { } scores)
        {
            for (int i = 0; i < scores.Length; i++)
            {
                if (scores[i] <= 0)
                    continue;
                var p = new TilePos(i % width, i / width);
                DrawRect(new Rect2(p.X * CellSize, p.Y * CellSize - Lift(p), CellSize, CellSize),
                    scores[i] >= o.RexThreshold ? RexSeen : RexGlimpse);
            }
        }

        // Without the light grid's darkness, draw a glow so lights are still easy to place.
        if (!o.Lighting)
        {
            foreach (var light in sim.Lights)
            {
                if (!light.On)
                    continue;
                DrawCircle(CenterOf(light.Position), CellSize * 2.5f, LightGlow);
                DrawCircle(CenterOf(light.Position), CellSize * 1.2f, LightGlow);
            }
        }

        if (o.Noise)
        {
            foreach (var noise in o.Noises)
            {
                var color = noise.ActorId == o.PlayerId ? PlayerNoise : OtherNoise;
                DrawArc(CenterOf(noise.Position), Math.Max(1, noise.Loudness) * CellSize, 0f, Mathf.Tau, 64, color, 1.5f);
            }
        }

        if (o.RexPath)
        {
            foreach (var p in o.RexPathTiles)
                DrawCircle(CenterOf(p), 3f, PathDot);
        }

        if (o.RexMemory)
        {
            foreach (var mark in o.Contacts)
            {
                float alpha = mark.Class == ContactClass.Object ? 0.3f : 0.35f + 0.6f * mark.Confidence / 100f;
                var color = mark.Class switch
                {
                    ContactClass.Prey => new Color(1f, 0.2f, 0.15f, alpha),
                    ContactClass.Unknown => new Color(1f, 0.85f, 0.2f, alpha),
                    _ => new Color(0.65f, 0.65f, 0.65f, alpha),
                };
                float radius = (mark.Radius + 0.5f) * CellSize;
                var center = CenterOf(mark.At);
                DrawArc(center, radius, 0f, Mathf.Tau, 48, color, mark.Focus ? 3f : 1.5f);
                char glyph = mark.Class switch
                {
                    ContactClass.Prey => '!',
                    ContactClass.Unknown => '?',
                    _ => 'x',
                };
                DrawGlyphAt(glyph, mark.At.X * CellSize, center.Y - CellSize / 2f, color);
            }
        }

        if (o.Scent)
        {
            long wash = sim.Weather.Wash(sim.CurrentTick);
            foreach (var mark in sim.ScentMarks)
            {
                int strength = mark.StrengthAt(wash);
                if (strength <= 0)
                    continue;
                float alpha = 0.2f + 0.6f * strength / 100f;
                var color = mark.Blood ? new Color(1f, 0.35f, 0.6f, alpha) : new Color(0.45f, 0.95f, 0.35f, alpha);
                DrawCircle(CenterOf(mark.Position) + new Vector2(-4f, -4f), 1.5f + 3f * strength / 100f, color);
            }
        }

        if (o.Vibration)
        {
            foreach (var vibration in o.Vibrations)
            {
                float reach = (vibration.Strength - Species.Human.Senses.VibrationThreshold)
                    / (float)ScentRules.VibrationFalloffPerTile;
                if (reach > 0f)
                    DrawArc(CenterOf(vibration.Position), reach * CellSize, 0f, Mathf.Tau, 64, VibrationRing, 2f);
            }
        }

        // Ropes left tied at the top of a drop.
        foreach (var rope in sim.Ropes)
        {
            if (Seen(rope.Top) || Seen(rope.Bottom))
                DrawLine(CenterOf(rope.Top), CenterOf(rope.Bottom), RopeColor, 2f);
        }

        DrawGhosts();

        // Aiming: the thrower's range and the cursor.
        if (o.Cursor is { } cursor)
        {
            var from = CenterOf(o.CursorFrom);
            DrawArc(from, (o.CursorRange + 0.5f) * CellSize, 0f, Mathf.Tau, 64, CursorColor, 1f);
            DrawLine(from, CenterOf(cursor), CursorColor, 1f);
            DrawRect(new Rect2(cursor.X * CellSize, cursor.Y * CellSize - Lift(cursor), CellSize, CellSize), CursorColor, false, 2f);
        }

        foreach (var p in o.PathPreview)
            DrawCircle(CenterOf(p), 3f, PreviewDot);

        if (o.Hover is { } hover && o.Cursor is null)
            DrawRect(new Rect2(hover.X * CellSize + 1, hover.Y * CellSize - Lift(hover) + 1, CellSize - 2, CellSize - 2), HoverEdge, false, 1.5f);
        if (o.PathLabel is { } label && o.PathPreview.Count > 0)
            DrawLabel(label, CenterOf(o.PathPreview[^1]) + new Vector2(0, -CellSize * 0.75f), LabelInk);
    }

    /// <summary>With the fog on: a dashed outline where you last saw each large creature that's out of sight now.</summary>
    private void DrawGhosts()
    {
        var o = Overlays;
        if (!o.Fog || Fog is null)
            return;
        var sim = _sim!;
        foreach (var ghost in Fog.Ghosts.Values)
        {
            if (o.PlayerSpottedActors.Contains(ghost.ActorId))
                continue;
            if (ghost.ActorId < sim.Actors.Count && sim.Actors[ghost.ActorId].IsDead)
                continue;
            foreach (var tile in ghost.Body)
            {
                var p = tile.Position;
                if (!sim.Map.InBounds(p))
                    continue;
                float top = p.Y * CellSize - LargeBodyLift(p);
                var r = new Rect2(p.X * CellSize + 4f, top + 4f, CellSize - 8f, CellSize - 8f);
                DrawDashedLine(r.Position, new Vector2(r.End.X, r.Position.Y), GhostEdge, 1.5f, 4f);
                DrawDashedLine(new Vector2(r.End.X, r.Position.Y), r.End, GhostEdge, 1.5f, 4f);
                DrawDashedLine(r.End, new Vector2(r.Position.X, r.End.Y), GhostEdge, 1.5f, 4f);
                DrawDashedLine(new Vector2(r.Position.X, r.End.Y), r.Position, GhostEdge, 1.5f, 4f);
            }
            var tipTop = ghost.Tip.Y * CellSize - LargeBodyLift(ghost.Tip);
            DrawLabel($"{ghost.Name}, last seen {Clock(ghost.Tick)}",
                new Vector2((ghost.Tip.X + 0.5f) * CellSize, tipTop - 4f), GhostInk);
        }
    }

    // ---- Helpers ---------------------------------------------------------------------------

    /// <summary>Whether an actor is drawn: you always; others when the fog (or "your view") is off, or when you've spotted them.</summary>
    private bool Shown(Actor actor)
    {
        var o = Overlays;
        if (actor.Id == o.PlayerId)
            return true;
        return !(o.Fog || o.PlayerView) || o.PlayerSpottedActors.Contains(actor.Id);
    }

    /// <summary>Whether a tile is in view or remembered (things left lying there are drawn).</summary>
    private bool Seen(TilePos p) => FogAt(p) is FogState.Visible or FogState.Remembered;

    /// <summary>
    /// A colour as the fog and light leave it. In view: scaled by height shade and (with lighting on) the
    /// light grid. Remembered: a cool, unlit grey. "Your view" debug overlay dims what you can't see.
    /// </summary>
    private Color Tone(Color c, float shade, int light, FogState state, bool dimmed)
    {
        if (state == FogState.Remembered)
        {
            float lum = (c.R * 0.3f + c.G * 0.59f + c.B * 0.11f) * shade * 0.28f;
            return new Color(lum * 0.85f + 0.01f, lum * 0.95f + 0.015f, lum * 1.25f + 0.03f, c.A * 0.7f);
        }
        float f = shade;
        if (Overlays.Lighting)
            f *= 0.12f + 0.88f * light / 100f;
        if (dimmed)
            f *= 0.25f;
        return new Color(c.R * f, c.G * f, c.B * f, c.A);
    }

    /// <summary>
    /// The glyph and ink for a tile: varied or classic, doors by state ('/' open, '+' closed, red '+'
    /// locked; as remembered when out of view), vehicles reddening as they lose integrity, '%' once breached.
    /// </summary>
    private (char Glyph, Color Ink) GlyphFor(TilePos p, CoverType cover, char glyph, Color ink, FogState state)
    {
        var sim = _sim!;
        if (cover == CoverType.Door)
        {
            var door = state == FogState.Remembered && Fog is not null ? Fog.RememberedDoor(p) : sim.Map.Structures.DoorAt(p);
            return door switch
            {
                DoorState.Open => ('/', ink),
                DoorState.Closed => ('+', ink),
                _ => ('+', LockedInk),
            };
        }
        if (cover == CoverType.Vehicle && state == FogState.Visible && sim.VehicleAt(p) is { } vehicle)
        {
            var damaged = ink.Lerp(DamagedInk, 1f - vehicle.Integrity / (float)Vehicle.MaxIntegrity);
            return (vehicle.Breached ? '%' : Overlays.Varied ? '▒' : glyph, damaged);
        }
        return (Overlays.Varied ? MapLook.VariedGlyph(cover, p.X, p.Y, CoverAt) : glyph, ink);
    }

    private CoverType? CoverAt(int x, int y)
    {
        var p = new TilePos(x, y);
        return _sim!.Map.InBounds(p) ? _sim.Cover[p] : null;
    }

    private CoverType? PlanCoverAt(int x, int y)
    {
        var p = new TilePos(x, y);
        return _sim!.Map.InBounds(p) && Fog is not null ? Fog.PlanCover(p) : null;
    }

    /// <summary>Relief shading from the ground heights, lit from the north-west; flat ground is 1.</summary>
    private static float[] Hillshade(Simulation sim)
    {
        var terrain = sim.Terrain;
        int w = terrain.Width, h = terrain.Height;
        var shade = new float[w * h];
        float lx = -1f, ly = -1f, lz = 1.6f;
        float length = Mathf.Sqrt(lx * lx + ly * ly + lz * lz);
        lx /= length;
        ly /= length;
        lz /= length;
        float M(int x, int y) => terrain[new TilePos(Math.Clamp(x, 0, w - 1), Math.Clamp(y, 0, h - 1))] / 10f;
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                float zx = (M(x + 1, y) - M(x - 1, y)) / 2f;
                float zy = (M(x, y + 1) - M(x, y - 1)) / 2f;
                float n = Mathf.Sqrt(zx * zx + zy * zy + 1f);
                float s = (-zx * lx - zy * ly + lz) / n / lz;
                for (int k = 1; k <= 6; k++)
                {
                    if (M(x - k, y - k) - M(x, y) > k * 1.2f)
                    {
                        s *= 0.62f;
                        break;
                    }
                }
                shade[y * w + x] = Math.Clamp(s, 0.35f, 1.4f);
            }
        }
        return shade;
    }

    private static string Clock(long ticks)
    {
        long seconds = ticks / BlueHarvest.Sim.Core.Ticks.PerSecond;
        return $"{seconds / 60}:{seconds % 60:D2}";
    }

    /// <summary>A short line of text on a dark backing, centred above <paramref name="at"/>.</summary>
    private void DrawLabel(string text, Vector2 at, Color ink)
    {
        var size = _font.GetStringSize(text, HorizontalAlignment.Left, -1, LabelFontSize);
        var box = new Rect2(at.X - size.X / 2f - 5f, at.Y - size.Y, size.X + 10f, size.Y + 4f);
        DrawRect(box, LabelBacking);
        DrawString(_font, new Vector2(box.Position.X + 5f, at.Y - _font.GetDescent(LabelFontSize) + 1f), text,
            HorizontalAlignment.Left, -1, LabelFontSize, ink);
    }

    private static Color ScaleColor(Color c, float factor) => new(c.R * factor, c.G * factor, c.B * factor, c.A);

    private void DrawFacing(Vector2 center, Direction facing)
    {
        var (dx, dy) = facing.Delta();
        var unit = new Vector2(dx, dy).Normalized();
        DrawLine(center + unit * (CellSize * 0.3f), center + unit * (CellSize * 0.5f), new Color(1f, 1f, 1f, 0.8f), 2f);
    }

    private void DrawEastEdge(int x, int y, int stepDm)
    {
        if (Math.Abs(stepDm) <= _cliffThresholdDm)
            return;

        float edgeX = (x + 1) * CellSize;
        float shadowX = stepDm > 0 ? edgeX - ShadowWidth : edgeX; // low side is west when east is higher
        DrawRect(new Rect2(shadowX, y * CellSize, ShadowWidth, CellSize), CliffShadow);
        DrawLine(new Vector2(edgeX, y * CellSize), new Vector2(edgeX, (y + 1) * CellSize), CliffLine, 2f);
    }

    private void DrawSouthEdge(int x, int y, int stepDm)
    {
        if (Math.Abs(stepDm) <= _cliffThresholdDm)
            return;

        float edgeY = (y + 1) * CellSize;
        float shadowY = stepDm > 0 ? edgeY - ShadowWidth : edgeY; // low side is north when south is higher
        DrawRect(new Rect2(x * CellSize, shadowY, CellSize, ShadowWidth), CliffShadow);
        DrawLine(new Vector2(x * CellSize, edgeY), new Vector2((x + 1) * CellSize, edgeY), CliffLine, 2f);
    }

    /// <summary>A glyph centred in the cell whose top-left corner is (<paramref name="left"/>, <paramref name="top"/>).</summary>
    private void DrawGlyphAt(char glyph, float left, float top, Color color)
    {
        string text = glyph.ToString();
        float width = _font.GetStringSize(text, HorizontalAlignment.Left, -1, FontSize).X;
        DrawString(_font, new Vector2(left + (CellSize - width) / 2f, top + _baseline), text,
            HorizontalAlignment.Left, -1, FontSize, color);
    }

    private static Color ColorFor(char glyph) => glyph switch
    {
        '@' => new Color(1f, 0.95f, 0.6f),
        'h' => new Color(0.55f, 0.90f, 1f),
        'R' => new Color(1f, 0.6f, 0.2f),
        'T' => new Color(1f, 0.28f, 0.22f),
        _ => Colors.White,
    };
}
