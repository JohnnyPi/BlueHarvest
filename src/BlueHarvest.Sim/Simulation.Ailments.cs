using BlueHarvest.Sim.Actors;
using BlueHarvest.Sim.Injuries;
using BlueHarvest.Sim.Perception;
using BlueHarvest.Sim.World;
using BlueHarvest.Content;
using BlueHarvest.Sim.ContentIntegration;
using BlueHarvest.Sim.Events;

namespace BlueHarvest.Sim;

/// <summary>Where water on a tile comes from, for drinking: none, fresh (streams, rivers, pools) or the sea.</summary>
public enum WaterSource : byte
{
    None,
    Fresh,
    Salt,
}

/// <summary>
/// Warmth and sickness (step 22b): the air's temperature and how warm it feels where someone is,
/// getting wet and drying out, drinking from streams and filling bottles, bad water, dirty wounds,
/// antibiotics. The minute-by-minute bookkeeping is <see cref="PassMinute"/>, called by UpdateNeeds.
/// </summary>
public partial class Simulation
{
    private int _baseTemperature = NeedRules.DefaultBaseTemperature;

    /// <summary>Someone drank from a stream or pool (at the tile).</summary>
    public event Action<Actor, TilePos>? DrankWater;

    /// <summary>Someone filled a bottle or canteen from a stream or pool.</summary>
    public event Action<Actor, Item>? WaterFilled;

    /// <summary>The place's mean temperature at sea level, in tenths of a degree (26 °C unless set).</summary>
    public int BaseTemperature => _baseTemperature;

    /// <summary>Sets the mean temperature at sea level, in °C (the island's sea temperature).</summary>
    public void SetBaseTemperature(double celsius) => _baseTemperature = (int)Math.Round(celsius * 10);

    /// <summary>
    /// The air's temperature at a tile, in tenths of a degree: the mean, the day's swing (coldest at
    /// 05:00, warmest at 15:00; none without a clock), 6.5 °C a kilometre up, cooler in rain and, out
    /// in the open, warmer in the sun.
    /// </summary>
    public int AirTemperature(TilePos p, bool outdoors = true) => AirTemperature(p, CurrentTick, outdoors);

    private int AirTemperature(TilePos p, long tick, bool outdoors)
    {
        int t = _baseTemperature - (Map.InBounds(p) ? Map.Heights[p] * 65 / 10_000 : 0);
        var weather = Weather.Settings;
        if (Clock is { } clock)
        {
            int m = clock.MinuteOfDay(tick);
            t += m is >= 300 and < 900
                ? -NeedRules.DaySwing + 2 * NeedRules.DaySwing * (m - 300) / 600
                : NeedRules.DaySwing - 2 * NeedRules.DaySwing * ((m - 900 + WorldClock.MinutesPerDay) % WorldClock.MinutesPerDay) / 840;
            if (outdoors && weather.RainPercent < 30 && weather.CloudPercent < 60 && SunPercent(tick) > 50)
                t += NeedRules.SunWarming;
        }
        if (weather.RainPercent >= 30)
            t -= NeedRules.RainCooling;
        return t;
    }

    /// <summary>What matters about where someone is for warmth: a roof, standing in water, a fire, and what they wear.</summary>
    private readonly record struct Whereabouts(bool Sheltered, bool Wading, int Fire, bool Poncho, int Clothes);

    private Whereabouts PlaceOf(Actor actor)
    {
        TilePos p = actor.Position;
        bool sheltered = Map.InBounds(p) && (Map.IsIndoor(p) || VehicleAt(p) is { Breached: false });
        bool wading = !actor.OnDeck && Map.InBounds(p) && Cover[p] == CoverType.ShallowWater;
        int fire = 0;
        foreach (Fire f in _fires.Values)
        {
            int d = Math.Max(Math.Abs(f.Position.X - p.X), Math.Abs(f.Position.Y - p.Y));
            if (d <= 2) { fire = 2; break; }
            if (d <= 5) fire = 1;
        }
        var inventory = actor.Inventory;
        bool poncho = inventory.WornOn(WearSlot.Body)?.Def.Has(ItemHandlers.RainCover) == true;
        int clothes = 0;
        foreach (WearSlot slot in new[] { WearSlot.Body, WearSlot.Hands, WearSlot.Feet, WearSlot.Head })
        {
            if (inventory.WornOn(slot) is { } worn)
                clothes += worn.Def.Warmth; // its content's warmth
        }
        return new Whereabouts(sheltered, wading, fire, poncho, clothes);
    }

    /// <summary>How warm it feels to someone now, in tenths of a degree (the air for those without needs).</summary>
    public int FeelsLike(Actor actor) =>
        actor.Needs is { } needs ? FeelsLike(actor, needs, CurrentTick, PlaceOf(actor)) : AirTemperature(actor.Position, CurrentTick, true);

