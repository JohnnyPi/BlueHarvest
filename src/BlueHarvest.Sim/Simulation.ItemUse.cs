using BlueHarvest.Sim.Actors;
using BlueHarvest.Sim.Injuries;
using BlueHarvest.Sim.Perception;

namespace BlueHarvest.Sim;

/// <summary>
/// Using items (step 20b): combining two (loading rounds, fitting batteries or a magazine, making a
/// splint), taking something out of what it's in, unloading, eating, drinking and the rest, keys and
/// keycards on locked containers, forcing a lock with a crowbar, and a flashlight's batteries
/// running down while it's on. Effects come only where a system exists to have them: a bandage stops
/// bleeding; food, water and pills are used up and logged until hunger, thirst and pain are built.
/// </summary>
public sealed partial class Simulation
{
    /// <summary>Two carried items went together (moved: rounds loaded; made: what a recipe made).</summary>
    public event Action<Actor, Combination, int, Item?>? ItemsCombined;

    /// <summary>Something was taken out of what it was in (the second item), or rounds unloaded from it.</summary>
    public event Action<Actor, Item, Item>? ItemTakenOut;

    /// <summary>Something carried was used: eaten, drunk, applied, read; the amount it used up.</summary>
    public event Action<Actor, Item, ItemUse, int>? ItemUsed;

    /// <summary>A locked container was opened with a key or keycard (the third item).</summary>
    public event Action<Actor, Item, Item>? ContainerUnlocked;

    /// <summary>A locked container was forced open.</summary>
    public event Action<Actor, Item>? LockForced;

    /// <summary>An actor's flashlight died: its batteries ran flat (or were taken out) while it was on.</summary>
    public event Action<Actor>? LightDied;

    // ---------- combining ----------

    private int Combine(Actor actor, int itemId, int withId)
    {
        var inventory = actor.Inventory;
        if (inventory.Find(itemId) is not { } a || inventory.Find(withId) is not { } b)
            return Refuse(actor, "you need both in hand or about you");
        if (Recipes.Of(a, b) is not { } combination)
            return Refuse(actor, $"the {a.Def.Name} and the {b.Def.Name} don't go together");

        var into = combination.Into;
        var from = combination.From;
        int moved = 0, ticks;
        Item? made = null;
        switch (combination.Kind)
        {
            case CombineKind.Load:
            {
                int room = into.Def.MaxAmount - into.Amount;
                if (room <= 0)
                    return Refuse(actor, $"the {into.Def.Name} is full");
                moved = Math.Min(room, from.Amount);
                into.Amount += moved;
                if (from.Amount > moved)
                    from.Amount -= moved;
                else
                    inventory.Remove(from);
                ticks = ItemRules.LoadTicksPerRound * moved;
                break;
            }
            case CombineKind.Fit:
            {
                if (into.RefusesToHold(from) is { } why)
                    return Refuse(actor, why);
                inventory.Remove(from);
                into.Insert(from);
                moved = 1;
                ticks = ItemRules.FitTicks;
                break;
            }
            default:
            {
                var recipe = combination.Recipe!;
                if (!HasUses(into, recipe.UsesOfA) || !HasUses(from, recipe.UsesOfB))
                    return Refuse(actor, $"not enough to make {ItemCatalog.Of(recipe.Makes).WithArticle}");
                UseUp(inventory, into, recipe.UsesOfA);
                UseUp(inventory, from, recipe.UsesOfB);
                made = CreateItem(recipe.Makes);
                if (!GiveItem(actor, made))
                    PlaceItem(made, actor.Position, actor.OnDeck);
                ticks = recipe.Ticks;
                break;
            }
        }
        EmitNoise(actor.Position, ItemRules.DropLoudness, NoiseKinds.Rummaging, actor.Id);
        ItemsCombined?.Invoke(actor, combination, moved, made);
        return ticks;
    }

    /// <summary>Whether an item has this many uses in it: a stack's count, a fill's amount, or itself.</summary>
    private static bool HasUses(Item item, int uses) =>
        item.Def.Stacks || item.Def.Unit is not null ? item.Amount >= uses : uses <= 1;

