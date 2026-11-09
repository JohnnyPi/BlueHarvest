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

/// <summary>
/// One actor's trail, as far as the nose can follow it: the freshest of its marks that can be smelled,
/// where and when it was laid, and how strong it is here. <see cref="Blood"/> if any of the marks of
/// it that can be smelled are blood.
/// </summary>
public sealed record SmelledTrail(int ActorId, TilePos At, long LaidTick, int Perceived, bool Blood);

/// <summary>A shake of the ground felt underfoot, with how strong it was on arrival.</summary>
public sealed record FeltVibration(VibrationEvent Vibration, int Perceived);

/// <summary>Everything one actor perceives right now.</summary>
public sealed class Percepts
{
    public Percepts(Actor observer, IReadOnlyList<SightContact> seen, IReadOnlyList<HeardNoise> heard, IReadOnlyList<SeenLight> lights,
        IReadOnlyList<SmelledTrail>? smelled = null, IReadOnlyList<FeltVibration>? felt = null)
    {
        Observer = observer;
        Seen = seen;
        Heard = heard;
        Lights = lights;
        Smelled = smelled ?? Array.Empty<SmelledTrail>();
        Felt = felt ?? Array.Empty<FeltVibration>();
    }

    public Actor Observer { get; }
    public IReadOnlyList<SightContact> Seen { get; }
    public IReadOnlyList<HeardNoise> Heard { get; }
    public IReadOnlyList<SeenLight> Lights { get; }

    /// <summary>Trails it can smell, one per actor, in actor order. Empty for anything without a nose for it.</summary>
    public IReadOnlyList<SmelledTrail> Smelled { get; }

    /// <summary>Ground vibrations felt since the observer's last look, oldest first.</summary>
    public IReadOnlyList<FeltVibration> Felt { get; }

    public SightContact? ContactWith(Actor target) => Seen.FirstOrDefault(c => c.Target == target);

    public bool Detects(Actor target) => ContactWith(target) is { Detected: true };

    public bool Hears(Actor target) => Heard.Any(h => h.Noise.ActorId == target.Id);

    public bool Smells(Actor target) => Smelled.Any(s => s.ActorId == target.Id);

    public bool Feels(Actor target) => Felt.Any(f => f.Vibration.ActorId == target.Id);
}

