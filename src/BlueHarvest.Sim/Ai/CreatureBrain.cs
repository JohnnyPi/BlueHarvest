using BlueHarvest.Sim.Actors;
using BlueHarvest.Sim.Core;
using BlueHarvest.Sim.Navigation;
using BlueHarvest.Sim.Perception;
using BlueHarvest.Sim.World;

namespace BlueHarvest.Sim.Ai;

/// <summary>What a creature is doing about its focus.</summary>
public enum Activity : byte
{
    /// <summary>Nothing worth its attention.</summary>
    Wandering,

    /// <summary>Going to look at something it hasn't identified: a noise, a glimpse, a light.</summary>
    Investigating,

    /// <summary>Going after prey.</summary>
    Hunting,

    /// <summary>Its focus is in reach of its jaws: biting, or test-biting if it isn't identified yet.</summary>
    Biting,

    /// <summary>Its focus is up on a platform out of reach: knocking out the posts holding it up.</summary>
    Smashing,
}

/// <summary>
/// High-tier creature AI: perceive, remember, choose, act. Each turn it folds what its senses report
/// into contact memory, scores every contact (utility AI), focuses on the best one if it beats
/// wandering, and pursues it: path there, line up its head, bite. Unidentified things get a test bite,
/// and what the bite felt reclassifies them.
/// It never reads the world beyond its percepts and its own bites, so hiding works.
/// </summary>
public sealed class CreatureBrain : IActorController, IWindowShiftListener
{
    private readonly RandomWalker _wander;
    private GoToController? _goTo;
    private GoToController? _goToExact;
    private TilePos _goal;
    private long _lastThinkTick = long.MinValue;
    private long _lastBiteSeenTick = long.MinValue;
    private ContactKey? _focusKey;
    private ContactKey? _biteTargetKey;
    private int _stuckTurns;

    /// <param name="rng">Its own stream, used only while wandering.</param>
    public CreatureBrain(AiProfile profile, Rng rng)
    {
        Memory = new ContactMemory(profile);
        _wander = new RandomWalker(rng);
    }

    public ContactMemory Memory { get; }

    /// <summary>The streamed map scrolled: what it remembers and where it was heading move with it.</summary>
    public void OnWindowShifted(int dx, int dy, WorldMap map)
    {
        Memory.Shift(dx, dy, map);
        _goal = new TilePos(Math.Clamp(_goal.X - dx, 0, map.Width - 1), Math.Clamp(_goal.Y - dy, 0, map.Height - 1));
    }

    public AiProfile Profile => Memory.Profile;

    /// <summary>What it's paying attention to; null while wandering. The escape check reads this.</summary>
    public Contact? Focus => _focusKey is { } key ? Memory.Find(key) : null;

    public Activity Activity { get; private set; }

    /// <summary>Moves left on its current route, for the path overlay. Empty while wandering.</summary>
    public IReadOnlyList<Direction> PlannedPath => Activity switch
    {
        Activity.Wandering => Array.Empty<Direction>(),
        Activity.Smashing => _goToExact?.RemainingPath ?? Array.Empty<Direction>(),
        _ => _goTo?.RemainingPath ?? Array.Empty<Direction>(),
    };

    public ActorAction? Decide(Actor self, Simulation sim)
    {
        long now = sim.CurrentTick;
        long since = _lastThinkTick == long.MinValue ? now - Simulation.NoiseMemoryTicks : _lastThinkTick;
        _lastThinkTick = now;

        LearnFromBite(self, sim, now);
        Memory.Observe(sim, self, PerceptionSystem.Perceive(sim, self, since));
        Memory.Forget(now);

        var focus = ChooseFocus(self, now);
        if (focus?.Key != _focusKey)
            _stuckTurns = 0;
        _focusKey = focus?.Key;

        if (focus is null)
        {
            Activity = Activity.Wandering;
            return _wander.Decide(self, sim);
        }

        Activity = focus.Class == ContactClass.Prey ? Activity.Hunting : Activity.Investigating;
        return Pursue(self, sim, focus, now);
    }

    /// <summary>A one-line account of what it's doing, for the HUD and the runner. Names are debug knowledge.</summary>
    public string Describe(Simulation sim)
    {
        if (Focus is not { } focus)
            return "wandering";

        string what = focus.Key.Source switch
        {
            ContactSource.Actor when focus.Key.Id < sim.Actors.Count => sim.Actors[focus.Key.Id].Name,
            ContactSource.Light => "a light",
            _ => "a sound",
        };
        int confidence = focus.Confidence(sim.CurrentTick, Profile);
        return Activity switch
        {
            Activity.Biting when focus.Class == ContactClass.Prey => $"biting at {what}",
            Activity.Biting => $"test-biting {what} at {focus.Estimate}",
            Activity.Smashing => $"smashing the supports under {what} ({focus.Cue}, confidence {confidence})",
            Activity.Hunting => $"{(sim.Actors.Any(a => a.Controller == this && a.Charging) ? "CHARGING" : "hunting")} {what} at {focus.Estimate} ({focus.Cue}, confidence {confidence})",
            _ => $"investigating {what} at {focus.Estimate} ({focus.Cue}, confidence {confidence})",
        };
    }