    /// <summary>Uses up so much of a carried item: from a stack or a fill, or the item itself if it has neither.</summary>
    private static void UseUp(Inventory inventory, Item item, int uses)
    {
        if (item.Def.Stacks)
        {
            if (item.Amount > uses)
                item.Amount -= uses;
            else
                inventory.Remove(item);
        }
        else if (item.Def.Unit is not null)
        {
            item.Amount = Math.Max(0, item.Amount - uses);
        }
        else
        {
            inventory.Remove(item);
        }
    }

    // ---------- taking out, unloading ----------

    private int TakeOut(Actor actor, int itemId)
    {
        var inventory = actor.Inventory;
        if (inventory.Find(itemId) is not { } item)
            return Refuse(actor, "you don't have that");
        if (inventory.HolderOf(item) is not { } holder)
            return Refuse(actor, $"the {item.Def.Name} isn't in anything");
        holder.Extract(item);
        var placement = inventory.PlaceFor(item);
        if (!placement.Ok || placement.Into == holder)
        {
            holder.Insert(item);
            return Refuse(actor, placement.Refused ?? "nowhere else to put it");
        }
        inventory.Add(item, placement);
        EmitNoise(actor.Position, ItemRules.DropLoudness, NoiseKinds.Rummaging, actor.Id);
        ItemTakenOut?.Invoke(actor, item, holder);
        return ItemRules.TakeOutTicks;
    }

    private int Unload(Actor actor, int itemId)
    {
        var inventory = actor.Inventory;
        if (inventory.Find(itemId) is not { } item)
            return Refuse(actor, "you don't have that");
        if (item.Def.Fills is { } fill && item.Amount > 0)
        {
            var rounds = CreateItem(fill, item.Amount);
            var placement = inventory.PlaceFor(rounds);
            if (!placement.Ok)
                return Refuse(actor, placement.Refused!);
            int count = item.Amount;
            item.Amount = 0;
            inventory.Add(rounds, placement);
            EmitNoise(actor.Position, ItemRules.DropLoudness, NoiseKinds.Rummaging, actor.Id);
            ItemTakenOut?.Invoke(actor, rounds, item);
            return ItemRules.LoadTicksPerRound * count / 2;
        }
        if (item.Def.Holds is { Only: not null } && item.Contents.Count > 0)
            return TakeOut(actor, item.Contents[^1].Id);
        return Refuse(actor, $"the {item.Def.Name} has nothing in it");
    }

    // ---------- using ----------

    private int UseItem(Actor actor, int itemId)
    {
        var inventory = actor.Inventory;
        if (inventory.Find(itemId) is not { } item)
            return Refuse(actor, "you don't have that");
        var def = item.Def;
        if (Recipes.UseOf(def) is not { } use)
            return Refuse(actor, $"you can't use the {def.Name} on its own");
        if (!def.Stacks && def.Unit is not null && item.Amount == 0)
            return Refuse(actor, $"the {def.Name} is empty");

        int used = 0;
        switch (use)
        {
            case ItemUse.Eat:
                UseUp(inventory, item, 1);
                used = 1;
                Nourish(actor, Nutrition.Food(def.Kind));
                break;
            case ItemUse.Drink:
                used = Math.Min(def.Dose, item.Amount);
                item.Amount -= used;
                Nourish(actor, Nutrition.Drink(def.Kind, used));
                if (item.Unsafe)
                    MaybeCatchGutInfection(actor);
                if (item.Amount == 0)
                    item.Unsafe = false; // nothing left in it to be bad
                break;
            case ItemUse.TakeMedicine:
                used = Math.Min(def.Dose, item.Amount);
                item.Amount -= used;
                if (def.Kind == ItemKind.Painkillers)
                    TakePainkillers(actor);
                else if (def.Kind == ItemKind.Antibiotics)
                    TakeAntibiotics(actor);
                break;
            case ItemUse.Inject:
                UseUp(inventory, item, 1);
                used = 1;
                Adrenaline(actor);
                break;
            case ItemUse.SwitchLight:
                return SwitchLight(actor, item);
            case ItemUse.Crack:
                return CrackGlowStick(actor, item);
            case ItemUse.LightTorch:
                return LightTorch(actor, item);
            case ItemUse.StopBleeding:
            {
                UpdateHealth(actor);
                var health = actor.Health;
                BodySection? section = null;
                foreach (var part in Enum.GetValues<BodySection>())
                {
                    if (health[part].Bleeding)
                    {
                        section = part;
                        break;
                    }
                }
                if (section is not { } bleeding)
                    return Refuse(actor, "nothing is bleeding");
                if (!health.CanTreat)
                    return Refuse(actor, "both arms are useless");
                health.Set(bleeding, health[bleeding] with { Bleeding = false });
                UseUp(inventory, item, 1);
                used = 1;
                break;
            }
            case ItemUse.DressWounds:
            {
                var health = actor.Health;
                if (Enum.GetValues<BodySection>().All(s => health[s].Severity == Severity.Green))
                    return Refuse(actor, "you have no wounds to see to");
                UseUp(inventory, item, 1);
                used = 1;
                if (def.Kind is ItemKind.Antiseptic or ItemKind.SutureKit or ItemKind.BurnGel)
                    CleanWounds(actor);
                break;
            }
            case ItemUse.Listen:
                if (!item.Charged)
                    return Refuse(actor, $"the {def.Name}'s batteries are flat");
                EmitNoise(actor.Position, ItemRules.RadioLoudness, NoiseKinds.RadioStatic, actor.Id);
                break;
            case ItemUse.TreatWater:
            {
                if (!inventory.All.Any(i => i.Def.Is(ItemCategory.Potable) && !i.Def.Stacks && i.Amount > 0))
                    return Refuse(actor, "you have no water to treat");
                // the first untreated water carried; a tablet in good water does no harm
                var bad = inventory.All.FirstOrDefault(i => i.Unsafe && i.Amount > 0);
                if (bad is not null)
                    bad.Unsafe = false;
                UseUp(inventory, item, 1);
                used = 1;
                break;
            }
        }
        if (use == ItemUse.Read)
            ReadParkMap(actor, item); // the first read may turn up something marked on it
        ItemUsed?.Invoke(actor, item, use, used);
        return Math.Max(RefuseTicks, def.UseTicks * NeedEffectsOf(actor).HandsPercent / 100);
    }

