#nullable enable
using BlueHarvest.Game.Ui;
using Godot;

namespace BlueHarvest.Game.Island;

/// <summary>
/// The first screen: play the testbed, or open the island generator. Esc quits.
/// </summary>
public partial class FrontMenu : Control
{
    public const string MenuScene = "res://FrontMenu.tscn";
    private const string TestbedScene = "res://Main.tscn";
    private const string IslandScene = "res://IslandExplorer.tscn";

    public override void _Ready()
    {
        RenderingServer.SetDefaultClearColor(new Color(0.02f, 0.02f, 0.03f));
        var font = new SystemFont
        {
            FontNames = new[] { "Consolas", "Cascadia Mono", "DejaVu Sans Mono", "Courier New", "monospace" },
        };
        Theme = HudTheme.Build(font);

        var center = new CenterContainer { AnchorRight = 1f, AnchorBottom = 1f };
        AddChild(center);
        var panel = new PanelContainer { CustomMinimumSize = new Vector2(420, 0) };
        panel.AddThemeStyleboxOverride("panel", HudTheme.Box(HudTheme.Panel, HudTheme.Line, 4, 24));
        center.AddChild(panel);
        var column = HudTheme.Column(12);
        panel.AddChild(column);

        var title = new Label { Text = "THE FORGOTTEN LAND", HorizontalAlignment = HorizontalAlignment.Center };
        title.AddThemeFontSizeOverride("font_size", 28);
        title.AddThemeColorOverride("font_color", HudTheme.Amber);
        column.AddChild(title);
        var sub = HudTheme.Text("Blue Harvest build", HudTheme.SmallSize, HudTheme.Muted);
        sub.HorizontalAlignment = HorizontalAlignment.Center;
        column.AddChild(sub);
        column.AddChild(new Control { CustomMinimumSize = new Vector2(0, 8) });

        Button first = AddButton(column, "Play the testbed", "The T-Rex fence-breach map", () => GetTree().ChangeSceneToFile(TestbedScene));
        AddButton(column, "Island generator", "Generate an island and look around it", () => GetTree().ChangeSceneToFile(IslandScene));
        AddButton(column, "Quit", "", () => GetTree().Quit());
        first.GrabFocus();
    }

    private static Button AddButton(Container parent, string text, string hint, System.Action onPressed)
    {
        var button = new Button { Text = text, CustomMinimumSize = new Vector2(0, 40), TooltipText = hint };
        button.AddThemeFontSizeOverride("font_size", HudTheme.TextSize);
        button.Pressed += onPressed;
        parent.AddChild(button);
        return button;
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape })
            GetTree().Quit();
    }
}
