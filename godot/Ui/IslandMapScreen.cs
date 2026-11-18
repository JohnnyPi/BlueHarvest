#nullable enable
using System;
using System.Linq;
using BlueHarvest.Game.Island;
using BlueHarvest.Sim.World;
using BlueHarvest.Sim.World.Generation;
using Godot;

namespace BlueHarvest.Game.Ui;

/// <summary>
/// The survivor's map of the island (step 25b-3), on M: full screen, over everything but the menu.
/// The outline always; the ground as it looks where they've seen it, muted where they only have it from a
/// park map; and over that what they know (<see cref="IslandOverlay"/> with their knowledge set).
/// Drag to pan, wheel or + − to zoom, W A S D or the arrows to pan, Home to centre on yourself,
/// F to fit the island. M or Esc closes it. No game time passes while it's open: the game only moves
/// when the player acts, and every key goes to the map.
/// </summary>
public partial class IslandMapScreen : Control
{
    private const float MinZoom = 0.5f;
    private const float MaxZoom = 96f;
    private const float PanSpeed = 900f;
    /// <summary>The zoom Home goes to if the map is further out than this: about 10 km across a big screen.</summary>
    private const float NearZoom = 12f;

    private static readonly Color Backdrop = new(0.02f, 0.02f, 0.03f);

    private Node2D _view = null!;
    private Sprite2D _picture = null!;
    private IslandOverlay _overlay = null!;
    private RichTextLabel _facts = null!;
    private RichTextLabel _hover = null!;

    private IslandWorld? _world;
    private MapKnowledge? _known;
    private bool[] _charted = Array.Empty<bool>();
    private Vector2 _here;
    private Vector2 _centre;
    private float _zoom = 4f;
    private bool _fitted;

    public IslandMapScreen()
    {
        AnchorRight = 1f;
        AnchorBottom = 1f;
        MouseFilter = MouseFilterEnum.Stop;
        ClipContents = true;
        Visible = false;
    }

