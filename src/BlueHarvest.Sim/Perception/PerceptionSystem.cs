using BlueHarvest.Sim.Actors;
using BlueHarvest.Sim.Lighting;
using BlueHarvest.Sim.World;

namespace BlueHarvest.Sim.Perception;

/// <summary>Something seen. <see cref="Detected"/> means the score cleared the observer's threshold.</summary>
public sealed record SightContact(Actor Target, int Score, bool Moving, bool Detected);

/// <summary>A noise heard, with how loud it was on arrival.</summary>
public sealed record HeardNoise(NoiseEvent Noise, int Perceived);

/// <summary>A light source in view, scored by how bright it looks from here.</summary>
public sealed record SeenLight(LightSource Light, int Score);

/// <summary>Everything one actor perceives right now.</summary>
public sealed class Percepts
{
    public Percepts(Actor observer, IReadOnlyList<SightContact> seen, IReadOnlyList<HeardNoise> heard, IReadOnlyList<SeenLight> lights)
    {
        Observer = observer;
        Seen = seen;
        Heard = heard;
        Lights = lights;
    }

    public Actor Observer { get; }
    public IReadOnlyList<SightContact> Seen { get; }
    public IReadOnlyList<HeardNoise> Heard { get; }
    public IReadOnlyList<SeenLight> Lights { get; }

    public SightContact? ContactWith(Actor target) => Seen.FirstOrDefault(c => c.Target == target);

    public bool Detects(Actor target) => ContactWith(target) is { Detected: true };

    public bool Hears(Actor target) => Heard.Any(h => h.Noise.ActorId == target.Id);
}

/// <summary>
/// Sight, hearing and light perception. Stateless: it reads the simulation and returns what an actor
/// perceives. Memory of what was perceived (contacts that fade) comes with the AI.
/// </summary>
public static class PerceptionSystem
{
    /// <summary>A target that stepped within this many ticks counts as moving.</summary>
    public const int MotionWindowTicks = 100;

    /// <summary>
    /// Everything <paramref name="observer"/> perceives now. Noises count if made at or after
    /// <paramref name="sinceTick"/>; the observer's own noises are skipped.
    /// </summary>
    public static Percepts Perceive(Simulation sim, Actor observer, long sinceTick)
    {
        var senses = sim.SensesOf(observer);

        var seen = new List<SightContact>();
        foreach (var target in sim.Actors)
        {
            if (target == observer || target.IsDead)
                continue;
            bool moving = target.MovedRecently(sim.CurrentTick, MotionWindowTicks);
            int score = SightScore(sim, observer, target.Position, sim.StandHeight(target), target.Species.BodyHeightDm, moving);
            if (score > 0)
                seen.Add(new SightContact(target, score, moving, score >= senses.DetectThreshold));
        }

        var heard = new List<HeardNoise>();
        foreach (var noise in sim.RecentNoises)
        {
            if (noise.Tick < sinceTick || noise.ActorId == observer.Id)
                continue;
            int perceived = HearingLevel(sim, observer, noise.Position, noise.Loudness);
            if (perceived >= senses.HearingThreshold)
                heard.Add(new HeardNoise(noise, perceived));
        }

        var lights = new List<SeenLight>();
        foreach (var light in sim.Lights)
        {
            if (!light.On)
                continue;
            int score = LightScore(sim, observer, light);
            if (score > 0)
                lights.Add(new SeenLight(light, score));
        }

        return new Percepts(observer, seen, heard, lights);
    }

    /// <summary>
    /// How well <paramref name="observer"/> would see a target of the given height at a tile, 0-100,
    /// looking from its eyes (the head tip, for large bodies).
    /// It multiplies four factors: how much of the sight line gets through (visibility),
    /// how lit the target is (night vision sets a floor), how close it is, and whether it's moving.
    /// </summary>
    public static int SightScore(Simulation sim, Actor observer, TilePos target, int targetHeightDm, bool moving) =>
        SightScore(sim, observer, target, sim.Terrain[target], targetHeightDm, moving);

    /// <summary>
    /// As above, for a target standing at absolute height <paramref name="targetBaseDm"/> (on a deck,
    /// say) rather than on the ground.
    /// </summary>
    public static int SightScore(Simulation sim, Actor observer, TilePos target, int targetBaseDm, int targetHeightDm, bool moving)
    {
        var senses = sim.SensesOf(observer);
        var from = sim.EyeTile(observer);
        int dx = target.X - from.X, dy = target.Y - from.Y;
        long d2 = (long)dx * dx + (long)dy * dy;
        int range = senses.SightRangeTiles;
        if (d2 > (long)range * range)
            return 0;

        // Anything adjacent is noticed whatever the facing.
        if (d2 > 2 && !Angles.WithinCone(dx, dy, observer.Facing, senses.FovCosPermille))
            return 0;

        var map = sim.Map;
        int visibility = d2 == 0
            ? 100
            : SightLine.Trace(map, from, sim.EyeZ(observer),
                target, targetBaseDm + targetHeightDm, includeTargetCover: true).Visibility;
        if (visibility == 0)
            return 0;

        int light = Math.Max(sim.Light.LevelAt(target), senses.NightVisionPercent);
        int lightFactor = Math.Min(100, light * 2);                 // 50% light or more is as good as day
        int distanceFactor = DistanceFactor(d2, range);
        int motionFactor = moving ? 100 : 100 - senses.MotionBiasPercent;

        return visibility * lightFactor / 100 * distanceFactor / 100 * motionFactor / 100;
    }

    /// <summary>How loud a noise of <paramref name="loudness"/> at <paramref name="source"/> is where the listener stands.</summary>
    public static int HearingLevel(Simulation sim, Actor listener, TilePos source, int loudness)
    {
        var map = sim.Map;
        var at = sim.EyeTile(listener);
        long d2 = (long)(source.X - at.X) * (source.X - at.X) + (long)(source.Y - at.Y) * (source.Y - at.Y);
        int distance = (int)Math.Sqrt(d2);

        var trace = d2 == 0
            ? LineTrace.Clear
            : SightLine.Trace(map, at, sim.EyeZ(listener),
                source, map.Heights[source] + 10, includeTargetCover: false);

        return loudness - distance
            - trace.SolidTiles * 6
            - (trace.TerrainBlocked ? 10 : 0)
            - (trace.RoofBlocked ? 12 : 0);
    }

    /// <summary>How bright a light source looks to the observer, 0-100; 0 if out of view.</summary>
    public static int LightScore(Simulation sim, Actor observer, LightSource light)
    {
        var senses = sim.SensesOf(observer);
        var from = sim.EyeTile(observer);
        int dx = light.Position.X - from.X, dy = light.Position.Y - from.Y;
        long d2 = (long)dx * dx + (long)dy * dy;
        int range = senses.SightRangeTiles;
        if (d2 > (long)range * range)
            return 0;
        if (d2 > 2 && !Angles.WithinCone(dx, dy, observer.Facing, senses.FovCosPermille))
            return 0;

        var map = sim.Map;
        int visibility = d2 == 0
            ? 100
            : SightLine.Trace(map, from, sim.EyeZ(observer),
                light.Position, map.Heights[light.Position] + light.HeightDm, includeTargetCover: false).Visibility;

        return visibility * light.IntensityPercent / 100 * DistanceFactor(d2, range) / 100;
    }

    /// <summary>100 within 2 tiles, falling linearly to 0 at the edge of range.</summary>
    private static int DistanceFactor(long d2, int range)
    {
        int distance = (int)Math.Sqrt(d2);
        return distance <= 2 ? 100 : Math.Max(0, 100 * (range - distance) / range);
    }
}
