using BlueHarvest.Sim.Core;

namespace BlueHarvest.Sim.World.Generation;

/// <summary>The tides, swell and river gauges of a generated island, for one run.</summary>
public static class IslandWaters
{
    /// <summary>The ocean swell comes in from the north-north-west (330°), as the coast was cut: the nearest of the eight directions.</summary>
    public const Direction SwellFrom = Direction.NorthWest;

    /// <summary>
    /// A gauge for every river: its rain is measured at its head and a third and two thirds of the
    /// way down, and a river rises more the bigger it is (0.8 m plus 0.7 m per root of its discharge,
    /// 0.8 to 4 m) when its catchment has had as much rain as it gets.
    /// </summary>
    public static WaterLevels For(IslandWorld world, ulong runSeed)
    {
        IslandShape s = world.Shape;
        var gauges = new List<RiverGauge>();
        for (int r = 0; r < s.Rivers.Count; r++)
        {
            River river = s.Rivers[r];
            int n = river.Xs.Length;
            if (n == 0)
                continue;
            var points = new List<(long X, long Y)>();
            foreach (int at in new[] { 0, n / 3, 2 * n / 3 })
            {
                (long X, long Y) pt = ((long)Math.Round(river.Xs[at]), (long)Math.Round(river.Ys[at]));
                if (!points.Contains(pt))
                    points.Add(pt);
            }
            double q = river.Qs[n / 2];
            int rise = (int)Math.Clamp(Math.Round(8 + 7 * Math.Sqrt(Math.Max(0, q))), 8, 40);
            gauges.Add(new RiverGauge(r, rise, points));
        }
        ulong seed = new StateHasher().Add((ulong)world.Params.Seed).Add("water").Add(runSeed).Value;
        return new WaterLevels(seed, SwellFrom, gauges);
    }
}
