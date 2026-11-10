#nullable enable
using Godot;

namespace BlueHarvest.Game.Ui;

/// <summary>HUD preferences kept between runs in user://ui.cfg (the log's corner, for now).</summary>
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
}
