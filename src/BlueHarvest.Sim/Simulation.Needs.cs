using BlueHarvest.Sim.Actors;
using BlueHarvest.Sim.Core;
using BlueHarvest.Sim.Perception;
using BlueHarvest.Sim.World;

namespace BlueHarvest.Sim;

/// <summary>
/// Hunger, thirst, sleep, pain and stamina, for the actors given needs (the player). They're brought
/// up to date at each of the actor's turns, a game minute at a time for food, water and sleep, and
/// tick by tick for breath; they slow and weaken, black out and in the end kill. Eating, drinking
/// and medicine are in Simulation.ItemUse.cs; sprinting and climbing spend breath in TryMove.
/// </summary>
public partial class Simulation
{
    /// <summary>A need moved to another stage (actor, need, new stage, old stage).</summary>
    public event Action<Actor, NeedKind, int, int>? NeedChanged;

    /// <summary>Someone fell asleep.</summary>
    public event Action<Actor>? FellAsleep;

    /// <summary>Someone woke up by themselves (why: "rested", "a noise: footsteps", "pain", "thirst", "hunger").</summary>
    public event Action<Actor, string>? WokeUp;

    /// <summary>Someone blacked out from thirst or exhaustion.</summary>
    public event Action<Actor, NeedKind>? NeedBlackedOut;

    /// <summary>
    /// Gives an actor needs from now on (the player). Without needs, nothing here touches them: they
    /// never tire, hunger, thirst or lose breath.
    /// </summary>
    public void UseNeeds(Actor actor, NeedsStart? start = null)
    {
        start ??= new NeedsStart();
        int awake = start.AwakeMinutes
            ?? (Clock is { } clock ? (clock.MinuteOfDay(CurrentTick) - 7 * 60 + WorldClock.MinutesPerDay) % WorldClock.MinutesPerDay : 120);
        var needs = new Needs(Math.Max(0, start.HungerKcal) * 10, Math.Max(0, start.ThirstMl) * 10, Math.Max(0, awake), CurrentTick);
        actor.Needs = needs;
        ReportNeeds(actor, needs, silent: true);
    }

    /// <summary>Sets someone's needs directly (setup, tests, debug): kcal and ml short, minutes awake, breath, heat lost, wetness.</summary>
    public void SetNeeds(Actor actor, int? hungerKcal = null, int? thirstMl = null, int? awakeMinutes = null, int? stamina = null,
        int? chill = null, int? wet = null)
    {
        if (actor.Needs is not { } needs)
            return;
        UpdateNeeds(actor);
        if (hungerKcal is { } h) needs.Hunger = Math.Max(0, h) * 10;
        if (thirstMl is { } t) needs.Thirst = Math.Max(0, t) * 10;
        if (awakeMinutes is { } a) needs.Fatigue = Math.Max(0, a);
        if (stamina is { } s) needs.Stamina = Math.Clamp(s, 0, NeedRules.MaxStamina);
        if (chill is { } c) needs.Chill = Math.Max(0, c);
        if (wet is { } w) needs.Wet = Math.Clamp(w, 0, 100);
        ReportNeeds(actor, needs, silent: true);
    }

    /// <summary>
    /// Brings someone's needs up to now: whole game minutes of hunger, thirst and waking (or sleep),
    /// breath regained since their last exertion; then death, waking, and stage changes.
    /// </summary>
    public void UpdateNeeds(Actor actor)
    {
        if (actor.Needs is not { } needs || actor.IsGone)
            return;
        long minute = CurrentTick / WorldClock.TicksPerGameMinute;
        if (minute > needs.Minute)
        {
            long from = needs.Minute;
            needs.Minute = minute;
            long last = Math.Min(minute, from + 100_000);
            var place = PlaceOf(actor);
            for (long m = from + 1; m <= last; m++)
                PassMinute(actor, needs, m * WorldClock.TicksPerGameMinute, place);
        }
        RegainBreath(actor, needs);

        if (needs.Thirst >= NeedRules.DiedOfThirstAt)
        {
            Kill(actor, "died of thirst");
            return;
        }
        if (needs.Hunger >= NeedRules.StarvedAt)
        {
            Kill(actor, "starved to death");
            return;
        }
        if (needs.Fatigue >= NeedRules.DiedOfExhaustionAt)
        {
            Kill(actor, "died of exhaustion");
            return;
        }
        if (needs.Chill >= NeedRules.DiedOfColdAt)
        {
            Kill(actor, "died of cold");
            return;
        }
        if (needs.Sickness(CurrentTick) >= NeedRules.DiedOfInfectionAt)
        {
            Kill(actor, "died of infection");
            return;
        }

        if (needs.Asleep)
        {
            if (needs.Fatigue == 0)
                Wake(actor, needs, "rested");
            else if (NoiseThatWakes(actor, needs) is { } noise)
                Wake(actor, needs, $"a noise: {noise.Kind}");
            else if (needs.Stage(NeedKind.Thirst, actor.Health, CurrentTick) >= 3)
                Wake(actor, needs, "thirst");
            else if (needs.Stage(NeedKind.Hunger, actor.Health, CurrentTick) >= 3)
                Wake(actor, needs, "hunger");
            else if (needs.Stage(NeedKind.Warmth, actor.Health, CurrentTick) >= 3)
                Wake(actor, needs, "cold");
        }
        ReportNeeds(actor, needs, silent: false);
    }

