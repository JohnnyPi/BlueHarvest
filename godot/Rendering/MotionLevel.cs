#nullable enable
namespace BlueHarvest.Game.Rendering;

/// <summary>
/// How much the map moves (the design guide's reduced-motion setting). Saved in user://ui.cfg; F4
/// cycles. It changes only what is drawn: nothing in the simulation reads it. Append only.
/// </summary>
public enum MotionLevel : byte
{
    /// <summary>Everything moves as designed.</summary>
    Full,

    /// <summary>
    /// Half speed, and nothing that moves only for atmosphere (gusts, blown leaves, reef crests, swash,
    /// spray, beacon pulses). Nothing flashes: a lamp on a failing battery is shown steadily dimmed.
    /// Water, fire, smoke and live fences still move, slowly, because they tell you something.
    /// </summary>
    Reduced,

    /// <summary>Nothing moves: the map is a still picture (as the debug panel's animation switch off).</summary>
    Still,
}
