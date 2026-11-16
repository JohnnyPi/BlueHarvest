using BlueHarvest.Sim.Actors;
using BlueHarvest.Sim.Core;
using BlueHarvest.Sim.Perception;
using BlueHarvest.Sim.World;

namespace BlueHarvest.Sim;

/// <summary>Something within an actor's reach: where it is, and what holds it (a container, or a body).</summary>
/// <param name="In">The container directly holding it, if it's inside one.</param>
/// <param name="On">The body it's on, if it's a dead person's.</param>
public sealed record ReachableItem(Item Item, TilePos At, bool OnDeck, Item? In = null, Actor? On = null);

/// <summary>
/// Items (step 20a): every item is made by the run's ledger, and lies in the world, is carried, or is
/// inside a container. Within reach (your tile or the next, at your level) you can pick things up,
/// put them down, put them in containers, open lockers and crates, and search the dead. What you
/// carry weighs on you: burdened over 10 kg, heavily laden over 20, and 35 kg is the most anyone carries.
/// </summary>
public sealed partial class Simulation
{
    /// <summary>A refused item action (nothing to reach, no room) costs this much: a moment's fumbling, no noise.</summary>
    public const int RefuseTicks = 10;

    private ItemLedger _items = new();

    /// <summary>The run's items: everything lying in the world, and the maker of every item.</summary>
    public ItemLedger Items => _items;

    /// <summary>Someone picked something up (from the ground, a container or a body).</summary>
    public event Action<Actor, Item>? ItemPickedUp;

    /// <summary>Someone put something down where they stand.</summary>
    public event Action<Actor, Item>? ItemDropped;

    /// <summary>Someone put something into a container (the second item).</summary>
    public event Action<Actor, Item, Item>? ItemStowed;

    /// <summary>Someone opened (true) or shut (false) a container.</summary>
    public event Action<Actor, Item, bool>? ContainerOpened;

    /// <summary>
    /// Takes a ledger made before the run (the island's loot, say). Only before any item is made, so
    /// that ids stay unique.
    /// </summary>
    public void UseItems(ItemLedger ledger)
    {
        if (_items.Made > 0)
            throw new InvalidOperationException("Items have already been made in this run; take the ledger before adding actors or items.");
        _items = ledger;
    }

    // ---------- setup ----------

    /// <summary>Makes an item (setup, tests, loot), fitted with what it comes with unless not <paramref name="fitted"/>. It lies nowhere until laid, given or stowed.</summary>
    public Item CreateItem(ItemKind kind, int? amount = null, string? tag = null, int level = 0, bool fitted = true) =>
        _items.Create(kind, amount, tag, level, fitted);

    /// <summary>Lays an item on a tile of the map (on its deck if <paramref name="onDeck"/>).</summary>
    public void PlaceItem(Item item, TilePos at, bool onDeck = false)
    {
        if (!Map.InBounds(at))
            throw new ArgumentOutOfRangeException(nameof(at), $"{item} would lie off the map at {at}.");
        var (x, y) = ToIsland(at);
        _items.Lay(item, x, y, onDeck && Map.Structures.HasDeck(at));
    }

    /// <summary>Puts an item into a container directly (setup). Throws if it won't go in.</summary>
    public void StowItem(Item container, Item item)
    {
        if (container.RefusesToHold(item) is { } why)
            throw new InvalidOperationException($"Can't put {item} in {container}: {why}.");
        container.Insert(item);
    }

    /// <summary>Gives an actor an item, carried where the inventory has room (setup). False if there's none.</summary>
    public bool GiveItem(Actor actor, Item item)
    {
        var placement = actor.Inventory.PlaceFor(item);
        if (!placement.Ok)
            return false;
        actor.Inventory.Add(item, placement);
        return true;
    }

    /// <summary>
    /// Lays out items as a map file or loot plan says: each on the tile, or inside the last container
    /// laid before it (a '>' in the spec). Containers can start locked or open.
    /// </summary>
    public IReadOnlyList<Item> PlaceItems(IEnumerable<ItemSpec> specs, TilePos at, bool onDeck = false)
    {
        var made = new List<Item>();
        Item? lastContainer = null;
        foreach (var spec in specs)
        {
            var def = ItemCatalog.Of(spec.Kind);
            int copies = def.Stacks ? 1 : spec.Count;
            for (int i = 0; i < copies; i++)
            {
                var item = CreateItem(spec.Kind, def.Stacks ? spec.Count : spec.Empty ? 0 : spec.Amount, spec.Tag, spec.Level, fitted: !spec.Empty);
                item.Locked = spec.Locked && def.Holds is { Closes: true };
                item.IsOpen = spec.Open && !item.Locked && def.Holds is { Closes: true };
                if (spec.Inside)
                {
                    if (lastContainer is null)
                        throw new FormatException($"Nothing to put the {def.Name} in at {at}.");
                    StowItem(lastContainer, item);
                }
                else
                {
                    PlaceItem(item, at, onDeck);
                    if (item.IsContainer)
                        lastContainer = item;
                }
                made.Add(item);
            }
        }
        return made;
    }

