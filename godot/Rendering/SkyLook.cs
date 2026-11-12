#nullable enable
using BlueHarvest.Sim.World;
using Godot;
using Sky = BlueHarvest.Sim.World.Sky; // Godot has a Sky of its own

namespace BlueHarvest.Game.Rendering;

/// <summary>
/// The colour of the light by time of day: blue at night, rose at sunrise, gold in the low sun, plain
/// white through the day. Looks only: how bright it is comes from the simulation's light grid.
/// </summary>
public static class SkyLook
{
    private static readonly Color Night = new(0.72f, 0.82f, 1.18f);
    private static readonly Color Rose = new(1.14f, 0.84f, 0.78f);
    private static readonly Color Gold = new(1.10f, 0.96f, 0.82f);
    private static readonly Color White = new(1f, 1f, 1f);

    /// <summary>Minute of the day and the tint there; between two entries the tint is blended.</summary>
    private static readonly (int Minute, Color Tint)[] Keys =
    {
        (0, Night),
        (Sky.FirstLight, Night),
        (Sky.Sunrise, Rose),
        (Sky.Sunrise + 60, Gold),
        (Sky.FullDay + 30, White),
        (Sky.DuskStart - 30, White),
        (Sky.Sunset - 30, Gold),
        (Sky.Sunset, Rose),
        (Sky.FullDark, Night),
        (WorldClock.MinutesPerDay, Night),
    };

    /// <summary>What to multiply lit colours by at this minute of the day.</summary>
    public static Color Tint(int minuteOfDay)
    {
        for (int i = 1; i < Keys.Length; i++)
        {
            if (minuteOfDay > Keys[i].Minute)
                continue;
            var (m0, c0) = Keys[i - 1];
            var (m1, c1) = Keys[i];
            float t = m1 == m0 ? 0f : (minuteOfDay - m0) / (float)(m1 - m0);
            return c0.Lerp(c1, Mathf.Clamp(t, 0f, 1f));
        }
        return Night;
    }
}
