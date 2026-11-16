using BlueHarvest.Sim.Actors;
using BlueHarvest.Sim.World;

namespace BlueHarvest.Sim.Perception;

/// <summary>The four senses the player's sidebar reports.</summary>
public enum Sense : byte { Sight, Sound, Smell, Feel }

/// <summary>What a cue is of. Append only.</summary>
public enum CueKind : byte
{
    // sight
    Creature,
    Person,
    Corpse,
    Fire,
    Smoke,
    Flare,
    // sound
    Noise,
    Water,
    Surf,
    Rain,
    Thunder,
    // smell
    Trail,
    Blood,
    Fuel,
    Burning,
    Death,
    // feel
    Tremor,
    Heat,
    Current,
    Wading,
    Wind,
}

/// <summary>
/// One thing the player senses, placed around them: where it is (<see cref="At"/>, when they can tell)
/// and which way (<see cref="Toward"/>: from the player to it, or for wind and current, which way it
/// pushes), how far (tiles) and how strong (0–100, in the sense's own terms). The front end words it;
/// nothing here is text but a noise's kind.
/// </summary>
public sealed record SenseCue(
    Sense Sense,
    CueKind Kind,
    TilePos? At,
    Direction? Toward,
    int Distance,
    int Strength,
    int? ActorId = null,
    string? NoiseKind = null,
    bool Danger = false);

/// <summary>How exposed the player feels: a feeling, not a measurement.</summary>
public enum ExposureStage : byte
{
    /// <summary>Nothing has any sense of you that you could feel.</summary>
    None,
    /// <summary>"You feel like something is watching you": something is close to making you out, has heard you, or has your scent.</summary>
    Watched,
    /// <summary>"Something has spotted you": a creature has you, and you can't see it.</summary>
    Spotted,
    /// <summary>"[It] sees you": a creature has you, and you can see it, so you know what it is.</summary>
    Seen,
}

/// <summary>
/// The player's exposure, worked out fresh each turn. <see cref="WatcherId"/> is the creature it's about
/// (the one most nearly seeing the player, or one that sees them and that they can see); the scores
/// are for the debug readout only.
/// </summary>
public sealed record Exposure(ExposureStage Stage, int? WatcherId, int Score, int Threshold, bool Heard, bool Smelled)
{
    public static readonly Exposure Nothing = new(ExposureStage.None, null, 0, 0, false, false);
}

/// <summary>
/// What the player senses around them, sense by sense, strongest first, and how exposed they are.
/// Built from the player's own percepts and the world around them, so it says only what they could
/// sense; it reads the creatures' senses only for exposure, the feeling of being watched. Brains never
/// read it. A pure function of the simulation's state.
/// </summary>
public sealed class SenseReport
{
    /// <summary>The most cues kept per sense.</summary>
    public const int MaxCues = 6;

    /// <summary>The player's nose (display only: people's brains don't smell). A creature's body scent (40) carries 4–5 tiles in still air, blood (100) about 25.</summary>
    public static readonly SenseProfile PlayerNose = SenseProfile.Human with { ScentRangeTiles = 25, ScentThreshold = 25 };

    /// <summary>
    /// How far each sense reports, in tiles: cues are placed around the player within a reach that
    /// makes sense for the sense. Sight goes as far as the player's own sight range (shortened by rain
    /// and fog); sound carries farthest; smell is the nose's reach; feel is the ground's tremor reach,
    /// and heat only close by.
    /// </summary>
    public const int SoundReachTiles = 80, SmellReachTiles = 25, FeelReachTiles = 30;

    /// <summary>Creatures farther than this aren't weighed for exposure.</summary>
    public const int WatcherRangeTiles = 60;

    /// <summary>A creature whose sight score is at least this percentage of its threshold makes you feel watched.</summary>
    public const int WatchedPercent = 60;

    /// <summary>Fires within this many tiles are felt as heat.</summary>
    public const int HeatRangeTiles = 6;

    /// <summary>Spilled fuel within this many tiles can be smelled.</summary>
    public const int FuelSmellTiles = 4;