    /// <summary>Builds the map's parts. Call once.</summary>
    public void Bind(Font font)
    {
        _view = new Node2D();
        AddChild(_view);
        _picture = new Sprite2D { Centered = false, TextureFilter = TextureFilterEnum.Linear };
        _view.AddChild(_picture);
        _overlay = new IslandOverlay();
        _overlay.Bind(font);
        _view.AddChild(_overlay);

        var title = new PanelContainer
        {
            OffsetLeft = 16f,
            OffsetTop = 16f,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        title.AddThemeStyleboxOverride("panel", HudTheme.Box(HudTheme.Panel, HudTheme.Amber, 4, 10));
        var column = HudTheme.Column(4);
        column.CustomMinimumSize = new Vector2(360f, 0f);
        title.AddChild(column);
        var heading = HudTheme.Heading("Your map of the island");
        heading.AddThemeColorOverride("font_color", HudTheme.Amber);
        column.AddChild(heading);
        _facts = HudTheme.Rich(HudTheme.SmallSize);
        column.AddChild(_facts);
        _hover = HudTheme.Rich(HudTheme.SmallSize);
        column.AddChild(_hover);
        column.AddChild(HudTheme.Text("Drag or W A S D to pan · wheel or + − to zoom · Home: you · F: the whole island · M or Esc closes",
            HudTheme.SmallSize, HudTheme.Muted));
        AddChild(title);
    }

    /// <summary>Opens the map: what this survivor knows, with them at <paramref name="here"/> (in cells).</summary>
    public void Open(IslandWorld world, MapKnowledge known, Vector2 here)
    {
        if (world != _world)
            _fitted = false;
        _world = world;
        _known = known;
        _here = here;
        _charted = IslandMapImage.ChartedCells(world, known);
        byte[] pixels = IslandMapImage.RenderKnown(world, known);
        Image image = Image.CreateFromData(world.Shape.Nx, world.Shape.Ny, false, Image.Format.Rgba8, pixels);
        _picture.Texture = ImageTexture.CreateFromImage(image);
        _overlay.SetWorld(world);
        _overlay.SetKnowledge(known, here);
        ShowFacts();
        _hover.Text = "";
        Visible = true;
        // the first time, the whole island; after that the same zoom, on you
        if (!_fitted || _zoom <= FitZoom() * 1.01f)
        {
            FitIsland();
            _fitted = true;
        }
        else
        {
            _centre = _here;
            Apply();
        }
    }

    public void Close() => Visible = false;

    /// <summary>Every key goes to the map while it's open. M or Esc closes it.</summary>
    public void HandleKey(InputEventKey key)
    {
        if (key.Echo)
            return;
        switch (key.Keycode)
        {
            case Key.M or Key.Escape:
                Close();
                break;
            case Key.Home:
                _centre = _here;
                SetZoom(Mathf.Max(_zoom, NearZoom));
                break;
            case Key.F:
                FitIsland();
                break;
            case Key.Equal or Key.Plus or Key.KpAdd:
                SetZoom(_zoom * 1.25f);
                break;
            case Key.Minus or Key.KpSubtract:
                SetZoom(_zoom / 1.25f);
                break;
        }
    }

    public override void _Process(double delta)
    {
        if (!Visible)
            return;
        var direction = Vector2.Zero;
        if (Input.IsPhysicalKeyPressed(Key.W) || Input.IsPhysicalKeyPressed(Key.Up)) direction.Y -= 1f;
        if (Input.IsPhysicalKeyPressed(Key.S) || Input.IsPhysicalKeyPressed(Key.Down)) direction.Y += 1f;
        if (Input.IsPhysicalKeyPressed(Key.A) || Input.IsPhysicalKeyPressed(Key.Left)) direction.X -= 1f;
        if (Input.IsPhysicalKeyPressed(Key.D) || Input.IsPhysicalKeyPressed(Key.Right)) direction.X += 1f;
        if (direction == Vector2.Zero)
            return;
        _centre += direction.Normalized() * PanSpeed * (float)delta / _zoom;
        Apply();
    }

    public override void _GuiInput(InputEvent @event)
    {
        switch (@event)
        {
            case InputEventMouseButton { Pressed: true } button when button.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown:
            {
                Vector2 anchor = ToCells(button.Position);
                SetZoom(_zoom * (button.ButtonIndex == MouseButton.WheelUp ? 1.2f : 1f / 1.2f));
                _centre = anchor - (button.Position - Size / 2f) / _zoom; // keep the point under the cursor still
                Apply();
                break;
            }
            case InputEventMouseMotion motion:
                if ((motion.ButtonMask & (MouseButtonMask.Left | MouseButtonMask.Middle | MouseButtonMask.Right)) != 0)
                {
                    _centre -= motion.Relative / _zoom;
                    Apply();
                }
                ShowHover(ToCells(motion.Position));
                break;
        }
        AcceptEvent(); // nothing under the map hears the mouse
    }

    public override void _Notification(int what)
    {
        if (what == NotificationResized && _world is not null)
            Apply();
    }

    public override void _Draw() => DrawRect(new Rect2(Vector2.Zero, Size), Backdrop);

    // ---------- the view ----------

    private Vector2 ToCells(Vector2 screen) => _centre + (screen - Size / 2f) / _zoom;

    private float FitZoom()
    {
        if (_world is null || Size.X <= 0 || Size.Y <= 0)
            return 1f;
        return Mathf.Min(Size.X / _world.Shape.Nx, Size.Y / _world.Shape.Ny) * 0.92f;
    }

    private void FitIsland()
    {
        if (_world is null)
            return;
        _centre = new Vector2(_world.Shape.Nx / 2f, _world.Shape.Ny / 2f);
        SetZoom(FitZoom());
    }

    private void SetZoom(float zoom)
    {
        _zoom = Mathf.Clamp(zoom, MinZoom, MaxZoom);
        _overlay.SetZoom(_zoom);
        Apply();
    }

    private void Apply()
    {
        _view.Scale = new Vector2(_zoom, _zoom);
        _view.Position = Size / 2f - _centre * _zoom;
        QueueRedraw();
    }

    // ---------- the words ----------

    private void ShowFacts()
    {
        if (_world is not { } world || _known is not { } known)
            return;
        IslandShape s = world.Shape;
        int land = 0, seenLand = 0;
        for (int i = 0; i < s.N; i++)
        {
            if (s.Land[i] == 0) continue;
            land++;
            if (known.HasSeen(i)) seenLand++;
        }
        int Found(FeatureKind kind) => known.Features.Count(f => f.Kind == kind);
        string entered = known.Sites.Count == 0
            ? "none yet"
            : string.Join(", ", known.Sites.Where(a => a >= 0 && a < world.Works.Sites.Count).OrderBy(a => a).Select(a => HudTheme.Escape(world.Works.Sites[a].Name)));
        _facts.Text =
            $"Seen: [color={HudTheme.Hex(HudTheme.Amber)}]{(land == 0 ? 0 : 100.0 * seenLand / land):0.0}%[/color] of the land\n" +
            $"Park-map sheets: {known.Sheets.Count}\n" +
            $"Found: {Found(FeatureKind.Ruin)} ruins, {Found(FeatureKind.Wreck)} wrecks, {Found(FeatureKind.Cave)} caves\n" +
            $"Been in: {entered}";
    }

    private void ShowHover(Vector2 at)
    {
        if (_world is not { } world || _known is not { } known)
            return;
        IslandShape s = world.Shape;
        int cx = Mathf.FloorToInt(at.X), cy = Mathf.FloorToInt(at.Y);
        if (cx < 0 || cy < 0 || cx >= s.Nx || cy >= s.Ny)
        {
            _hover.Text = "";
            return;
        }
        int i = cy * s.Nx + cx;
        string what;
        if (known.HasSeen(i))
            what = s.Land[i] == 0 ? "Sea, seen." : "Seen.";
        else if (_charted.Length == s.N && _charted[i])
        {
            int area = world.Areas().AreaOf(i);
            what = area >= 0 && area < world.Works.Sites.Count
                ? $"On your park map of the ground round {HudTheme.Escape(world.Works.Sites[area].Name)}."
                : "On a park map.";
        }
        else
            what = s.Land[i] == 0 ? "Sea." : "Unexplored.";
        _hover.Text = $"[color={HudTheme.Hex(HudTheme.Muted)}]Under the cursor:[/color] {what} {Bearing(at - _here, s.Cell)}";
    }

    /// <summary>"2.4 km north-east of you", from an offset in cells.</summary>
    private static string Bearing(Vector2 offset, double cellM)
    {
        double km = offset.Length() * cellM / 1000.0;
        if (km < 0.2)
            return "Where you are.";
        // screen y grows southward; 0° is north, clockwise
        double deg = (Math.Atan2(offset.X, -offset.Y) * 180.0 / Math.PI + 360.0) % 360.0;
        string[] names = { "north", "north-east", "east", "south-east", "south", "south-west", "west", "north-west" };
        string way = names[(int)Math.Round(deg / 45.0) % 8];
        return $"{km:0.0} km {way} of you.";
    }
}