/// <summary>
/// Sight, hearing, light, scent and ground vibration. Stateless: it reads the simulation and returns
/// what an actor perceives. Memory of what was perceived (contacts that fade) comes with the AI.
/// Weather acts here: rain and fog shorten sight (through SensesOf), and rain, wind and thunder mask
/// sounds; rain and wind act on scent; rain on soft ground damps vibration where it starts.
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
            if (target == observer || target.IsGone)
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
            int perceived = HearingLevel(sim, observer, noise.Position, noise.Loudness, noise.Tick);
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

        var smelled = senses.ScentRangeTiles > 0 ? Smell(sim, observer, senses) : Array.Empty<SmelledTrail>();

        var felt = new List<FeltVibration>();
        if (senses.VibrationThreshold != int.MaxValue)
        {
            foreach (var vibration in sim.RecentVibrations)
            {
                if (vibration.Tick < sinceTick || vibration.ActorId == observer.Id)
                    continue;
                int perceived = VibrationLevel(observer, vibration.Position, vibration.Strength);
                if (perceived >= senses.VibrationThreshold)
                    felt.Add(new FeltVibration(vibration, perceived));
            }
        }

        return new Percepts(observer, seen, heard, lights, smelled, felt);
    }

    /// <summary>
    /// Every actor's trail the observer can smell: for each, the freshest mark strong enough to smell
    /// from here, and whether any of what it smells of them is blood. Walks the marks newest first and
    /// stops checking an actor's older body scent once it has their freshest mark.
    /// </summary>
    private static IReadOnlyList<SmelledTrail> Smell(Simulation sim, Actor observer, SenseProfile senses)
    {
        var marks = sim.ScentMarks;
        if (marks.Count == 0)
            return Array.Empty<SmelledTrail>();

        long wash = sim.Weather.Wash(sim.CurrentTick);
        var best = new Dictionary<int, SmelledTrail>();
        var bloodFound = new HashSet<int>();
        for (int i = marks.Count - 1; i >= 0; i--)
        {
            var mark = marks[i];
            if (mark.ActorId == observer.Id)
                continue;
            bool have = best.TryGetValue(mark.ActorId, out var trail);
            if (have && (!mark.Blood || bloodFound.Contains(mark.ActorId)))
                continue;
            int strength = mark.StrengthAt(wash);
            if (strength < senses.ScentThreshold)
                continue;
            int perceived = ScentLevel(sim, observer, senses, mark.Position, strength);
            if (perceived < senses.ScentThreshold)
                continue;

            if (mark.Blood)
                bloodFound.Add(mark.ActorId);
            best[mark.ActorId] = have
                ? trail! with { Blood = true }
                : new SmelledTrail(mark.ActorId, mark.Position, mark.Tick, perceived, mark.Blood);
        }
        return best.Values.OrderBy(t => t.ActorId).ToList();
    }

    /// <summary>
    /// How strong scent of <paramref name="strengthNow"/> lying at <paramref name="source"/> is at the
    /// observer's nose (its head, for large bodies). Still air takes 3 per tile. Wind carries it: up
    /// to 2 less per tile straight downwind of the source and 2 more upwind, at 10 m/s or more. Rain
    /// takes some off everywhere; walls and roofs between take more. Out of scent range it's 0.
    /// </summary>
    public static int ScentLevel(Simulation sim, Actor observer, TilePos source, int strengthNow) =>
        ScentLevel(sim, observer, sim.SensesOf(observer), source, strengthNow);

    private static int ScentLevel(Simulation sim, Actor observer, SenseProfile senses, TilePos source, int strengthNow)
    {
        var nose = sim.EyeTile(observer);
        int dx = nose.X - source.X, dy = nose.Y - source.Y; // from the scent to the nose
        long d2 = (long)dx * dx + (long)dy * dy;
        int range = senses.ScentRangeTiles;
        if (range <= 0 || d2 > (long)range * range)
            return 0;

        var weather = sim.Weather;
        int distance = (int)Math.Sqrt(d2);
        int costPer10 = ScentRules.CalmCostPer10Tiles;
        if (distance > 0)
        {
            var (wx, wy) = weather.Downwind;
            // -1000 straight upwind .. 1000 straight downwind (clamped: the distance is rounded down).
            int along = Math.Clamp((dx * wx + dy * wy) / distance, -1000, 1000);
            int windEffect = Math.Min(ScentRules.MaxWindEffectPer10Tiles,
                weather.Settings.WindSpeed * ScentRules.WindEffectPerMetrePerSecond);
            costPer10 -= windEffect * along / 1000;
        }
        int level = strengthNow - distance * costPer10 / 10
            - weather.Settings.RainPercent * ScentRules.RainPenaltyAtFull / 100;
        if (level < senses.ScentThreshold || distance == 0)
            return level;

        var trace = SightLine.Trace(sim.Map, nose, sim.EyeZ(observer),
            source, sim.Map.Heights[source] + ScentRules.ScentHeightDm, includeTargetCover: false);
        return level - trace.SolidTiles * ScentRules.SolidTilePenalty - (trace.RoofBlocked ? ScentRules.RoofPenalty : 0);
    }

    /// <summary>
    /// How strong a shake of <paramref name="strength"/> at <paramref name="source"/> is where the
    /// observer stands: 2 less per tile, through walls and all (the ground carries it, not the air).
    /// </summary>
    public static int VibrationLevel(Actor observer, TilePos source, int strength)
    {
        var at = observer.Position;
        long d2 = (long)(source.X - at.X) * (source.X - at.X) + (long)(source.Y - at.Y) * (source.Y - at.Y);
        return strength - (int)Math.Sqrt(d2) * ScentRules.VibrationFalloffPerTile;
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

    /// <summary>
    /// How loud a noise of <paramref name="loudness"/> at <paramref name="source"/> is where the listener
    /// stands, after the weather's masking at <paramref name="atTick"/> (the current tick if left out).
    /// </summary>
    public static int HearingLevel(Simulation sim, Actor listener, TilePos source, int loudness, long? atTick = null)
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
            - (trace.RoofBlocked ? 12 : 0)
            - sim.Weather.HearingMask(atTick ?? sim.CurrentTick);
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