    // ---------- locks ----------

    /// <summary>A key or keycard the actor carries that opens a container: the same tag, and a level at least the lock's.</summary>
    public static Item? KeyFor(Actor actor, Item container) =>
        container.Tag is null ? null
        : actor.Inventory.All.FirstOrDefault(i => i.Def.Is(ItemCategory.Key) && i.Tag == container.Tag && i.Level >= container.Level);

    private int Pry(Actor actor, int itemId)
    {
        if (!HasHands(actor))
            return Bump(actor, "it can't do that");
        var reach = ItemsWithinReach(actor).FirstOrDefault(r => r.Item.Id == itemId && r.On is null);
        if (reach is null)
            return Refuse(actor, "you can't reach that from here");
        var container = reach.Item;
        if (!container.Locked)
            return Refuse(actor, $"the {container.Def.Name} isn't locked");
        if (!actor.Inventory.Has(ItemKind.Crowbar))
            return Refuse(actor, "you need a crowbar to force it");
        container.Locked = false;
        container.IsOpen = true;
        EmitNoise(reach.At, ItemRules.PryLoudness, NoiseKinds.Prying, actor.Id);
        LockForced?.Invoke(actor, container);
        return ItemRules.PryTicks;
    }

    // ---------- the flashlight's batteries ----------

    /// <summary>The flashlight that would light: the first one carried with both its batteries charged.</summary>
    private static Item? WorkingFlashlight(Actor actor) =>
        actor.Inventory.All.FirstOrDefault(i => i.Kind == ItemKind.Flashlight && i.Charged);

    /// <summary>
    /// Runs a lit flashlight's batteries down for the time since last time (1% per
    /// <see cref="ItemRules.LightTicksPerPercent"/>), and puts the light out if they're flat or gone.
    /// </summary>
    private void DrainLight(Actor actor)
    {
        if (actor.CarriedLight is not { } light || !_lights[light].On)
        {
            actor.LightDrainTick = CurrentTick;
            return;
        }
        var flashlight = WorkingFlashlight(actor);
        if (flashlight is not null)
        {
            long percent = (CurrentTick - actor.LightDrainTick) / ItemRules.LightTicksPerPercent;
            if (percent > 0)
            {
                actor.LightDrainTick += percent * ItemRules.LightTicksPerPercent;
                foreach (var cell in flashlight.Contents)
                    cell.Amount = (int)Math.Max(0, cell.Amount - percent);
            }
            if (flashlight.Charged)
                return;
        }
        SetLightOn(light, false);
        LightDied?.Invoke(actor);
    }
}
