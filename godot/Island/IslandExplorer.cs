#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BlueHarvest.Game.Ui;
using BlueHarvest.Sim.World.Generation;
using Godot;

namespace BlueHarvest.Game.Island;

/// <summary>
/// Island generator and map. Type a seed (or roll one) and generate; drag to pan, wheel to zoom.
/// Basic view shows only what a survivor knows on arrival: the island's outline, and later whatever
/// they have unlocked. Debug view (F9) reveals the whole island: layers, roads, facilities and the rest,
/// and zoomed far enough in it streams the 1 m tiles: buildings, rooms, fences, river beds.
/// Esc goes back to the menu.
/// </summary>
public partial class IslandExplorer : Node2D
{
    private const float PanelWidth = 330f;
    private const float PanSpeed = 900f;
    /// <summary>Screen pixels per grid cell at which the debug view starts streaming 1 m tiles (1.5 px per tile).</summary>
    private const float TileZoom = 192f;
    private const float MaxZoom = 128f * 16f;

    private IslandWorld? _world;
    private Task<IslandWorld>? _job;
    private volatile string _stage = "";
    private ulong _jobStarted;

    private Sprite2D _sprite = null!;
    private IslandOverlay _overlay = null!;
    private IslandTileLayer _tiles = null!;
    private Camera2D _camera = null!;
    private float _zoom = 1f;

    private SpinBox _seed = null!;
    private OptionButton _size = null!;
    private Button _generate = null!;
    private CheckButton _debug = null!;
    private Label _status = null!;
    private Control _debugBox = null!;
    private Control _basicBox = null!;
    private OptionButton _layer = null!;
    private RichTextLabel _facts = null!;
    private RichTextLabel _hover = null!;

    private static readonly (string Name, double Scale)[] Sizes = { ("Small (50%)", 50), ("Standard (75%)", 75), ("Large (100%)", 100) };
    private static readonly (string Name, MapLayer Layer)[] Layers =
    {
        ("Biomes", MapLayer.Biomes), ("Height", MapLayer.Height), ("Rainfall", MapLayer.Rain), ("Regions", MapLayer.Regions),
    };

    private bool DebugOn => _debug.ButtonPressed;

    public override void _Ready()
    {
        RenderingServer.SetDefaultClearColor(new Color(0.02f, 0.02f, 0.03f));
        var font = new SystemFont
        {
            FontNames = new[] { "Consolas", "Cascadia Mono", "DejaVu Sans Mono", "Courier New", "monospace" },
        };

        _sprite = new Sprite2D { Centered = false, TextureFilter = TextureFilterEnum.Nearest };
        AddChild(_sprite);
        _tiles = new IslandTileLayer();
        AddChild(_tiles);
        _overlay = new IslandOverlay();
        _overlay.Bind(font);
        AddChild(_overlay);
        _camera = new Camera2D();
        AddChild(_camera);
        _camera.MakeCurrent();

        var layer = new CanvasLayer();
        AddChild(layer);
        BuildPanel(layer, font);
        ApplyMode();
        StartGeneration();
    }