    /// <summary>Gives an actor their loadout's kit: worn things first, so that bags are on before the rest goes in.</summary>
    private void Equip(Actor actor, Loadout loadout)
    {
        foreach (var (kind, count) in loadout.Items.OrderBy(k => ItemCatalog.Of(k.Kind).Wear == WearSlot.None ? 1 : 0))
        {
            var def = ItemCatalog.Of(kind);
            for (int left = count; left > 0;)
            {
                int amount = def.Stacks ? Math.Min(left, def.MaxAmount) : def.StartAmount;
                left -= def.Stacks ? amount : 1;
                var item = CreateItem(kind, amount);
                if (!GiveItem(actor, item))
                    throw new InvalidOperationException($"{actor.Name} has no room for {item.Describe()} ({loadout.Name}).");
            }
        }
    }

    // ---------- where things are ----------

    private TilePos ToTile(WorldItem lying) => FromIsland(lying.X, lying.Y);

    /// <summary>What lies on a tile of the map (both levels), oldest first.</summary>
    public IReadOnlyList<WorldItem> ItemsAt(TilePos p)
    {
        var (x, y) = ToIsland(p);
        return _items.At(x, y);
    }

    /// <summary>Everything lying on the map window, with its tile.</summary>
    public IEnumerable<(WorldItem Lying, TilePos At)> ItemsOnMap()
    {
        foreach (var lying in _items.All)
        {
            var at = ToTile(lying);
            if (Map.InBounds(at))
                yield return (lying, at);
        }
    }

    /// <summary>Where an item lies on the map, if it's lying on the map at the top level.</summary>
    public TilePos? WhereIs(Item item) =>
        _items.Find(item.Id) is { } lying && ToTile(lying) is var at && Map.InBounds(at) ? at : null;

    /// <summary>Whether a species can handle things: people can, animals can't.</summary>
    private static bool HasHands(Actor actor) => actor.Species.OpensDoors && !actor.IsLarge;

