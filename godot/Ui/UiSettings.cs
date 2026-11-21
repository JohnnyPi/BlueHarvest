#nullable enable
using BlueHarvest.Game.Rendering;
using Godot;

namespace BlueHarvest.Game.Ui;

/// <summary>HUD preferences kept between runs in user://ui.cfg: the log's corner and the map's look and motion.</summary>
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

    /// <summary>The map's look (F3); Layered unless Classic or Expressive was chosen.</summary>
    public static LookMode LoadLook()
    {
        var config = new ConfigFile();
        if (config.Load(FilePath) != Error.Ok)
            return LookMode.Layered;
        return config.GetValue("map", "look", "layered").AsString() switch
        {
            "classic" => LookMode.Classic,
            "expressive" => LookMode.Expressive,
            _ => LookMode.Layered,
        };
    }

    public static void SaveLook(LookMode look)
    {
        var config = new ConfigFile();
        config.Load(FilePath); // a missing file just means a fresh config
        config.SetValue("map", "look", look switch { LookMode.Classic => "classic", LookMode.Expressive => "expressive", _ => "layered" });
        config.Save(FilePath);
    }

    /// <summary>How much the map moves (F4); full unless reduced or still was chosen.</summary>
    public static MotionLevel LoadMotion()
    {
        var config = new ConfigFile();
        if (config.Load(FilePath) != Error.Ok)
            return MotionLevel.Full;
        return config.GetValue("map", "motion", "full").AsString() switch
        {
            "reduced" => MotionLevel.Reduced,
            "still" => MotionLevel.Still,
            _ => MotionLevel.Full,
        };
    }

    public static void SaveMotion(MotionLevel motion)
    {
        var config = new ConfigFile();
        config.Load(FilePath); // a missing file just means a fresh config
        config.SetValue("map", "motion", motion switch { MotionLevel.Reduced => "reduced", MotionLevel.Still => "still", _ => "full" });
        config.Save(FilePath);
    }
}
