#nullable enable
using System;
using System.Collections.Generic;
using Godot;

namespace BlueHarvest.Game.Ui;

/// <summary>One line of the inventory panel: a section heading (no item), or an item, indented as deep as it sits in bags.</summary>
public readonly record struct InventoryRow(string Text, int? ItemId = null, int Indent = 0, Color? Color = null);

/// <summary>
/// What you carry, on I: hands, pockets, what you wear and what's in it, then the weight. Clicking an
/// item asks for its actions (Main opens them in the action menu beside it). I or Esc closes it.
/// </summary>
public partial class InventoryPanel : PanelContainer
{
    private readonly VBoxContainer _rows;
    private readonly RichTextLabel _footer;

    /// <summary>An item's line was clicked: its id, and where on screen to open its actions.</summary>
    public event Action<int, Vector2>? ItemClicked;

    public InventoryPanel()
    {
        AnchorLeft = 0f;
        AnchorRight = 0f;
        AnchorTop = 0.5f;
        AnchorBottom = 0.5f;
        OffsetLeft = 16f;
        OffsetRight = 16f + 420f;
        OffsetTop = -260f;
        OffsetBottom = 260f;
        MouseFilter = MouseFilterEnum.Stop;
        Visible = false;
        AddThemeStyleboxOverride("panel", HudTheme.Box(HudTheme.Panel, HudTheme.Line, 6, 12));

        var column = HudTheme.Column(6);
        AddChild(column);
        column.AddChild(HudTheme.Heading("Inventory"));
        column.AddChild(HudTheme.Text("Click an item for what you can do with it.  I or Esc closes.", HudTheme.SmallSize, HudTheme.Muted));

        var scroll = new ScrollContainer
        {
            SizeFlagsVertical = SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        column.AddChild(scroll);
        _rows = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _rows.AddThemeConstantOverride("separation", 1);
        scroll.AddChild(_rows);

        _footer = HudTheme.Rich(HudTheme.SmallSize);
        column.AddChild(_footer);
    }

    /// <summary>Shows these lines (rebuilt each time) and the footer (BBCode).</summary>
    public void Display(IReadOnlyList<InventoryRow> rows, string footer)
    {
        foreach (Node child in _rows.GetChildren())
        {
            _rows.RemoveChild(child);
            child.QueueFree();
        }
        foreach (var row in rows)
        {
            if (row.ItemId is not { } id)
            {
                var heading = HudTheme.Text(row.Text, HudTheme.SmallSize, row.Color ?? HudTheme.Muted);
                _rows.AddChild(heading);
                continue;
            }
            var button = new Button
            {
                Text = new string(' ', 3 * row.Indent) + row.Text,
                Flat = true,
                FocusMode = FocusModeEnum.None,
                Alignment = HorizontalAlignment.Left,
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                ClipText = true,
            };
            if (row.Color is { } color)
                button.AddThemeColorOverride("font_color", color);
            button.Pressed += () => ItemClicked?.Invoke(id, button.GetGlobalRect().Position + new Vector2(button.Size.X * 0.6f, 0f));
            _rows.AddChild(button);
        }
        _footer.Text = footer;
    }
}