    // ---------- the panel ----------
    private void BuildPanel(CanvasLayer layer, Font font)
    {
        var panel = new PanelContainer
        {
            AnchorBottom = 1f,
            OffsetRight = PanelWidth,
            MouseFilter = Control.MouseFilterEnum.Stop,
            Theme = HudTheme.Build(font),
        };
        panel.AddThemeStyleboxOverride("panel", HudTheme.Box(HudTheme.Panel, HudTheme.Line, 0, 14));
        layer.AddChild(panel);
        var column = HudTheme.Column(8);
        panel.AddChild(column);

        var title = HudTheme.Heading("Island generator");
        title.AddThemeColorOverride("font_color", HudTheme.Amber);
        column.AddChild(title);

        column.AddChild(HudTheme.Heading("Seed"));
        var seedRow = new HBoxContainer();
        column.AddChild(seedRow);
        _seed = new SpinBox { MinValue = 0, MaxValue = uint.MaxValue, Step = 1, Value = 7, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        seedRow.AddChild(_seed);
        Button roll = HudTheme.PlainButton("Random");
        roll.Pressed += () =>
        {
            _seed.Value = GD.Randi();
            StartGeneration();
        };
        seedRow.AddChild(roll);

        column.AddChild(HudTheme.Heading("Size"));
        _size = new OptionButton { FocusMode = Control.FocusModeEnum.None };
        foreach ((string name, double _) in Sizes) _size.AddItem(name);
        _size.Selected = 1;
        column.AddChild(_size);

        _generate = HudTheme.PlainButton("Generate  (G)");
        _generate.Pressed += StartGeneration;
        column.AddChild(_generate);
        _status = HudTheme.Text("", HudTheme.SmallSize, HudTheme.Muted);
        column.AddChild(_status);

        _debug = new CheckButton { Text = "Debug view: reveal everything  (F9)", FocusMode = Control.FocusModeEnum.None };
        _debug.AddThemeFontSizeOverride("font_size", HudTheme.SmallSize);
        _debug.Toggled += _ => ApplyMode();
        column.AddChild(_debug);

        // basic view: the outline and what has been unlocked
        var basic = HudTheme.Column(6);
        _basicBox = basic;
        column.AddChild(basic);
        basic.AddChild(HudTheme.Heading("Known so far"));
        basic.AddChild(HudTheme.Text("The island's outline, as seen from the boat.", HudTheme.SmallSize));
        basic.AddChild(HudTheme.Heading("Unlocks"));
        basic.AddChild(HudTheme.Text("Nothing yet. Maps, survey data and facility records will fill this in.", HudTheme.SmallSize, HudTheme.Muted));

        // debug view: layers, overlays and the numbers
        var debug = HudTheme.Column(6);
        _debugBox = debug;
        column.AddChild(debug);
        debug.AddChild(HudTheme.Heading("Layer"));
        _layer = new OptionButton { FocusMode = Control.FocusModeEnum.None };
        foreach ((string name, MapLayer _) in Layers) _layer.AddItem(name);
        _layer.ItemSelected += _ => ShowPicture();
        debug.AddChild(_layer);
        debug.AddChild(HudTheme.Heading("Show"));
        var grid = new GridContainer { Columns = 2 };
        debug.AddChild(grid);
        AddToggle(grid, "Rivers", () => _overlay.Rivers, v => _overlay.Rivers = v);
        AddToggle(grid, "Roads", () => _overlay.Roads, v => _overlay.Roads = v);
        AddToggle(grid, "Facilities", () => _overlay.Sites, v => _overlay.Sites = v);
        AddToggle(grid, "Paddocks", () => _overlay.Paddocks, v => _overlay.Paddocks = v);
        AddToggle(grid, "Transit", () => _overlay.Transit, v => _overlay.Transit = v);
        AddToggle(grid, "Power lines", () => _overlay.Power, v => _overlay.Power = v);
        AddToggle(grid, "Caves, wrecks, ruins", () => _overlay.Relics, v => _overlay.Relics = v);
        AddToggle(grid, "Ley lines", () => _overlay.LeyLines, v => _overlay.LeyLines = v);
        debug.AddChild(HudTheme.Heading("Island"));
        _facts = HudTheme.Rich(HudTheme.SmallSize);
        debug.AddChild(_facts);

        column.AddChild(HudTheme.Heading("Under the cursor"));
        _hover = HudTheme.Rich(HudTheme.SmallSize);
        _hover.CustomMinimumSize = new Vector2(0, 90);
        column.AddChild(_hover);

        column.AddChild(new Control { SizeFlagsVertical = Control.SizeFlags.ExpandFill });
        column.AddChild(HudTheme.Text("Drag to pan, wheel to zoom, Home to fit. In the debug view, zoom far in to see the 1 m tiles.", HudTheme.SmallSize, HudTheme.Muted));
        Button back = HudTheme.PlainButton("Back to menu  (Esc)");
        back.Pressed += () => GetTree().ChangeSceneToFile(FrontMenu.MenuScene);
        column.AddChild(back);
    }

    private void AddToggle(Container parent, string text, Func<bool> get, Action<bool> set)
    {
        var box = new CheckBox { Text = text, ButtonPressed = get(), FocusMode = Control.FocusModeEnum.None };
        box.Toggled += on =>
        {
            set(on);
            _overlay.QueueRedraw();
        };
        parent.AddChild(box);
    }

    // ---------- generating ----------
    private void StartGeneration()
    {
        if (_job != null) return; // one at a time
        var p = new IslandParams { Seed = (uint)_seed.Value, Scale = Sizes[Math.Max(0, _size.Selected)].Scale };
        _stage = "Starting";
        _jobStarted = Time.GetTicksMsec();
        _generate.Disabled = true;
        _job = Task.Run(() => IslandWorld.Generate(p, stage => _stage = stage));
    }

    public override void _Process(double delta)
    {
        if (_job != null)
        {
            if (!_job.IsCompleted)
            {
                _status.Text = $"{_stage}… {(Time.GetTicksMsec() - _jobStarted) / 1000.0:0.0} s";
            }
            else
            {
                Task<IslandWorld> job = _job;
                _job = null;
                _generate.Disabled = false;
                if (job.IsFaulted)
                {
                    _status.Text = "Generation failed: " + (job.Exception?.GetBaseException().Message ?? "unknown error");
                    GD.PushError(job.Exception?.ToString() ?? "island generation failed");
                }
                else
                {
                    _world = job.Result;
                    _status.Text = $"Seed {_world.Params.Seed} generated in {(Time.GetTicksMsec() - _jobStarted) / 1000.0:0.0} s";
                    ShowPicture();
                    ShowFacts();
                    _overlay.SetWorld(_world);
                    _tiles.SetWorld(_world);
                    FitView();
                }
            }
        }

        Vector2 dir = Vector2.Zero;
        if (_seed.GetLineEdit().HasFocus()) dir = Vector2.Zero;
        else
        {
            if (Input.IsPhysicalKeyPressed(Key.W) || Input.IsPhysicalKeyPressed(Key.Up)) dir.Y -= 1;
            if (Input.IsPhysicalKeyPressed(Key.S) || Input.IsPhysicalKeyPressed(Key.Down)) dir.Y += 1;
            if (Input.IsPhysicalKeyPressed(Key.A) || Input.IsPhysicalKeyPressed(Key.Left)) dir.X -= 1;
            if (Input.IsPhysicalKeyPressed(Key.D) || Input.IsPhysicalKeyPressed(Key.Right)) dir.X += 1;
        }
        if (dir != Vector2.Zero) _camera.Position += dir * PanSpeed * (float)delta / _zoom;

        bool tileView = DebugOn && _zoom >= TileZoom && _world != null;
        _tiles.Visible = tileView;
        _overlay.SetTileView(tileView);
        if (tileView)
        {
            Vector2 view = GetViewportRect().Size;
            _tiles.Stream(new Rect2(_camera.Position - view / 2f / _zoom, view / _zoom));
        }
        UpdateHover();
    }

    // ---------- what is shown ----------
    private void ApplyMode()
    {
        bool debug = DebugOn;
        _debugBox.Visible = debug;
        _basicBox.Visible = !debug;
        _overlay.Visible = debug;
        ShowPicture();
    }

    private void ShowPicture()
    {
        if (_world == null) return;
        MapLayer layer = DebugOn ? Layers[Math.Max(0, _layer.Selected)].Layer : MapLayer.Outline;
        byte[] pixels = IslandMapImage.Render(_world, layer);
        Image image = Image.CreateFromData(_world.Shape.Nx, _world.Shape.Ny, false, Image.Format.Rgba8, pixels);
        _sprite.Texture = ImageTexture.CreateFromImage(image);
    }

    private void ShowFacts()
    {
        if (_world == null) return;
        IslandShape s = _world.Shape;
        IslandWorks k = _world.Works;
        int land = 0;
        double peak = 0;
        for (int i = 0; i < s.N; i++)
        {
            if (s.Land[i] == 0) continue;
            land++;
            peak = Math.Max(peak, s.Height[i]);
        }
        double km2 = land * s.Cell * s.Cell / 1e6;
        var lines = new List<string>
        {
            $"{_world.WidthM / 1000:0} × {_world.HeightM / 1000:0} km frame, {km2:0} km² of land",
            $"Peak {peak:0} m, {s.Rivers.Count} rivers, {s.Lakes.Count} lakes, {s.Falls.Count} falls",
            $"{k.Sites.Count} facilities, {k.Paddocks.Count} paddocks",
            $"{k.RoadKm:0} km of road, {k.Bridges} bridges, {k.Tunnels} tunnels",
            $"{k.Lifts.Count} lifts, tram {(k.Tram != null ? $"{k.Tram.Km:0.0} km" : "none")}, monorail {(k.Monorail != null ? $"{k.Monorail.Km:0.0} km" : "none")}",
            $"{k.Caves.Count} caves, {k.Wrecks.Count} wrecks, {k.Ruins.Count} ruins",
        };
        _facts.Text = string.Join("\n", lines);
    }

    private Vector2 MouseCell()
    {
        Vector2 view = GetViewportRect().Size, mouse = GetViewport().GetMousePosition();
        return _camera.Position + (mouse - view / 2f) / _zoom;
    }

    private void UpdateHover()
    {
        if (_world == null) { _hover.Text = ""; return; }
        IslandShape s = _world.Shape;
        Vector2 at = MouseCell();
        int x = Mathf.FloorToInt(at.X), y = Mathf.FloorToInt(at.Y);
        if (GetViewport().GetMousePosition().X < PanelWidth || x < 0 || y < 0 || x >= s.Nx || y >= s.Ny) { _hover.Text = ""; return; }
        int i = y * s.Nx + x;
        double xm = at.X * s.Cell, ym = at.Y * s.Cell;
        bool land = s.Land[i] != 0;
        if (!DebugOn)
        {
            _hover.Text = land ? "Land. Unexplored." : "Open sea.";
            return;
        }
        IslandClimate c = _world.Climate;
        var lines = new List<string> { $"{xm / 1000:0.0} km east, {ym / 1000:0.0} km south" };
        if (!land)
        {
            lines.Add(s.Sea[i] switch { SeaBand.Reef => "Reef", SeaBand.Lagoon => "Lagoon", SeaBand.Shelf => "Shallow sea", _ => "Deep sea" });
        }
        else
        {
            lines.Add($"{Legend.Biomes[c.Biomes[i]].Name}, {s.Height[i]:0} m");
            lines.Add($"{Legend.Regions[c.Regions[i]].Name}");
            lines.Add($"{s.Rain[i]:0} mm rain, {c.Temperature[i]:0.0} °C, slope {c.SlopeDeg[i]:0}°");
            if (_world.Works.PaddockCell[i] >= 0) lines.Add(_world.Works.Paddocks[_world.Works.PaddockCell[i]].Name);
        }
        if (_tiles.Visible)
        {
            if (_tiles.TryGetTile(xm, ym, out IslandChunk chunk, out int tile))
            {
                string cover = CoverLegend.Looks[chunk.Cover[tile]].Name;
                float water = chunk.Water[tile];
                lines.Add(float.IsNaN(water)
                    ? $"[color={HudTheme.Hex(HudTheme.Safe)}]Tile:[/color] {cover}, {chunk.Height[tile]:0.0} m"
                    : $"[color={HudTheme.Hex(HudTheme.Safe)}]Tile:[/color] {cover}, {water - chunk.Height[tile]:0.0} m deep");
                if (chunk.Label[tile] >= 0) lines.Add(HudTheme.Escape(_world.Works.Labels[chunk.Label[tile]]));
            }
            else lines.Add($"Building tiles… ({_tiles.PendingCount} chunks on the way)");
        }
        Site? near = null;
        double best = double.MaxValue;
        foreach (Site t in _world.Works.Sites)
        {
            double d = Math.Sqrt((t.X - xm) * (t.X - xm) + (t.Y - ym) * (t.Y - ym));
            if (d < Math.Max(t.R, 6 * s.Cell / Math.Max(_zoom, 0.01)) && d < best) { best = d; near = t; }
        }
        if (near != null)
        {
            lines.Add($"[color={HudTheme.Hex(HudTheme.Amber)}]{HudTheme.Escape(near.Name)}[/color]  ({HudTheme.Escape(near.Owner)})");
            if (near.Nearest is { } n) lines.Add($"Nearest: {HudTheme.Escape(n.Name)}, {n.Minutes:0} min on foot");
        }
        _hover.Text = string.Join("\n", lines);
    }

    // ---------- the view ----------
    private void SetZoom(float zoom)
    {
        _zoom = Mathf.Clamp(zoom, 0.25f, MaxZoom);
        _camera.Zoom = new Vector2(_zoom, _zoom);
        _overlay.SetZoom(_zoom);
    }

    /// <summary>Fits the whole island in the space right of the panel.</summary>
    private void FitView()
    {
        if (_world == null) return;
        Vector2 view = GetViewportRect().Size;
        float w = _world.Shape.Nx, h = _world.Shape.Ny;
        SetZoom(Mathf.Min((view.X - PanelWidth) / w, view.Y / h) * 0.95f);
        // the panel covers the left of the screen, so centre the island in what is left
        _camera.Position = new Vector2(w / 2f - PanelWidth / 2f / _zoom, h / 2f);
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        switch (@event)
        {
            case InputEventMouseButton { Pressed: true } button when button.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown:
            {
                Vector2 view = GetViewportRect().Size, offset = button.Position - view / 2f;
                Vector2 anchor = _camera.Position + offset / _zoom;
                SetZoom(_zoom * (button.ButtonIndex == MouseButton.WheelUp ? 1.2f : 1f / 1.2f));
                _camera.Position = anchor - offset / _zoom; // keep the point under the cursor still
                break;
            }
            case InputEventMouseMotion motion when (motion.ButtonMask & (MouseButtonMask.Left | MouseButtonMask.Middle | MouseButtonMask.Right)) != 0:
                _camera.Position -= motion.Relative / _zoom;
                break;
            case InputEventKey { Pressed: true, Echo: false } key:
                if (key.Keycode == Key.Escape) GetTree().ChangeSceneToFile(FrontMenu.MenuScene);
                else if (key.Keycode == Key.F9) _debug.ButtonPressed = !_debug.ButtonPressed;
                else if (key.Keycode == Key.G) StartGeneration();
                else if (key.Keycode == Key.Home) FitView();
                break;
        }
    }
}
