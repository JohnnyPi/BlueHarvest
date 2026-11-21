#nullable enable
using BlueHarvest.Game.Rendering;
using Godot;

namespace BlueHarvest.Game.Ui;

/// <summary>HUD preferences kept between runs in user://ui.cfg: the log's corner and the map's look.</summary>
public static class UiSettings
{
    private const string FilePath = "user://ui.cfg";

    public static LogCorner LoadLogCorner()
    {
        var config = new ConfigFile();
        if (config.Load(FilePath) != Error.Ok)
            return LogCorner.LowerLeft;
        return config.GetValue("hud", "log_corner", "lower").AsString() == "upper"
            ? LogCorner.UpperLeft
            : LogCorner.LowerLeft;
    }

    public static void SaveLogCorner(LogCorner corner)
    {
        var config = new ConfigFile();
        config.Load(FilePath); // a missing file just means a fresh config
        config.SetValue("hud", "log_corner", corner == LogCorner.UpperLeft ? "upper" : "lower");
        config.Save(FilePath);
    }

    /// <summary>The map's look (F3); Layered unless Classic was chosen.</summary>
    public static LookMode LoadLook()
    {
        var config = new ConfigFile();
        if (config.Load(FilePath) != Error.Ok)
            return LookMode.Layered;
        return config.GetValue("map", "look", "layered").AsString() == "classic" ? LookMode.Classic : LookMode.Layered;
    }

    public static void SaveLook(LookMode look)
    {
        var config = new ConfigFile();
        config.Load(FilePath); // a missing file just means a fresh config
        config.SetValue("map", "look", look == LookMode.Classic ? "classic" : "layered");
        config.Save(FilePath);
    }
}
