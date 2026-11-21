#nullable enable
using BlueHarvest.Game.Rendering;
using Godot;

namespace BlueHarvest.Game.Ui;

/// <summary>
/// The game's one font: DejaVu Sans Mono 2.37, bundled in godot/Fonts with its licence, so the map
/// and the HUD look the same on every machine. The system's monospace fonts stay behind it as a
/// fallback, for any character it lacks and in case the file is missing.
/// </summary>
public static class GameFont
{
    public const string FilePath = "res://Fonts/DejaVuSansMono.ttf";

    private static Font? _font;

    /// <summary>The font, loaded and checked once (the glyph check prints to the Godot output).</summary>
    public static Font Load()
    {
        if (_font is not null)
            return _font;

        var system = new SystemFont
        {
            FontNames = new[] { "Consolas", "Cascadia Mono", "DejaVu Sans Mono", "Courier New", "monospace" },
        };
        if (ResourceLoader.Exists(FilePath) && ResourceLoader.Load(FilePath) is FontFile bundled)
        {
            GlyphSet.Audit(bundled); // before the fallback goes on, so it can't hide a missing glyph
            bundled.Fallbacks = new Godot.Collections.Array<Font> { system };
            _font = bundled;
        }
        else
        {
            GD.PushWarning($"{FilePath} is missing or not imported (open the project in the Godot editor once); using the system's fonts, so some map glyphs may not show.");
            GlyphSet.Audit(system);
            _font = system;
        }
        return _font;
    }
}
