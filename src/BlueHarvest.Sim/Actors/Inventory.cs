namespace BlueHarvest.Sim.Actors;

/// <summary>Where an item would go if picked up: a slot, or into a carried container; or why it can't.</summary>
public readonly record struct Placement(CarrySlot Slot, Item? Into, string? Refused)
{
    public bool Ok => Refused is null;

    public static Placement No(string why) => new(CarrySlot.Pocket, null, why);
}

/// <summary>
/// What someone carries: in their hands (two, or both for something big), in their pockets
/// (<see cref="ItemRules.PocketMl"/> of small things), and worn (a pack on the back, a pouch on the
/// belt), with containers holding the rest. Weight counts everything, contents and all.
/// <para>
/// The old count API (<see cref="Count"/>, <see cref="Has(ItemKind)"/>, <see cref="Use"/>) works
/// across all of it, so flares, kits, the flashlight and the rope behave as they always did.
/// </para>
/// </summary>
public sealed class Inventory
{
    private readonly List<(Item Item, CarrySlot Slot)> _carried = new();

    /// <summary>What's carried at the top level, with where: hands, pockets and worn. Containers' contents are inside them.</summary>
    public IReadOnlyList<(Item Item, CarrySlot Slot)> Carried => _carried;

    /// <summary>Everything carried, contents included, depth first in carried order.</summary>
    public IEnumerable<Item> All => _carried.SelectMany(c => c.Item.SelfAndNested());

    /// <summary>The weight of everything carried, grams.</summary>
    public int Grams => _carried.Sum(c => c.Item.TotalGrams);

    public Load Load => ItemRules.LoadOf(Grams);

    /// <summary>The room taken in pockets, ml.</summary>
    public int PocketMl => _carried.Where(c => c.Slot == CarrySlot.Pocket).Sum(c => c.Item.TotalMl);

    /// <summary>How many hands are free: 0, 1 or 2.</summary>
    public int FreeHands => 2 - _carried.Sum(c => c.Slot switch
    {
        CarrySlot.BothHands => 2,
        CarrySlot.LeftHand or CarrySlot.RightHand => 1,
        _ => 0,
    });

    /// <summary>The containers carried that you can put things in: worn first, then in hand, then pockets.</summary>
    public IEnumerable<Item> Containers => _carried
        .OrderBy(c => c.Slot == CarrySlot.Worn ? 0 : c.Slot == CarrySlot.Pocket ? 2 : 1)
        .SelectMany(c => c.Item.SelfAndNested())
        .Where(i => i.IsContainer);

    /// <summary>What's worn in a slot, or null.</summary>
    public Item? WornOn(WearSlot slot) =>
        _carried.FirstOrDefault(c => c.Slot == CarrySlot.Worn && c.Item.Def.Wear == slot).Item;

    // ---------- the count API ----------

    /// <summary>How many of a kind are carried: the sum of stacks for stacking kinds, the number of items otherwise.</summary>
    public int Count(ItemKind kind) => All.Where(i => i.Kind == kind).Sum(i => i.Def.Stacks ? i.Amount : 1);

    public bool Has(ItemKind kind) => All.Any(i => i.Kind == kind && (!i.Def.Stacks || i.Amount > 0));

    /// <summary>Whether a key of this kind with this tag is carried (a keycard for a facility).</summary>
    public bool Has(ItemKind kind, string tag) => All.Any(i => i.Kind == kind && i.Tag == tag);

    /// <summary>
    /// Uses up one of a kind: one from the first stack of it carried, or the item itself if it doesn't
    /// stack (the rope). False if there was none.
    /// </summary>
    internal bool Use(ItemKind kind)
    {
        var item = All.FirstOrDefault(i => i.Kind == kind && (!i.Def.Stacks || i.Amount > 0));
        if (item is null)
            return false;
        if (item.Def.Stacks && item.Amount > 1)
            item.Amount--;
        else
            Remove(item);
        return true;
    }

    // ---------- finding ----------

    /// <summary>The carried item with this id, at any depth; null if it isn't carried.</summary>
    public Item? Find(int id) => All.FirstOrDefault(i => i.Id == id);

