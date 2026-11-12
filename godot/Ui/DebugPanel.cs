#nullable enable
using System;
using System.Collections.Generic;
using BlueHarvest.Game.Rendering;
using Godot;

namespace BlueHarvest.Game.Ui;

/// <summary>
/// Everything that isn't the game: the map overlays, the omniscient Rex, the weather presets and a
/// readout of the numbers behind the HUD. F9 opens it; while it's open, 1-8 toggle the overlays and
/// G the omniscient Rex.
/// </summary>
public partial class DebugPanel : PanelContainer
{
    public const float PanelWidth = 560f;

    private readonly List<(CheckBox Box, Key Hotkey, Func<bool> Get, Action<bool> Set)> _toggles = new();
    private CheckBox _hunt = null!;
    private OptionButton _weather = null!;
    private OptionButton _time = null!;
    private RichTextLabel _readout = null!;

    /// <summary>An overlay was switched on or off.</summary>
    public event Action? OverlaysChanged;

    /// <summary>The omniscient Rex was switched on (true) or off.</summary>
    public event Action<bool>? HuntToggled;

    /// <summary>A weather preset was picked, by index.</summary>
    public event Action<int>? WeatherChosen;

    /// <summary>A time of day was picked, by index.</summary>
    public event Action<int>? TimeChosen;

    public DebugPanel()
    {
        AnchorLeft = 0.5f;
        AnchorRight = 0.5f;
        AnchorTop = 0f;
        AnchorBottom = 0f;
        // Centred over the map, which ends where the sidebar starts.
        OffsetLeft = -PanelWidth / 2f - Sidebar.PanelWidth / 2f;
        OffsetRight = PanelWidth / 2f - Sidebar.PanelWidth / 2f;
        OffsetTop = 10f;
        OffsetBottom = 10f;
        MouseFilter = MouseFilterEnum.Stop;
        Visible = false;
        AddThemeStyleboxOverride("panel", HudTheme.Box(HudTheme.Panel, HudTheme.Amber, 4, 12));
    }