    /// <summary>A body is smelled within this many tiles, once it has lain this long.</summary>
    public const int DeathSmellTiles = 8, DeathSmellAfterTicks = 2 * 60 * 100;

    private SenseReport(IReadOnlyList<SenseCue> sight, IReadOnlyList<SenseCue> sound, IReadOnlyList<SenseCue> smell,
        IReadOnlyList<SenseCue> feel, Exposure exposure)
    {
        Sight = sight;
        Sound = sound;
        Smell = smell;
        Feel = feel;
        Exposure = exposure;
    }

    public IReadOnlyList<SenseCue> Sight { get; }
    public IReadOnlyList<SenseCue> Sound { get; }
    public IReadOnlyList<SenseCue> Smell { get; }
    public IReadOnlyList<SenseCue> Feel { get; }
    public Exposure Exposure { get; }

    public IReadOnlyList<SenseCue> Of(Sense sense) => sense switch
    {
        Sense.Sight => Sight,
        Sense.Sound => Sound,
        Sense.Smell => Smell,
        _ => Feel,
    };

    /// <summary>All cues, every sense.</summary>
    public IEnumerable<SenseCue> All => Sight.Concat(Sound).Concat(Smell).Concat(Feel);

    /// <summary>
    /// What <paramref name="player"/> senses now. <paramref name="percepts"/> are theirs (from
    /// PerceptionSystem.Perceive, since <paramref name="sinceTick"/>, as the front end already makes).
    /// </summary>
    public static SenseReport For(Simulation sim, Actor player, Percepts percepts, long sinceTick)
    {
        TilePos at = player.Position;
        int sightReach = sim.SensesOf(player).SightRangeTiles;
        return new SenseReport(
            Ranked(SightCues(sim, player, percepts, at), sightReach),
            Ranked(SoundCues(sim, player, percepts, at), SoundReachTiles),
            Ranked(SmellCues(sim, player, at), SmellReachTiles),
            Ranked(FeelCues(sim, player, percepts, at), FeelReachTiles),
            ExposureOf(sim, player, percepts, sinceTick));
    }

    /// <summary>The strongest cues within the sense's reach (cues with no place, like rain, are everywhere).</summary>
    private static IReadOnlyList<SenseCue> Ranked(IEnumerable<SenseCue> cues, int reach) =>
        cues.Where(c => c.At is null || c.Distance <= reach)
            .OrderByDescending(c => c.Danger).ThenByDescending(c => c.Strength).ThenBy(c => c.Distance)
            .ThenBy(c => c.ActorId ?? -1).ThenBy(c => c.Kind).Take(MaxCues).ToList();

    private static int Distance(TilePos a, TilePos b)
    {
        long dx = b.X - a.X, dy = b.Y - a.Y;
        return (int)Math.Round(Math.Sqrt(dx * dx + dy * dy));
    }

    private static Direction? Toward(TilePos from, TilePos to) =>
        from == to ? null : Directions.FromDelta(to.X - from.X, to.Y - from.Y);

    private static SenseCue Placed(Sense sense, CueKind kind, TilePos from, TilePos to, int strength,
        int? actorId = null, string? noise = null, bool danger = false) =>
        new(sense, kind, to, Toward(from, to), Distance(from, to), Math.Clamp(strength, 0, 100), actorId, noise, danger);

    /// <summary>Whether something is a creature that could be dangerous: anything that isn't a person.</summary>
    private static bool IsCreature(Actor a) => a.Species != Species.Human;

    // ---------- sight ----------

