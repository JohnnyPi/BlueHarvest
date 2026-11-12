#nullable enable
using Godot;

namespace BlueHarvest.Game.Ui;

/// <summary>The controls, on F1. Centred over the map; F1 or Esc closes it.</summary>
public partial class HelpPanel : PanelContainer
{
    private const string Keys =
        "[color=#e9b04c]Move[/color]            arrows · numpad · hjkl yubn\n" +
        "[color=#e9b04c]Sprint[/color]          hold Shift while moving or clicking\n" +
        "[color=#e9b04c]Wait[/color]            .  numpad 5  Space\n" +
        "[color=#e9b04c]Wait for light[/color]  z  until dawn or dusk (any key or click stops)\n" +
        "[color=#e9b04c]Pan the map[/color]     W A S D   ·   Home recentres\n" +
        "[color=#e9b04c]Zoom[/color]            mouse wheel  ·  + −\n" +
        "\n" +
        "[color=#e9b04c]Left-click[/color]      walk there (stops if the Rex comes into view;\n" +
        "                any key or click stops it too)\n" +
        "[color=#e9b04c]Right-click[/color]     actions for that tile: walk, sprint, throw a flare,\n" +
        "                rope down, doors, ladders, first aid, orders\n" +
        "\n" +
        "[color=#e9b04c]Flare[/color]           f, then click a tile (or move the cursor, Enter)\n" +
        "[color=#e9b04c]Rope down[/color]       r, then a direction\n" +
        "[color=#e9b04c]First aid[/color]       t          [color=#e9b04c]Flashlight[/color]   x\n" +
        "[color=#e9b04c]Close a door[/color]    c          [color=#e9b04c]Ladder[/color]       < >\n" +
        "[color=#e9b04c]Order[/color]           o, then f follow · s stay · h hide\n" +
        "\n" +
        "[color=#e9b04c]F1[/color] this help   [color=#e9b04c]F9[/color] debug panel   [color=#e9b04c]F10[/color] main menu   [color=#e9b04c]Esc[/color] cancel";

    public HelpPanel()
    {
        AnchorLeft = 0.5f;
        AnchorRight = 0.5f;
        AnchorTop = 0.5f;
        AnchorBottom = 0.5f;
        OffsetLeft = -300f - Sidebar.PanelWidth / 2f;
        OffsetRight = 300f - Sidebar.PanelWidth / 2f;
        OffsetTop = -220f;
        OffsetBottom = 220f;
        MouseFilter = MouseFilterEnum.Stop;
        Visible = false;
        AddThemeStyleboxOverride("panel", HudTheme.Box(HudTheme.Panel, HudTheme.Line, 6, 16));

        var column = HudTheme.Column(10);
        AddChild(column);
        column.AddChild(HudTheme.Heading("Controls"));
        var text = HudTheme.Rich(HudTheme.TextSize);
        text.AutowrapMode = TextServer.AutowrapMode.Off;
        text.Text = Keys;
        column.AddChild(text);
    }
}