    /// <summary>What the needs do to someone now (no effect for those without needs).</summary>
    public NeedEffects NeedEffectsOf(Actor actor) =>
        actor.Needs is { } needs ? needs.Effects(actor.Health, CurrentTick) : NeedEffects.None;

    /// <summary>A need's stage for someone now (0 for those without needs).</summary>
    public int NeedStage(Actor actor, NeedKind kind) =>
        actor.Needs is { } needs ? needs.Stage(kind, actor.Health, CurrentTick) : 0;

    /// <summary>Why someone can't go to sleep now, or null if they can.</summary>
    public string? SleepBlock(Actor actor)
    {
        if (actor.Needs is not { } needs)
            return "you don't need to sleep";
        if (needs.Asleep)
            return null;
        if (needs.OnAdrenaline(CurrentTick))
            return "your heart is still pounding";
        if (needs.Fatigue < NeedRules.MinFatigueToSleep)
            return "you aren't tired enough to sleep";
        if (NeedStage(actor, NeedKind.Thirst) >= 3)
            return "you're too thirsty to sleep";
        if (NeedStage(actor, NeedKind.Hunger) >= 3)
            return "you're too hungry to sleep";
        if (NeedStage(actor, NeedKind.Warmth) >= 3)
            return "you're too cold to sleep";
        int pain = NeedStage(actor, NeedKind.Pain), sleep = NeedStage(actor, NeedKind.Sleep);
        if (pain >= 3 && sleep < 3 || pain == 2 && sleep < 2)
            return "it hurts too much to sleep";
        return null;
    }

    /// <summary>Lies down and sleeps (or sleeps on) for a while. Those without needs just wait.</summary>
    private int Sleep(Actor actor, int ticks)
    {
        if (actor.Needs is not { } needs)
            return Math.Max(1, ticks);
        if (!needs.Asleep)
        {
            if (needs.WokeTick == CurrentTick)
                return Math.Min(Math.Max(1, ticks), RefuseTicks); // woken this very turn: no going back down
            if (SleepBlock(actor) is { } why)
                return Refuse(actor, why);
            needs.Asleep = true;
            needs.WakeCheckTick = CurrentTick;
            actor.LastMovedTick = CurrentTick;
            FellAsleep?.Invoke(actor);
        }
        return Math.Max(1, ticks);
    }

    private void Wake(Actor actor, Needs needs, string why)
    {
        if (!needs.Asleep)
            return;
        needs.Asleep = false;
        needs.WokeTick = CurrentTick;
        WokeUp?.Invoke(actor, why);
    }

    /// <summary>A noise since the last check loud enough, where the sleeper lies, to wake them.</summary>
    private NoiseEvent? NoiseThatWakes(Actor actor, Needs needs)
    {
        NoiseEvent? loudest = null;
        int best = NeedRules.WakeLoudness - 1;
        foreach (var noise in _noises)
        {
            if (noise.Tick < needs.WakeCheckTick || noise.ActorId == actor.Id)
                continue;
            int heard = PerceptionSystem.HearingLevel(this, actor, noise.Position, noise.Loudness, noise.Tick);
            if (heard > best)
            {
                best = heard;
                loudest = noise;
            }
        }
        needs.WakeCheckTick = CurrentTick;
        return loudest;
    }

    /// <summary>Breath comes back over the time since the last exertion, slower on the move and as the needs bite.</summary>
    private void RegainBreath(Actor actor, Needs needs)
    {
        var effects = needs.Effects(actor.Health, CurrentTick);
        if (CurrentTick > needs.StaminaTick)
        {
            long elapsed = CurrentTick - needs.StaminaTick;
            needs.StaminaTick = CurrentTick;
            bool still = needs.Asleep || !actor.MovedRecently(CurrentTick, PerceptionSystem.MotionWindowTicks);
            int rate = effects.RegenPercent * (still ? 100 : NeedRules.MovingRegenPercent) / 100;
            long per = (long)NeedRules.RegenFullTicks * 100 / NeedRules.MaxStamina; // rate-ticks for one point
            long gain = elapsed * rate + needs.StaminaRemainder;
            needs.Stamina = (int)Math.Min(NeedRules.MaxStamina, needs.Stamina + gain / per);
            needs.StaminaRemainder = gain % per;
        }
        if (needs.Stamina > effects.StaminaCap)
            needs.Stamina = effects.StaminaCap;
    }

