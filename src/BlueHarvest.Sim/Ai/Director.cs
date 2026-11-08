using BlueHarvest.Sim.Actors;
using BlueHarvest.Sim.World;

namespace BlueHarvest.Sim.Ai;

/// <summary>
/// Stub of the island director. It estimates tension around the player and opens calm windows after
/// a peak; later it will pull levers (weather, herds, patrols) to steer tension toward a target curve.
/// Rule: it reads creatures' minds to judge pacing, but never writes anything into them, so no
/// creature ever knows more than its senses told it.
/// Not part of the simulation state yet: the front end and runner update it after each turn.
/// </summary>
public sealed class Director
{
    /// <summary>Tension at or above this counts as a peak.</summary>
    public const int PeakTension = 80;

    /// <summary>Once tension falls below this after a peak, a calm window opens.</summary>
    public const int CalmBelow = 20;

    /// <summary>How long a calm window lasts: 60 s of game time.</summary>
    public const int CalmWindowTicks = 6000;

    private bool _peaked;

    /// <summary>0-100: how much danger the player is in right now, as the director judges it.</summary>
    public int Tension { get; private set; }

    /// <summary>Highest tension seen so far.</summary>
    public int PeakSoFar { get; private set; }

    /// <summary>The tick a calm window ends; levers will hold back new pressure until then.</summary>
    public long CalmUntilTick { get; private set; } = long.MinValue;

    public bool InCalmWindow(long now) => now < CalmUntilTick;

    /// <summary>
    /// Re-estimates tension: for each creature with a brain, nearness (up to 40) plus how fixed it is
    /// on the player (60 hunting or biting, 30 investigating). The worst creature sets the tension.
    /// </summary>
    public void Update(Simulation sim, Actor player)
    {
        int tension = 0;
        var playerKey = ContactKey.ForActor(player.Id);
        foreach (var actor in sim.Actors)
        {
            if (actor.Controller is not CreatureBrain brain)
                continue;

            int distance = Chebyshev(actor.Position, player.Position);
            int t = Math.Max(0, 40 - distance);
            if (brain.Focus is { } focus && focus.Key == playerKey)
                t += brain.Activity is Activity.Hunting or Activity.Biting or Activity.Smashing ? 60 : 30;
            tension = Math.Max(tension, Math.Min(100, t));
        }

        Tension = tension;
        PeakSoFar = Math.Max(PeakSoFar, tension);
        if (tension >= PeakTension)
        {
            _peaked = true;
        }
        else if (_peaked && tension < CalmBelow)
        {
            _peaked = false;
            CalmUntilTick = sim.CurrentTick + CalmWindowTicks;
        }
    }

    private static int Chebyshev(TilePos a, TilePos b) => Math.Max(Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));
}