    private int FeelsLike(Actor actor, Needs needs, long tick, Whereabouts place)
    {
        int t = AirTemperature(actor.Position, tick, !place.Sheltered);
        if (!place.Sheltered)
            t -= Weather.Settings.WindSpeed * NeedRules.WindChillPerMs * (place.Poncho ? 50 : 100) / 100;
        t -= needs.Wet * NeedRules.SoakedChill / 100;
        t += place.Clothes;
        if (needs.Asleep)
            t -= NeedRules.AsleepChill;
        else if (actor.MovedRecently(tick, PerceptionSystem.MotionWindowTicks))
            t += NeedRules.MovingWarmth;
        t += place.Fire == 2 ? NeedRules.FireCloseWarmth : place.Fire == 1 ? NeedRules.FireNearWarmth : 0;
        return t;
    }

    /// <summary>
    /// One game minute of needs: food, water and sleep (faster when cold, hot or ill), heat lost or
    /// regained, getting wet or drying, and an infection growing in dirty wounds (or beaten back).
    /// </summary>
    private void PassMinute(Actor actor, Needs needs, long tick, Whereabouts place)
    {
        var health = actor.Health;
        int warmth = needs.Stage(NeedKind.Warmth, health, tick);
        int sick = needs.Stage(NeedKind.Sickness, health, tick);
        int feels = FeelsLike(actor, needs, tick, place);

        var profile = needs.Profile;
        int hungerPercent = 100 + (warmth >= 3 ? 50 : warmth >= 2 ? 30 : 0);
        int thirstPercent = 100 + Math.Clamp((feels - NeedRules.SweatAbove) / 10 * NeedRules.SweatPercentPerDegree, 0, 100)
            + (sick >= 3 ? 50 : sick >= 2 ? 20 : 0)
            + (needs.GutSickness(tick) >= NeedRules.GutSick ? 30 : 0);
        if (needs.Asleep)
        {
            needs.Hunger += NeedRules.HungerAsleepPerMinute * hungerPercent / 100 * profile.HungerPercent / 100;
            needs.Thirst += NeedRules.ThirstAsleepPerMinute * thirstPercent / 100 * profile.ThirstPercent / 100;
            needs.Fatigue = Math.Max(0, needs.Fatigue - NeedRules.SleepRecoveryPerMinute);
        }
        else
        {
            needs.Hunger += NeedRules.HungerPerMinute * hungerPercent / 100 * profile.HungerPercent / 100;
            needs.Thirst += NeedRules.ThirstPerMinute * thirstPercent / 100 * profile.ThirstPercent / 100;
            needs.Fatigue += sick >= 3 ? 2 : 1; // a fever wears you out
        }

        // heat: lost below comfort, in proportion; won back above it (big animals don't feel it)
        if (!profile.FeelsCold)
            needs.Chill = 0;
        else if (feels < NeedRules.ComfortTemperature)
            needs.Chill += (NeedRules.ComfortTemperature - feels) / 10;
        else
            needs.Chill = Math.Max(0, needs.Chill - Math.Min(NeedRules.MostWarmingPerMinute, (feels - NeedRules.ComfortTemperature) / 5));

        // wet: rain out in the open (much less under a poncho), wading; dry out otherwise
        int rain = place.Sheltered ? 0 : Weather.Settings.RainPercent;
        int soak = rain * (place.Poncho ? NeedRules.PonchoRainPercent : 100) / 1000;
        if (place.Wading)
            needs.Wet = Math.Max(needs.Wet, NeedRules.WadingWet);
        if (soak > 0)
            needs.Wet = Math.Min(100, needs.Wet + soak);
        else if (!place.Wading && needs.Wet > 0)
        {
            int dry = 1 + (place.Sheltered ? 1 : 0) + (place.Fire == 2 ? 6 : place.Fire == 1 ? 2 : 0) + (feels >= 250 ? 1 : 0);
            needs.Wet = Math.Max(0, needs.Wet - dry);
        }

        // infection: dirty wounds feed it; antibiotics beat it back
        if (!profile.CatchesIllness)
            needs.Infection = 0;
        else if (needs.OnAntibiotics(tick))
            needs.Infection = Math.Max(0, needs.Infection - NeedRules.AntibioticQuarterCure);
        else
            needs.Infection += needs.DirtyWounds(health, tick) * NeedRules.InfectionQuarterPerWound;
    }

    /// <summary>Red wounds of someone's that have gone uncleaned too long (0 for those without needs).</summary>
    public int DirtyWounds(Actor actor) => actor.Needs is { } needs ? needs.DirtyWounds(actor.Health, CurrentTick) : 0;

    /// <summary>Red wounds of someone's that haven't been cleaned yet, dirty or not (0 for those without needs).</summary>
    public int UncleanedWounds(Actor actor)
    {
        if (actor.Needs is not { } needs)
            return 0;
        int count = 0;
        foreach (var section in Enum.GetValues<BodySection>())
        {
            var wound = actor.Health[section];
            if (wound.Severity == Severity.Red && needs.CleanedTick[(int)section] < wound.SinceTick)
                count++;
        }
        return count;
    }