    /// <summary>
    /// Exertion: breath spent (and the food and water it burns), with none coming back for the
    /// <paramref name="busyTicks"/> the effort takes.
    /// </summary>
    private void SpendBreath(Actor actor, int amount, int busyTicks)
    {
        if (actor.Needs is not { } needs || amount <= 0)
            return;
        needs.Stamina = Math.Max(0, needs.Stamina - amount);
        needs.Hunger += amount * NeedRules.ExertionHunger;
        needs.Thirst += amount * NeedRules.ExertionThirstPercent / 100;
        needs.StaminaTick = Math.Max(needs.StaminaTick, CurrentTick + busyTicks);
    }

    /// <summary>Whether someone has the breath to sprint (always, without needs).</summary>
    public bool HasBreathToSprint(Actor actor) => actor.Needs is not { } needs || needs.Stamina >= NeedRules.SprintMinimum;

    /// <summary>A blackout from thirst or exhaustion on this turn, or null: a roll seeded by who and when.</summary>
    private NeedKind? NeedBlackout(Actor actor)
    {
        if (actor.Needs is not { Asleep: false } needs)
            return null;
        int percent = needs.Effects(actor.Health, CurrentTick).BlackoutPercent;
        if (percent <= 0 || Rng.Fork($"needs-blackout:{actor.Id}:{CurrentTick}").NextInt(100) >= percent)
            return null;
        // the cause named: the worst of the needs that black you out
        var health = actor.Health;
        if (needs.Stage(NeedKind.Sleep, health, CurrentTick) >= 3) return NeedKind.Sleep;
        if (needs.Stage(NeedKind.Thirst, health, CurrentTick) >= 3) return NeedKind.Thirst;
        if (needs.Stage(NeedKind.Warmth, health, CurrentTick) >= 4) return NeedKind.Warmth;
        return NeedKind.Sickness;
    }

    /// <summary>Senses as the needs leave them: asleep, blind and hard to wake; tired or parched, dimmer and duller.</summary>
    private SenseProfile AdjustForNeeds(Actor actor, SenseProfile senses)
    {
        if (actor.Needs is not { } needs)
            return senses;
        if (needs.Asleep)
            return senses with { SightRangeTiles = 0, HearingThreshold = senses.HearingThreshold + NeedRules.AsleepHearingPenalty };
        var effects = needs.Effects(actor.Health, CurrentTick);
        if (effects.SightPercent >= 100 && effects.HearingPenalty == 0)
            return senses;
        return senses with
        {
            SightRangeTiles = senses.SightRangeTiles * effects.SightPercent / 100,
            HearingThreshold = senses.HearingThreshold + effects.HearingPenalty,
        };
    }

    /// <summary>Food eaten or drink drunk: kcal and water made up, tiredness held off.</summary>
    private void Nourish(Actor actor, (int Kcal, int WaterMl, int AlertMinutes) got)
    {
        if (actor.Needs is not { } needs)
            return;
        UpdateNeeds(actor);
        needs.Hunger = Math.Max(0, needs.Hunger - got.Kcal * 10);
        needs.Thirst = Math.Max(0, needs.Thirst - got.WaterMl * 10);
        needs.Fatigue = Math.Max(0, needs.Fatigue - got.AlertMinutes);
    }

    /// <summary>Painkillers swallowed: relief for the next four game hours (a second dose only starts the clock again).</summary>
    private void TakePainkillers(Actor actor)
    {
        if (actor.Needs is { } needs)
            needs.PainkillersUntilTick = CurrentTick + NeedRules.PainkillerTicks;
    }

    /// <summary>Adrenaline: pain shut out and breath full again, for 30 s.</summary>
    private void Adrenaline(Actor actor)
    {
        if (actor.Needs is not { } needs)
            return;
        UpdateNeeds(actor);
        needs.AdrenalineUntilTick = CurrentTick + NeedRules.AdrenalineTicks;
        needs.Stamina = NeedRules.MaxStamina;
        needs.StaminaRemainder = 0;
    }

    /// <summary>Reports the needs whose stage has changed since last time (or just records them).</summary>
    private void ReportNeeds(Actor actor, Needs needs, bool silent)
    {
        foreach (var kind in Enum.GetValues<NeedKind>())
        {
            int stage = needs.Stage(kind, actor.Health, CurrentTick);
            int was = needs.Reported[(int)kind];
            if (stage == was)
                continue;
            needs.Reported[(int)kind] = stage;
            if (!silent)
                NeedChanged?.Invoke(actor, kind, stage, was);
        }
    }

    /// <summary>The needs in the state fingerprint (only actors that have them).</summary>
    private void AddNeedsTo(StateHasher hasher)
    {
        foreach (var actor in _actors)
        {
            if (actor.Needs is not { } needs)
                continue;
            hasher.Add(actor.Id).Add(needs.Hunger).Add(needs.Thirst).Add(needs.Fatigue).Add(needs.Stamina)
                .Add(needs.Asleep ? 1 : 0).Add(needs.PainkillersUntilTick).Add(needs.AdrenalineUntilTick)
                .Add(needs.Chill).Add(needs.Wet).Add(needs.GutOnsetTick).Add(needs.GutEndTick).Add(needs.Infection).Add(needs.AntibioticsUntilTick);
        }
    }
}