    private static IEnumerable<SenseCue> SightCues(Simulation sim, Actor player, Percepts percepts, TilePos at)
    {
        foreach (SightContact c in percepts.Seen)
        {
            if (!c.Detected || c.Target.IsGone)
                continue;
            bool creature = IsCreature(c.Target);
            yield return Placed(Sense.Sight, creature ? CueKind.Creature : CueKind.Person, at, c.Target.Position,
                c.Score, c.Target.Id, danger: creature);
        }

        // the dead, where you can make them out
        int threshold = sim.SensesOf(player).DetectThreshold;
        foreach (Actor a in sim.Actors)
        {
            if (!a.IsDead || a.LeftBehind || a == player || Distance(at, a.Position) > sim.SensesOf(player).SightRangeTiles)
                continue;
            int score = PerceptionSystem.SightScore(sim, player, a.Position, 5, moving: false);
            if (score >= threshold / 2)
                yield return Placed(Sense.Sight, CueKind.Corpse, at, a.Position, score, a.Id);
        }

        // fires and flares in view: their light gives them away
        var flares = sim.GroundFlares.ToHashSet();
        SenseCue? fire = null;
        int fires = 0;
        foreach (SeenLight light in percepts.Lights)
        {
            TilePos p = light.Light.Position;
            if (sim.FireAt(p) is { } burning)
            {
                fires++;
                int d = Distance(at, p);
                if (fire is null || d < fire.Distance)
                    fire = Placed(Sense.Sight, CueKind.Fire, at, p, burning.Intensity, danger: d <= HeatRangeTiles);
            }
            else if (flares.Contains(p))
            {
                yield return Placed(Sense.Sight, CueKind.Flare, at, p, light.Score);
            }
        }
        if (fire is not null)
            yield return fire with { Strength = Math.Min(100, fire.Strength + fires) };

        // a pall of smoke, if there's a fire to make it
        if (sim.Fires.Count > 0 && ThickestNear(sim, at, 20, threshold: 40) is { } smoke)
            yield return Placed(Sense.Sight, CueKind.Smoke, at, smoke.Tile, smoke.Value);
    }

    /// <summary>The nearest tile within <paramref name="radius"/> with at least <paramref name="threshold"/> of smoke (the thicker on a tie).</summary>
    private static (TilePos Tile, int Value)? ThickestNear(Simulation sim, TilePos at, int radius, int threshold)
    {
        (TilePos Tile, int Value)? best = null;
        int bestDistance = int.MaxValue;
        for (int y = Math.Max(0, at.Y - radius); y <= Math.Min(sim.Map.Height - 1, at.Y + radius); y++)
        {
            for (int x = Math.Max(0, at.X - radius); x <= Math.Min(sim.Map.Width - 1, at.X + radius); x++)
            {
                var p = new TilePos(x, y);
                int smoke = sim.Map.Smoke[p];
                if (smoke < threshold)
                    continue;
                int d = Distance(at, p);
                if (d > radius)
                    continue;
                if (d < bestDistance || d == bestDistance && smoke > best!.Value.Value)
                {
                    best = (p, smoke);
                    bestDistance = d;
                }
            }
        }
        return best;
    }

    // ---------- sound ----------

    /// <summary>Noises that mean danger: something big at work, or someone in trouble.</summary>
    private static readonly HashSet<string> DangerNoises =
    [
        NoiseKinds.Jaws, NoiseKinds.Crashing, NoiseKinds.HeavyBlow, NoiseKinds.Collapse, NoiseKinds.Scream,
        NoiseKinds.TanksBursting, NoiseKinds.MetalTear, NoiseKinds.MetalCrunch,
    ];

    private static IEnumerable<SenseCue> SoundCues(Simulation sim, Actor player, Percepts percepts, TilePos at)
    {
        // one cue per source: an actor's loudest noise, or the loudest of each kind of noise from the world
        var loudest = new Dictionary<string, HeardNoise>();
        foreach (HeardNoise heard in percepts.Heard)
        {
            NoiseEvent n = heard.Noise;
            if (n.ActorId == player.Id)
                continue;
            string key = n.ActorId is { } id ? $"actor:{id}" : $"kind:{n.Kind}";
            if (!loudest.TryGetValue(key, out HeardNoise? had) || heard.Perceived > had.Perceived)
                loudest[key] = heard;
        }
        foreach (HeardNoise heard in loudest.Values)
        {
            NoiseEvent n = heard.Noise;
            bool danger = DangerNoises.Contains(n.Kind)
                || n.ActorId is { } id && IsCreature(sim.Actors[id]) && n.Kind == NoiseKinds.Footsteps;
            yield return Placed(Sense.Sound, CueKind.Noise, at, n.Position, heard.Perceived * 3, n.ActorId, n.Kind, danger);
        }

        // the roar of running water or breaking surf, toward where it's loudest
        TilePos ear = sim.EyeTile(player);
        int mask = sim.WaterMaskAt(ear);
        if (mask > 0 && WaterSource(sim, ear, mask) is { } water)
            yield return Placed(Sense.Sound, water.Surf ? CueKind.Surf : CueKind.Water, at, water.Tile, mask * 5);

        bool outdoors = !sim.Map.IsIndoor(at);
        long now = sim.CurrentTick;
        if (sim.Weather.Thunder(now))
            yield return new SenseCue(Sense.Sound, CueKind.Thunder, null, null, 0, 100);
        int rain = sim.Weather.Settings.RainPercent;
        if (rain >= 20)
            yield return new SenseCue(Sense.Sound, CueKind.Rain, null, null, 0, outdoors ? rain : rain / 2);
    }

