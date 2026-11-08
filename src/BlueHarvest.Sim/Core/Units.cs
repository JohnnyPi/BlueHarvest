namespace BlueHarvest.Sim.Core;

public static class Units
{
    /// <summary>Formats decimeters as meters, e.g. 25 → "2.5 m".</summary>
    public static string FormatMeters(int decimeters)
    {
        string sign = decimeters < 0 ? "-" : "";
        int abs = Math.Abs(decimeters);
        return $"{sign}{abs / 10}.{abs % 10} m";
    }
}