    /// <summary>
    /// How much a contact deserves attention. Prey outweighs the unidentified, which outweigh lights;
    /// confidence, motion and nearness raise it; having already searched there all but drops it.
    /// </summary>
    public int Score(Actor self, Contact contact, long now)
    {
        if (contact.Class is ContactClass.Object or ContactClass.Ally or ContactClass.Threat)
            return int.MinValue;
        int confidence = contact.Confidence(now, Profile);
        if (confidence <= 0)
            return int.MinValue;

        int weight = contact.Class == ContactClass.Prey ? Profile.HuntWeight
            : contact.Key.Source == ContactSource.Light ? Profile.LightWeight
            : Profile.InvestigateWeight;
        int score = confidence * weight / 100;
        if (contact.Class == ContactClass.Prey && contact.Moving)
            score += Profile.MovingPreyBonus;
        score -= Chebyshev(self.Position, contact.Estimate) * Profile.DistancePenaltyPer10Tiles / 10;

        if (contact.Searched)
            return score / 4;
        if (_focusKey == contact.Key)
            score += Profile.FocusBonus;
        return score;
    }

    private Contact? ChooseFocus(Actor self, long now)
    {
        Contact? best = null;
        int bestScore = Profile.WanderScore;
        foreach (var contact in Memory.All)
        {
            int score = Score(self, contact, now);
            if (score > bestScore)
            {
                best = contact;
                bestScore = score;
            }
        }
        return best;
    }

    /// <summary>
    /// Bite if the estimate is in reach. If it's up on a platform too high to reach, go and knock out
    /// the platform's nearest support post instead (the posts are in plain sight). Otherwise close in.
    /// </summary>
    private ActorAction Pursue(Actor self, Simulation sim, Contact focus, long now)
    {
        var target = focus.Estimate;
        bool reachable = sim.CanReach(self, target, focus.EstimateZ);
        if (reachable && sim.BiteZone(self).Contains(target))
        {
            Activity = Activity.Biting;
            _biteTargetKey = focus.Key;
            _stuckTurns = 0;
            return new BiteAction(Investigative: focus.Class != ContactClass.Prey);
        }

        if (!reachable && self.IsLarge && sim.PlatformAt(target) is { Collapsed: false } platform
            && NearestSupport(sim, self, platform) is { } support)
        {
            Activity = Activity.Smashing;
            return CloseIn(self, sim, focus, support, exact: true, now);
        }

        return CloseIn(self, sim, focus, target, exact: false, now);
    }

    /// <summary>
    /// Charge prey it can see (or saw a moment ago); the simulation only lets a straight step over
    /// open ground go at a charge, so turns and undergrowth still slow it.
    /// </summary>
    private bool ShouldCharge(Contact focus, long now) =>
        Profile.ChargeSightTicks > 0
        && focus.Class == ContactClass.Prey
        && focus.Cue == "seen"
        && now - focus.LastUpdatedTick <= Profile.ChargeSightTicks;

    /// <summary>
    /// Close in on <paramref name="target"/>: near it, turn the head onto it or step toward it
    /// (shoving a vehicle that's in the way); otherwise path there. With <paramref name="exact"/> the
    /// body itself has to get onto the tile (to trample a post). Too many turns without progress
    /// count as having searched.
    /// </summary>
    private ActorAction CloseIn(Actor self, Simulation sim, Contact focus, TilePos target, bool exact, long now)
    {
        var at = self.Position;
        int reach = self.Species.Body.HeadReach;
        int near = exact ? 2 : reach + 2;
        if (at != target && Chebyshev(at, target) <= near)
        {
            // Large bodies turn 45° toward it, or step forward once facing it (shoving a vehicle
            // that's in the way); small ones step toward it.
            var toward = Directions.FromDelta(target.X - at.X, target.Y - at.Y);
            if (sim.CanMove(self, toward, shove: true))
            {
                _stuckTurns = 0;
                return new MoveAction(toward, Shove: true, Sprint: ShouldCharge(focus, now));
            }
        }

        _goal = target;
        var goTo = exact
            ? (_goToExact ??= new GoToController(() => _goal, 0))
            : (_goTo ??= new GoToController(() => _goal, Math.Max(1, reach - 1)));
        var action = goTo.Decide(self, sim) ?? new WaitAction(self.Movement.StepTicks);
        if (action is MoveAction move)
        {
            _stuckTurns = 0;
            return ShouldCharge(focus, now) ? move with { Sprint = true } : move;
        }

        if (++_stuckTurns >= Profile.StuckTurnsBeforeGivingUp)
        {
            Memory.MarkSearched(focus, now);
            _stuckTurns = 0;
        }
        return action;
    }

    /// <summary>
    /// What its last bite felt. Anyone caught is now a certain contact. A test-bitten light turns out to
    /// be nothing to eat. An actor or sound it bit at and missed counts as searched, unless the jaws
    /// closed on metal right where it was: something hard is in the way, so it keeps at it.
    /// </summary>
    private static TilePos? NearestSupport(Simulation sim, Actor self, Platform platform) =>
        sim.SupportsOf(platform)
            .OrderBy(t => Chebyshev(t, self.Position))
            .ThenBy(t => t.Y)
            .ThenBy(t => t.X)
            .Select(t => (TilePos?)t)
            .FirstOrDefault();

    private void LearnFromBite(Actor self, Simulation sim, long now)
    {
        if (self.LastBite is not { } bite || bite.Tick <= _lastBiteSeenTick)
            return;
        _lastBiteSeenTick = bite.Tick;

        bool caughtTarget = false;
        foreach (var hit in bite.Hits)
        {
            Memory.Felt(hit.Victim, hit.At, sim.StandHeight(hit.Victim), now);
            if (_biteTargetKey == ContactKey.ForActor(hit.Victim.Id))
                caughtTarget = true;
        }

        if (!caughtTarget && _biteTargetKey is { } key && Memory.Find(key) is { } missed)
        {
            if (key.Source == ContactSource.Light)
                missed.Class = ContactClass.Object;
            else if (!bite.CoverBitten.Contains(missed.Estimate))
                Memory.MarkSearched(missed, now);
        }
        _biteTargetKey = null;
    }

    private static int Chebyshev(TilePos a, TilePos b) => Math.Max(Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));
}