    /// <summary>Builds the controls for these overlays, weather presets and times of day. Call once.</summary>
    public void Bind(OverlayState overlays, IReadOnlyList<string> weatherNames, IReadOnlyList<string> timeNames)
    {
        var column = HudTheme.Column(8);
        AddChild(column);
        var title = HudTheme.Heading("Debug  ·  F9 closes");
        title.AddThemeColorOverride("font_color", HudTheme.Amber);
        column.AddChild(title);

        var grid = new GridContainer { Columns = 3 };
        grid.AddThemeConstantOverride("h_separation", 16);
        grid.AddThemeConstantOverride("v_separation", 2);
        column.AddChild(grid);
        var o = overlays;
        Toggle(grid, "1  Lighting", Key.Key1, () => o.Lighting, v => o.Lighting = v);
        Toggle(grid, "2  Your view", Key.Key2, () => o.PlayerView, v => o.PlayerView = v);
        Toggle(grid, "3  Rex view", Key.Key3, () => o.RexView, v => o.RexView = v);
        Toggle(grid, "4  Noise rings", Key.Key4, () => o.Noise, v => o.Noise = v);
        Toggle(grid, "5  Rex path", Key.Key5, () => o.RexPath, v => o.RexPath = v);
        Toggle(grid, "6  Rex memory", Key.Key6, () => o.RexMemory, v => o.RexMemory = v);
        Toggle(grid, "7  Scent", Key.Key7, () => o.Scent, v => o.Scent = v);
        Toggle(grid, "8  Vibration", Key.Key8, () => o.Vibration, v => o.Vibration = v);

        _hunt = new CheckBox { Text = "G  Omniscient Rex", FocusMode = FocusModeEnum.None };
        _hunt.Toggled += on => HuntToggled?.Invoke(on);
        grid.AddChild(_hunt);

        // How the map is drawn: the chosen look, each part switchable for comparison.
        column.AddChild(HudTheme.Heading("View"));
        var view = new GridContainer { Columns = 3 };
        view.AddThemeConstantOverride("h_separation", 16);
        view.AddThemeConstantOverride("v_separation", 2);
        column.AddChild(view);
        Toggle(view, "Raised 3/4", Key.None, () => o.Raised, v => o.Raised = v);
        Toggle(view, "Varied tiles", Key.None, () => o.Varied, v => o.Varied = v);
        Toggle(view, "Fog of war", Key.None, () => o.Fog, v => o.Fog = v);
        Toggle(view, "Roofs", Key.None, () => o.Roofs, v => o.Roofs = v);
        Toggle(view, "Edge markers", Key.None, () => o.EdgeMarkers, v => o.EdgeMarkers = v);
        Toggle(view, "Moving water", Key.None, () => o.Animation, v => o.Animation = v);

        var weatherRow = new HBoxContainer();
        weatherRow.AddThemeConstantOverride("separation", 10);
        var weatherLabel = HudTheme.Text("Weather", HudTheme.SmallSize, HudTheme.Muted);
        weatherLabel.AutowrapMode = TextServer.AutowrapMode.Off;
        weatherLabel.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        _weather = new OptionButton { FocusMode = FocusModeEnum.None };
        for (int i = 0; i < weatherNames.Count; i++)
            _weather.AddItem(weatherNames[i], i);
        _weather.ItemSelected += index => WeatherChosen?.Invoke((int)index);
        weatherRow.AddChild(weatherLabel);
        weatherRow.AddChild(_weather);

        // jump the clock forward to a time of day (maps with a clock only)
        var timeLabel = HudTheme.Text("Skip to", HudTheme.SmallSize, HudTheme.Muted);
        timeLabel.AutowrapMode = TextServer.AutowrapMode.Off;
        timeLabel.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        _time = new OptionButton { FocusMode = FocusModeEnum.None };
        for (int i = 0; i < timeNames.Count; i++)
            _time.AddItem(timeNames[i], i);
        _time.Select(-1);
        _time.Text = "time of day";
        _time.ItemSelected += index =>
        {
            TimeChosen?.Invoke((int)index);
            _time.Select(-1); // a button to press again, not a setting
            _time.Text = "time of day";
        };
        weatherRow.AddChild(timeLabel);
        weatherRow.AddChild(_time);
        column.AddChild(weatherRow);

        _readout = HudTheme.Rich(HudTheme.SmallSize);
        column.AddChild(_readout);
    }

    /// <summary>Handles 1-8 and G while the panel is open. True if the key was one of them.</summary>
    public bool TryHotkey(Key key)
    {
        if (!Visible || key == Key.None)
            return false;
        if (key == Key.G)
        {
            _hunt.ButtonPressed = !_hunt.ButtonPressed; // raises Toggled
            return true;
        }
        foreach (var toggle in _toggles)
        {
            if (toggle.Hotkey != key)
                continue;
            toggle.Box.ButtonPressed = !toggle.Box.ButtonPressed; // raises Toggled
            return true;
        }
        return false;
    }

    /// <summary>Brings the check boxes and the weather list in line with the game, without raising events.</summary>
    public void Sync(bool hunting, int weatherIndex)
    {
        foreach (var toggle in _toggles)
            toggle.Box.SetPressedNoSignal(toggle.Get());
        _hunt.SetPressedNoSignal(hunting);
        if (weatherIndex >= 0)
            _weather.Select(weatherIndex);
    }

    public void ShowReadout(string bbcode) => _readout.Text = bbcode;

    private void Toggle(GridContainer grid, string text, Key hotkey, Func<bool> get, Action<bool> set)
    {
        var box = new CheckBox { Text = text, FocusMode = FocusModeEnum.None, ButtonPressed = get() };
        box.Toggled += on =>
        {
            set(on);
            OverlaysChanged?.Invoke();
        };
        grid.AddChild(box);
        _toggles.Add((box, hotkey, get, set));
    }
}