    /// <summary>
    /// Where the water's roar comes from: the tile whose own roar, less a point a tile, is loudest here
    /// (the same rule the mask is spread by), and whether it's surf. No source is louder than
    /// <see cref="Simulation.SurfMaskAtFull"/>, so none that gives <paramref name="mask"/> here lies
    /// farther than that less the mask.
    /// </summary>
    private static (TilePos Tile, bool Surf)? WaterSource(Simulation sim, TilePos ear, int mask)
    {
        int radius = Math.Max(0, Math.Max(Simulation.SurfMaskAtFull, Simulation.RiverMaskMax) - mask);
        (TilePos Tile, bool Surf)? best = null;
        int bestValue = 0;
        long bestSquared = long.MaxValue;
        for (int y = Math.Max(0, ear.Y - radius); y <= Math.Min(sim.Map.Height - 1, ear.Y + radius); y++)
        {
            for (int x = Math.Max(0, ear.X - radius); x <= Math.Min(sim.Map.Width - 1, ear.X + radius); x++)
            {
                var p = new TilePos(x, y);
                if (sim.Cover[p] is not (CoverType.ShallowWater or CoverType.DeepWater))
                    continue;
                int surf = sim.IsSurfEdge(p) ? sim.SurfPercentAt(p) * Simulation.SurfMaskAtFull / 100 : 0;
                int river = Math.Min(Simulation.RiverMaskMax, sim.CurrentAt(p).SpeedDm / 2);
                int source = Math.Max(surf, river);
                if (source == 0)
                    continue;
                int dx = x - ear.X, dy = y - ear.Y;
                int here = source - Math.Max(Math.Abs(dx), Math.Abs(dy));
                long squared = (long)dx * dx + (long)dy * dy;
                // the loudest; of those as loud, the nearest as the crow flies (so the arrow points straight at it)
                if (here > bestValue || here == bestValue && here > 0 && squared < bestSquared)
                {
                    bestValue = here;
                    bestSquared = squared;
                    best = (p, surf >= river);
                }
            }
        }
        return best;
    }

    // ---------- smell ----------

    private static IEnumerable<SenseCue> SmellCues(Simulation sim, Actor player, TilePos at)
    {
        foreach (SmelledTrail trail in PerceptionSystem.SmellTrails(sim, player, PlayerNose))
        {
            Actor owner = sim.Actors[trail.ActorId];
            if (owner == player || owner.IsDead && !trail.Blood)
                continue;
            yield return Placed(Sense.Smell, trail.Blood ? CueKind.Blood : CueKind.Trail, at, trail.At,
                trail.Perceived, owner.Id, danger: IsCreature(owner) && !owner.IsDead);
        }

        TilePos nose = sim.EyeTile(player);
        int smoke = sim.Map.Smoke[nose];
        Fire? nearest = NearestFire(sim, at);
        if (smoke >= 10)
        {
            // smoke on the air: toward the fire if there is one in smelling distance
            if (nearest is not null && Distance(at, nearest.Position) <= PlayerNose.ScentRangeTiles)
                yield return Placed(Sense.Smell, CueKind.Smoke, at, nearest.Position, smoke);
            else
                yield return new SenseCue(Sense.Smell, CueKind.Smoke, null, null, 0, smoke);
        }
        else if (nearest is not null && Distance(at, nearest.Position) <= 12)
        {
            yield return Placed(Sense.Smell, CueKind.Burning, at, nearest.Position, 30 + nearest.Intensity / 2);
        }

        if (NearestFuel(sim, at) is { } fuel)
            yield return Placed(Sense.Smell, CueKind.Fuel, at, fuel, 60 - 10 * Distance(at, fuel));

        foreach (Actor a in sim.Actors)
        {
            if (!a.IsDead || a.LeftBehind || a == player || sim.CurrentTick - a.Health.DiedTick < DeathSmellAfterTicks)
                continue;
            int d = Distance(at, a.Position);
            if (d <= DeathSmellTiles)
                yield return Placed(Sense.Smell, CueKind.Death, at, a.Position, 80 - 8 * d, a.Id);
        }
    }