    /// <summary>The carried container directly holding an item, or null if it's at the top level (or not carried).</summary>
    public Item? HolderOf(Item item)
    {
        foreach (var (top, _) in _carried)
            if (top.Holder(item) is { } holder)
                return holder;
        return null;
    }

    /// <summary>Where a top-level item is carried; null if it's inside something, or not carried.</summary>
    public CarrySlot? SlotOf(Item item)
    {
        foreach (var (top, slot) in _carried)
            if (top == item)
                return slot;
        return null;
    }

    // ---------- placing ----------

    /// <summary>
    /// Where an item would go if picked up now: worn if it's worn and that slot is free; small things
    /// in pockets; then into a carried bag; then a free hand (both for something big). Stacks merge
    /// with a stack of the same already carried. Refused if it's fixed in place, too heavy to add, or
    /// there's nowhere for it.
    /// </summary>
    public Placement PlaceFor(Item item)
    {
        var def = item.Def;
        if (def.Size == ItemSize.Fixed)
            return Placement.No($"the {def.Name} doesn't move");
        int grams = item.TotalGrams;
        if (grams > ItemRules.MaxCarryGrams)
            return Placement.No($"the {def.Name} is too heavy to lift");
        if (Grams + grams > ItemRules.MaxCarryGrams)
            return Placement.No("too much to carry: put something down first");

        // the same thing already carried: it joins the stack
        if (def.Stacks)
        {
            foreach (var (top, slot) in _carried)
                if (top.StacksWith(item) && (slot != CarrySlot.Pocket || PocketMl + item.TotalMl <= ItemRules.PocketMl))
                    return new Placement(slot, top, null);
            foreach (var bag in Containers)
                if (bag.MergeTarget(item) is not null && bag.RefusesToHold(item) is null)
                    return new Placement(CarrySlot.Worn, bag, null);
        }

        if (def.Wear != WearSlot.None && WornOn(def.Wear) is null)
            return new Placement(CarrySlot.Worn, null, null);

        if (def.Size == ItemSize.Pocket && def.Ml <= ItemRules.PocketItemMl && PocketMl + item.TotalMl <= ItemRules.PocketMl)
            return new Placement(CarrySlot.Pocket, null, null);

        if (def.Size != ItemSize.TwoHanded)
        {
            foreach (var bag in Containers)
                if (bag != item && bag.RefusesToHold(item) is null)
                    return new Placement(CarrySlot.Worn, bag, null);
        }

        int free = FreeHands;
        if (def.Size == ItemSize.TwoHanded)
            return free == 2 ? new Placement(CarrySlot.BothHands, null, null) : Placement.No($"you need both hands free for the {def.Name}");
        if (free > 0)
            return new Placement(HandFree(CarrySlot.RightHand) ? CarrySlot.RightHand : CarrySlot.LeftHand, null, null);
        return Placement.No("no room: your hands are full");
    }

    private bool HandFree(CarrySlot hand) => !_carried.Any(c => c.Slot == hand || c.Slot == CarrySlot.BothHands);

    /// <summary>Carries an item where <see cref="PlaceFor"/> said (it must have said yes).</summary>
    internal void Add(Item item, Placement placement)
    {
        if (!placement.Ok)
            throw new InvalidOperationException($"Can't carry {item}: {placement.Refused}.");
        if (placement.Into is { } into)
        {
            if (into.Def.Holds is null)
            {
                // a stack carried at the top level: join it
                into.Amount += item.Amount;
                return;
            }
            into.Insert(item);
            return;
        }
        _carried.Add((item, placement.Slot));
    }

    /// <summary>Takes an item out of wherever it is carried. False if it wasn't carried.</summary>
    internal bool Remove(Item item)
    {
        for (int i = 0; i < _carried.Count; i++)
        {
            if (_carried[i].Item == item)
            {
                _carried.RemoveAt(i);
                return true;
            }
            if (_carried[i].Item.Extract(item))
                return true;
        }
        return false;
    }
}
