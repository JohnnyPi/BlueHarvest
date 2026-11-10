#nullable enable
using BlueHarvest.Sim.Injuries;
using Godot;

namespace BlueHarvest.Game.Ui;

/// <summary>
/// The HUD's look in one place: a dark field-station palette, the shared Theme and a few small
/// builders so every panel uses the same headings, text sizes and boxes.
/// </summary>
public static class HudTheme
{
    public static readonly Color Panel = new(0.071f, 0.086f, 0.078f, 0.97f);
    public static readonly Color PanelSoft = new(0.035f, 0.043f, 0.039f, 0.93f);
    public static readonly Color Raised = new(0.106f, 0.129f, 0.114f);
    public static readonly Color Line = new(0.173f, 0.208f, 0.184f);
    public static readonly Color Ink = new(0.882f, 0.898f, 0.847f);
    public static readonly Color Muted = new(0.627f, 0.663f, 0.608f);
    public static readonly Color Amber = new(0.914f, 0.690f, 0.298f);
    public static readonly Color Danger = new(1f, 0.416f, 0.333f);
    public static readonly Color Safe = new(0.455f, 0.855f, 0.565f);
    public static readonly Color Caution = new(0.941f, 0.784f, 0.251f);
    public static readonly Color Earth = new(0.753f, 0.541f, 0.333f);

    public const int TextSize = 15;
    public const int SmallSize = 13;
    public const int HeadingSize = 12;

    /// <summary>"#rrggbb" for BBCode color tags.</summary>
    public static string Hex(Color c) => "#" + c.ToHtml(false);

    public static Color SeverityColor(Severity severity) => severity switch
    {
        Severity.Red => Danger,
        Severity.Yellow => Caution,
        _ => Safe,
    };

    /// <summary>Escapes text for a BBCode label so a '[' in a name or message can't start a tag.</summary>
    public static string Escape(string text) => text.Replace("[", "[lb]");

    public static Theme Build(Font font)
    {
        var theme = new Theme { DefaultFont = font, DefaultFontSize = TextSize };
        theme.SetStylebox("panel", "PanelContainer", Box(Panel, Line, 0, 0));
        theme.SetColor("font_color", "Label", Ink);
        theme.SetColor("default_color", "RichTextLabel", Ink);
        theme.SetFontSize("normal_font_size", "RichTextLabel", TextSize);
        theme.SetFontSize("bold_font_size", "RichTextLabel", TextSize);

        foreach (string type in new[] { "Button", "CheckBox", "OptionButton" })
        {
            theme.SetColor("font_color", type, Ink);
            theme.SetColor("font_hover_color", type, Ink);
            theme.SetColor("font_pressed_color", type, Ink);
            theme.SetColor("font_hover_pressed_color", type, Ink);
            theme.SetFontSize("font_size", type, SmallSize);
        }
        theme.SetStylebox("normal", "Button", Box(Raised, Line, 4, 6));
        theme.SetStylebox("hover", "Button", Box(Raised, Muted, 4, 6));
        theme.SetStylebox("pressed", "Button", Box(Raised, Amber, 4, 6));
        theme.SetStylebox("normal", "OptionButton", Box(Raised, Line, 4, 6));
        theme.SetStylebox("hover", "OptionButton", Box(Raised, Muted, 4, 6));
        theme.SetStylebox("pressed", "OptionButton", Box(Raised, Amber, 4, 6));

        theme.SetStylebox("panel", "PopupMenu", Box(Panel, Line, 4, 6));
        theme.SetColor("font_color", "PopupMenu", Ink);
        theme.SetColor("font_hover_color", "PopupMenu", Ink);
        theme.SetColor("font_disabled_color", "PopupMenu", Muted);
        theme.SetColor("font_separator_color", "PopupMenu", Muted);
        theme.SetStylebox("hover", "PopupMenu", Box(Raised, Amber, 3, 4));
        theme.SetFontSize("font_size", "PopupMenu", TextSize);
        theme.SetFontSize("font_separator_size", "PopupMenu", SmallSize);
        return theme;
    }

    /// <summary>A flat box: background, 1 px border, corner radius and inner margin.</summary>
    public static StyleBoxFlat Box(Color background, Color border, int radius, float margin)
    {
        var box = new StyleBoxFlat { BgColor = background, BorderColor = border };
        box.SetBorderWidthAll(1);
        box.SetCornerRadiusAll(radius);
        box.SetContentMarginAll(margin);
        return box;
    }

    /// <summary>A small upper-case section heading in the muted color.</summary>
    public static Label Heading(string text)
    {
        var label = new Label { Text = text, Uppercase = true };
        label.AddThemeFontSizeOverride("font_size", HeadingSize);
        label.AddThemeColorOverride("font_color", Muted);
        return label;
    }

    /// <summary>A wrapping label.</summary>
    public static Label Text(string text = "", int size = TextSize, Color? color = null)
    {
        var label = new Label { Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        label.AddThemeFontSizeOverride("font_size", size);
        if (color is { } c)
            label.AddThemeColorOverride("font_color", c);
        return label;
    }

    /// <summary>A BBCode label that grows to fit its text and never scrolls.</summary>
    public static RichTextLabel Rich(int size = TextSize)
    {
        var label = new RichTextLabel
        {
            BbcodeEnabled = true,
            FitContent = true,
            ScrollActive = false,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        label.AddThemeFontSizeOverride("normal_font_size", size);
        label.AddThemeFontSizeOverride("bold_font_size", size);
        return label;
    }

    /// <summary>A vertical stack with even spacing.</summary>
    public static VBoxContainer Column(int gap = 6)
    {
        var column = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        column.AddThemeConstantOverride("separation", gap);
        return column;
    }

    /// <summary>A button that never takes keyboard focus, so arrow keys keep moving the player.</summary>
    public static Button PlainButton(string text)
    {
        var button = new Button { Text = text, FocusMode = Control.FocusModeEnum.None };
        return button;
    }
}