    private static Fire? NearestFire(Simulation sim, TilePos at)
    {
        Fire? best = null;
        int bestDistance = int.MaxValue;
        foreach (Fire f in sim.Fires)
        {
            int d = Distance(at, f.Position);
            if (d < bestDistance || d == bestDistance && best is not null && f.Intensity > best.Intensity)
            {
                best = f;
                bestDistance = d;
            }
        }
        return best;
    }

    private static TilePos? NearestFuel(Simulation sim, TilePos at)
    {
        TilePos? best = null;
        int bestDistance = int.MaxValue;
        for (int y = Math.Max(0, at.Y - FuelSmellTiles); y <= Math.Min(sim.Map.Height - 1, at.Y + FuelSmellTiles); y++)
        {
            for (int x = Math.Max(0, at.X - FuelSmellTiles); x <= Math.Min(sim.Map.Width - 1, at.X + FuelSmellTiles); x++)
            {
                var p = new TilePos(x, y);
                int d = Distance(at, p);
                if (d <= FuelSmellTiles && d < bestDistance && sim.FuelAt(p))
                {
                    best = p;
                    bestDistance = d;
                }
            }
        }
        return best;
    }

    // ---------- feel ----------

    private static IEnumerable<SenseCue> FeelCues(Simulation sim, Actor player, Percepts percepts, TilePos at)
    {
        // the ground shaking: one cue per source, the strongest
        var strongest = new Dictionary<int, FeltVibration>();
        foreach (FeltVibration f in percepts.Felt)
        {
            int key = f.Vibration.ActorId ?? -1;
            if (!strongest.TryGetValue(key, out FeltVibration? had) || f.Perceived > had.Perceived)
                strongest[key] = f;
        }
        foreach (FeltVibration f in strongest.Values)
        {
            bool creature = f.Vibration.ActorId is { } id && IsCreature(sim.Actors[id]);
            yield return Placed(Sense.Feel, CueKind.Tremor, at, f.Vibration.Position, f.Perceived * 3, f.Vibration.ActorId,
                danger: creature && f.Perceived >= 15);
        }

        // heat from a fire close by
        Fire? hottest = null;
        int heat = 0;
        foreach (Fire fire in sim.Fires)
        {
            int d = Distance(at, fire.Position);
            if (d > HeatRangeTiles)
                continue;
            int h = fire.Intensity * (HeatRangeTiles + 1 - d) / (HeatRangeTiles + 1);
            if (h > heat || h == heat && hottest is not null && (fire.Position.Y, fire.Position.X).CompareTo((hottest.Position.Y, hottest.Position.X)) < 0)
            {
                heat = h;
                hottest = fire;
            }
        }
        if (hottest is not null && heat > 0)
            yield return Placed(Sense.Feel, CueKind.Heat, at, hottest.Position, heat, danger: Distance(at, hottest.Position) <= 2);

        // water: wading, and the current pushing
        CoverType cover = sim.Cover[at];
        if (!player.OnDeck && cover is CoverType.ShallowWater or CoverType.DeepWater)
        {
            WaterFlow flow = sim.CurrentAt(at);
            if (!flow.IsNone)
            {
                int speed = flow.SpeedDm;
                yield return new SenseCue(Sense.Feel, CueKind.Current, null, Directions.FromDelta(flow.X, flow.Y), 0,
                    speed * 5, Danger: speed >= Simulation.SweepSpeedDm);
            }
            else
            {
                yield return new SenseCue(Sense.Feel, CueKind.Wading, null, null, 0, 40);
            }
        }

        // the wind on your face, outdoors
        var weather = sim.Weather.Settings;
        if (!sim.Map.IsIndoor(at) && weather.WindSpeed >= 4)
            yield return new SenseCue(Sense.Feel, CueKind.Wind, null, weather.WindFrom.Opposite(), 0, weather.WindSpeed * 5);
    }