    /// <summary>
    /// Everything an actor could take or open now: what lies on their tile and the next ones at their
    /// level (and what's inside open containers there), and what the dead next to them carry.
    /// </summary>
    public IReadOnlyList<ReachableItem> ItemsWithinReach(Actor actor)
    {
        var list = new List<ReachableItem>();
        if (actor.IsGone || !HasHands(actor))
            return list;
        var here = actor.Position;
        for (int dy = -1; dy <= 1; dy++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                var p = new TilePos(here.X + dx, here.Y + dy);
                if (!Map.InBounds(p))
                    continue;
                foreach (var lying in ItemsAt(p))
                {
                    if (lying.OnDeck != actor.OnDeck)
                        continue;
                    list.Add(new ReachableItem(lying.Item, p, lying.OnDeck));
                    AddInside(list, lying.Item, p, lying.OnDeck);
                }
            }
        }
        foreach (var body in _actors)
        {
            if (!body.IsDead || body.LeftBehind || body == actor || body.OnDeck != actor.OnDeck || Chebyshev(body.Position, here) > 1)
                continue;
            foreach (var item in body.Inventory.All)
                list.Add(new ReachableItem(item, body.Position, body.OnDeck, body.Inventory.HolderOf(item), body));
        }
        return list;
    }

    private static void AddInside(List<ReachableItem> list, Item container, TilePos at, bool onDeck)
    {
        if (!container.IsAccessible)
            return;
        foreach (var inner in container.Contents)
        {
            list.Add(new ReachableItem(inner, at, onDeck, container));
            AddInside(list, inner, at, onDeck);
        }
    }

    // ---------- actions ----------

    private int Refuse(Actor actor, string reason)
    {
        MoveBlocked?.Invoke(actor, reason);
        return RefuseTicks;
    }

    private int PickUp(Actor actor, int itemId)
    {
        if (!HasHands(actor))
            return Bump(actor, "it can't carry things");
        var reach = ItemsWithinReach(actor).FirstOrDefault(r => r.Item.Id == itemId);
        if (reach is null)
            return Refuse(actor, "you can't reach that from here");
        var item = reach.Item;
        var placement = actor.Inventory.PlaceFor(item);
        if (!placement.Ok)
            return Refuse(actor, placement.Refused!);

        if (reach.On is { } body)
            body.Inventory.Remove(item);
        else if (reach.In is { } holder)
            holder.Extract(item);
        else
            _items.Lift(item);
        bool intoBag = placement.Into is { IsContainer: true };
        int grams = item.TotalGrams;
        actor.Inventory.Add(item, placement);

        if (reach.At != actor.Position)
            actor.Facing = Directions.FromDelta(reach.At.X - actor.Position.X, reach.At.Y - actor.Position.Y);
        EmitNoise(actor.Position, ItemRules.PickUpLoudness, NoiseKinds.Rummaging, actor.Id);
        ItemPickedUp?.Invoke(actor, item);
        return ItemRules.PickUpTicks
            + (grams > ItemRules.HeavyItemGrams ? ItemRules.HeavyPickUpTicks : 0)
            + (intoBag ? ItemRules.StowTicks : 0)
            + (reach.On is not null ? ItemRules.FromBodyTicks : 0);
    }

    private int Drop(Actor actor, int itemId)
    {
        if (actor.Inventory.Find(itemId) is not { } item)
            return Refuse(actor, "you don't have that");
        actor.Inventory.Remove(item);
        PlaceItem(item, actor.Position, actor.OnDeck);
        LetGoOf(actor, item);
        EmitNoise(actor.Position, ItemRules.DropLoudness, NoiseKinds.Thump, actor.Id);
        ItemDropped?.Invoke(actor, item);
        return ItemRules.DropTicks;
    }

    private int Put(Actor actor, int itemId, int containerId)
    {
        if (actor.Inventory.Find(itemId) is not { } item)
            return Refuse(actor, "you don't have that");
        var container = actor.Inventory.Find(containerId);
        bool carried = container is not null;
        container ??= ItemsWithinReach(actor).FirstOrDefault(r => r.Item.Id == containerId && r.On is null)?.Item;
        if (container is null)
            return Refuse(actor, "you can't reach that from here");
        if (!container.IsContainer)
            return Refuse(actor, $"the {container.Def.Name} can't hold anything");
        if (!container.IsAccessible)
            return Refuse(actor, $"the {container.Def.Name} is shut");
        if (container.RefusesToHold(item) is { } why)
            return Refuse(actor, why);

        actor.Inventory.Remove(item);
        container.Insert(item);
        if (!carried)
            LetGoOf(actor, item);
        EmitNoise(actor.Position, ItemRules.PickUpLoudness, NoiseKinds.Rummaging, actor.Id);
        ItemStowed?.Invoke(actor, item, container);
        return ItemRules.PutTicks;
    }

    private int OpenOrShut(Actor actor, int itemId)
    {
        if (!HasHands(actor))
            return Bump(actor, "it can't open things");
        var reach = ItemsWithinReach(actor).FirstOrDefault(r => r.Item.Id == itemId && r.On is null);
        if (reach is null)
            return Refuse(actor, "you can't reach that from here");
        var container = reach.Item;
        if (container.Def.Holds is not { Closes: true } holds)
            return Refuse(actor, $"the {container.Def.Name} has nothing to open");
        int extra = 0;
        if (!container.IsOpen && container.Locked)
        {
            if (KeyFor(actor, container) is not { } key)
                return Refuse(actor, actor.Inventory.Has(ItemKind.Crowbar)
                    ? $"the {container.Def.Name} is locked (you could force it)" : $"the {container.Def.Name} is locked");
            container.Locked = false;
            extra = ItemRules.UnlockTicks;
            ContainerUnlocked?.Invoke(actor, container, key);
        }

        container.IsOpen = !container.IsOpen;
        if (holds.OpenLoudness > 0)
            EmitNoise(reach.At, holds.OpenLoudness, NoiseKinds.Opening, actor.Id);
        ContainerOpened?.Invoke(actor, container, container.IsOpen);
        return Math.Max(RefuseTicks, container.IsOpen ? holds.OpenTicks : holds.OpenTicks / 2) + extra;
    }

    /// <summary>After something left an actor's hands: a flashlight that was their last one goes out with it.</summary>
    private void LetGoOf(Actor actor, Item item)
    {
        if (actor.CarriedLight is { } light && _lights[light].On
            && item.SelfAndNested().Any(i => i.Kind == ItemKind.Flashlight) && !actor.Inventory.Has(ItemKind.Flashlight))
            SetLightOn(light, false);
    }

    // ---------- the weight of it ----------

    /// <summary>Why a load stops someone climbing (a step up, a ladder, a rope), or null if it doesn't.</summary>
    private static string? LoadStopsClimbing(Actor actor) =>
        actor.Inventory.Load == Load.Heavy ? "too heavily laden to climb"
        : actor.Inventory.FreeHands == 0 ? "your hands are full"
        : null;

    /// <summary>The fingerprint of every item: lying (in id order) and carried (in actor order), contents and all.</summary>
    private void AddItemsTo(StateHasher hasher)
    {
        hasher.Add(_items.Made).Add(_items.Count);
        foreach (var lying in _items.All)
        {
            hasher.Add(lying.X).Add(lying.Y).Add(lying.OnDeck ? 1 : 0);
            AddItem(hasher, lying.Item);
        }
        foreach (var actor in _actors)
        {
            hasher.Add(actor.LightDrainTick).Add(actor.Inventory.Carried.Count);
            foreach (var (item, slot) in actor.Inventory.Carried)
            {
                hasher.Add((int)slot);
                AddItem(hasher, item);
            }
        }
    }

    private static void AddItem(StateHasher hasher, Item item)
    {
        hasher.Add(item.Id).Add((int)item.Kind).Add(item.Amount).Add(item.IsOpen ? 1 : 0).Add(item.Locked ? 1 : 0)
            .Add(item.Level).Add(item.Tag?.Length ?? -1).Add(item.Contents.Count).Add(item.Unsafe ? 1 : 0);
        foreach (var inner in item.Contents)
            AddItem(hasher, inner);
    }
}
