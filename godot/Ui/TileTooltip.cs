#nullable enable
using Godot;

namespace BlueHarvest.Game.Ui;

/// <summary>
/// The card that follows the mouse over the map: what the tile is, its height against yours, its
/// light, and anything that matters for getting away. It never catches clicks.
/// </summary>
public partial class TileTooltip : PanelContainer
{
    private const float Width = 270f;
    private readonly RichTextLabel _text;

    public TileTooltip()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        Visible = false;
        AddThemeStyleboxOverride("panel", HudTheme.Box(new Color(0.055f, 0.071f, 0.063f, 0.96f), HudTheme.Line, 4, 8));
        _text = HudTheme.Rich(HudTheme.SmallSize + 1);
        _text.CustomMinimumSize = new Vector2(Width - 16f, 0);
        AddChild(_text);
    }

    /// <summary>Shows the card near the mouse, kept inside <paramref name="bounds"/> (the map's part of the screen).</summary>
    public void ShowAt(Vector2 mouse, string bbcode, Rect2 bounds)
    {
        if (_text.Text != bbcode)
        {
            _text.Text = bbcode;
            ResetSize();
        }
        var size = Size;
        float x = Mathf.Min(mouse.X + 18f, bounds.End.X - size.X - 6f);
        float y = Mathf.Min(mouse.Y + 18f, bounds.End.Y - size.Y - 6f);
        Position = new Vector2(Mathf.Max(bounds.Position.X + 6f, x), Mathf.Max(bounds.Position.Y + 6f, y));
        Visible = true;
    }
}