    // ---------- exposure ----------

    /// <summary>
    /// The feeling of being watched. Every creature within <see cref="WatcherRangeTiles"/> is weighed by
    /// its own senses on the player: if one detects them, they are Seen (if they can see it too) or
    /// Spotted; if one comes within <see cref="WatchedPercent"/>% of making them out, heard their last
    /// noises or has their scent, they are Watched.
    /// </summary>
    private static Exposure ExposureOf(Simulation sim, Actor player, Percepts percepts, long sinceTick)
    {
        TilePos at = player.Position;
        bool moving = player.MovedRecently(sim.CurrentTick, PerceptionSystem.MotionWindowTicks);
        long wash = sim.Weather.Wash(sim.CurrentTick);
        int freshest = -1; // the player's freshest scent mark
        for (int i = sim.ScentMarks.Count - 1; i >= 0; i--)
        {
            if (sim.ScentMarks[i].ActorId == player.Id)
            {
                freshest = i;
                break;
            }
        }

        Actor? top = null, seenDetector = null, unseenDetector = null;
        long topRatio = -1;
        int topScore = 0, topThreshold = 0;
        bool heard = false, smelled = false;
        foreach (Actor w in sim.Actors)
        {
            if (w == player || w.IsGone || !IsCreature(w) || Distance(at, w.Position) > WatcherRangeTiles)
                continue;
            SenseProfile senses = sim.SensesOf(w);
            int score = PerceptionSystem.SightScore(sim, w, at, sim.StandHeight(player), player.Species.BodyHeightDm, moving);
            int threshold = Math.Max(1, senses.DetectThreshold);
            long ratio = (long)score * 1000 / threshold;
            if (ratio > topRatio)
            {
                topRatio = ratio;
                top = w;
                topScore = score;
                topThreshold = threshold;
            }
            if (score >= threshold)
            {
                if (percepts.Detects(w))
                    seenDetector ??= w;
                else
                    unseenDetector ??= w;
            }

            foreach (NoiseEvent n in sim.RecentNoises)
            {
                if (heard)
                    break;
                if (n.ActorId == player.Id && n.Tick >= sinceTick
                    && PerceptionSystem.HearingLevel(sim, w, n.Position, n.Loudness, n.Tick) >= senses.HearingThreshold)
                    heard = true;
            }
            if (!smelled && freshest >= 0 && senses.ScentRangeTiles > 0)
            {
                ScentMark mark = sim.ScentMarks[freshest];
                int strength = mark.StrengthAt(wash);
                smelled = strength >= senses.ScentThreshold
                    && PerceptionSystem.ScentLevel(sim, w, mark.Position, strength) >= senses.ScentThreshold;
            }
        }

        if (seenDetector is not null)
            return Weighed(ExposureStage.Seen, seenDetector);
        if (unseenDetector is not null)
            return Weighed(ExposureStage.Spotted, unseenDetector);
        if (top is not null && (topRatio >= WatchedPercent * 10 || heard || smelled))
            return new Exposure(ExposureStage.Watched, top.Id, topScore, topThreshold, heard, smelled);
        return top is null ? Exposure.Nothing : new Exposure(ExposureStage.None, top.Id, topScore, topThreshold, heard, smelled);

        Exposure Weighed(ExposureStage stage, Actor who)
        {
            int score = PerceptionSystem.SightScore(sim, who, at, sim.StandHeight(player), player.Species.BodyHeightDm, moving);
            return new Exposure(stage, who.Id, score, sim.SensesOf(who).DetectThreshold, heard, smelled);
        }
    }
}