    /// <summary>
    /// What water there is at a tile to drink: a river is fresh, the sea's edge salt; other water is
    /// fresh, unless it's the open sea of an island run (at sea level).
    /// </summary>
    public WaterSource WaterSourceAt(TilePos p)
    {
        if (!Map.InBounds(p) || Cover[p] is not (CoverType.ShallowWater or CoverType.DeepWater))
            return WaterSource.None;
        WaterTile w = Map.Water[p];
        if (w.Kind == WaterKind.Sea)
            return WaterSource.Salt;
        if (w.Kind == WaterKind.River)
            return WaterSource.Fresh;
        return Waters is not null && Map.Heights[p] <= SeaLevelDm + HeightMap.DecimetersPerLevel ? WaterSource.Salt : WaterSource.Fresh;
    }

    private string? WaterRefusal(TilePos at) => WaterSourceAt(at) switch
    {
        WaterSource.None => "there's no water there",
        WaterSource.Salt => "that's seawater: it would only make you thirstier",
        _ => null,
    };

    /// <summary>A drink straight from a stream or pool, on your tile or the next: half a litre, untreated.</summary>
    private int DrinkFrom(Actor actor, Direction? direction)
    {
        TilePos at = direction is { } d ? actor.Position.Offset(d) : actor.Position;
        // a big body drinks where its head is
        if (direction is null && actor.IsLarge && WaterSourceAt(at) != WaterSource.Fresh)
        {
            foreach (TilePos head in BiteZone(actor))
            {
                if (WaterSourceAt(head) != WaterSource.Fresh)
                    continue;
                at = head;
                break;
            }
        }
        if (WaterRefusal(at) is { } why)
            return Refuse(actor, why);
        if (direction is { } facing)
            actor.Facing = facing;
        Nourish(actor, (0, NeedRules.DrinkFromWaterMl, 0));
        MaybeCatchGutInfection(actor);
        EmitNoise(at, 4, NoiseKinds.Drinking, actor.Id);
        Emit(new DrankWaterPayload(actor, at));
        return NeedRules.DrinkFromWaterTicks * NeedEffectsOf(actor).HandsPercent / 100;
    }

    /// <summary>Fills a carried water bottle or canteen from a stream or pool: full, and untreated.</summary>
    private int Fill(Actor actor, int itemId, Direction? direction)
    {
        if (actor.Inventory.Find(itemId) is not { } item)
            return Refuse(actor, "you don't have that");
        if (!item.Def.Has(ItemHandlers.Refill))
            return Refuse(actor, $"you can't carry water in the {item.Def.Name}");
        TilePos at = direction is { } d ? actor.Position.Offset(d) : actor.Position;
        if (WaterRefusal(at) is { } why)
            return Refuse(actor, why);
        if (direction is { } facing)
            actor.Facing = facing;
        item.Amount = item.Def.MaxAmount;
        item.Unsafe = true;
        EmitNoise(at, 4, NoiseKinds.Drinking, actor.Id);
        Emit(new WaterFilledPayload(actor, item));
        return NeedRules.FillTicks * NeedEffectsOf(actor).HandsPercent / 100;
    }

    /// <summary>A drink of untreated water: one time in five, a gut infection comes on six hours later (one at a time).</summary>
    private void MaybeCatchGutInfection(Actor actor)
    {
        if (actor.Needs is not { } needs || needs.GutInfected(CurrentTick) || !needs.Profile.CatchesIllness)
            return;
        if (Rng.Fork($"bad-water:{actor.Id}:{CurrentTick}").NextInt(100) >= NeedRules.BadWaterPercent)
            return;
        needs.GutOnsetTick = CurrentTick + NeedRules.GutIncubationTicks;
        needs.GutEndTick = needs.GutOnsetTick + NeedRules.GutIllnessTicks;
    }

    /// <summary>Antiseptic, stitches or burn gel on your wounds: the red ones are clean now (an infection already started stays).</summary>
    private void CleanWounds(Actor actor)
    {
        if (actor.Needs is not { } needs)
            return;
        foreach (var section in Enum.GetValues<BodySection>())
        {
            if (actor.Health[section].Severity == Severity.Red)
                needs.CleanedTick[(int)section] = CurrentTick;
        }
    }

    /// <summary>A course of antibiotics: 12 hours beating an infection back (and the wounds count as clean); a gut infection is over in 6.</summary>
    private void TakeAntibiotics(Actor actor)
    {
        if (actor.Needs is not { } needs)
            return;
        UpdateNeeds(actor);
        needs.AntibioticsUntilTick = CurrentTick + NeedRules.AntibioticTicks;
        CleanWounds(actor);
        if (needs.GutInfected(CurrentTick))
            needs.GutEndTick = Math.Min(needs.GutEndTick, Math.Max(CurrentTick, needs.GutOnsetTick) + NeedRules.AntibioticGutTicks);
    }
}
