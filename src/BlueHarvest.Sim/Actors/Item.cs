namespace BlueHarvest.Sim.Actors;

/// <summary>
/// What an item is good for. A kind can be several at once (a canteen is a container that holds a
/// potable fill; a flare is a light you throw). Append only: these are bits.
/// </summary>
[Flags]
public enum ItemCategory : uint
{
    None = 0,
    /// <summary>Does something when used: switched on, applied, read, operated.</summary>
    Usable = 1u << 0,
    /// <summary>Worn on the body (a pack on the back, a pouch on the belt, a vest, a helmet).</summary>
    Wearable = 1u << 1,
    /// <summary>Can be drunk.</summary>
    Potable = 1u << 2,
    /// <summary>Can be eaten.</summary>
    Edible = 1u << 3,
    /// <summary>Treats wounds or pain.</summary>
    Medical = 1u << 4,
    /// <summary>A tool: for repairs, prying, cutting.</summary>
    Tool = 1u << 5,
    /// <summary>Gives light.</summary>
    Light = 1u << 6,
    Weapon = 1u << 7,
    /// <summary>Rounds, shells, magazines: goes into a weapon.</summary>
    Ammo = 1u << 8,
    /// <summary>Batteries and cells: goes into something that needs power.</summary>
    Power = 1u << 9,
    Fuel = 1u << 10,
    /// <summary>Opens something: keys, keycards.</summary>
    Key = 1u << 11,
    /// <summary>Something to read: notes, logs, files ("old data").</summary>
    Document = 1u << 12,
    /// <summary>Holds other items.</summary>
    Container = 1u << 13,
    /// <summary>Can be thrown.</summary>
    Throwable = 1u << 14,
    /// <summary>Part of the room, not something you carry off: lockers, desks, fridges.</summary>
    Fixture = 1u << 15,
    /// <summary>A part for fixing or making something.</summary>
    Part = 1u << 16,
}

/// <summary>Where an item can go when carried. Append only.</summary>
public enum ItemSize : byte
{
    /// <summary>Fits a pocket (up to <see cref="ItemRules.PocketItemMl"/>), a hand or a container.</summary>
    Pocket,
    /// <summary>Takes one hand, or goes in a container.</summary>
    Hand,
    /// <summary>Takes both hands; only a container big enough (a crate, a locker) holds it.</summary>
    TwoHanded,
    /// <summary>Doesn't move: a fixture of the room.</summary>
    Fixed,
}

/// <summary>Where something is worn. Append only.</summary>
public enum WearSlot : byte { None, Back, Waist, Body, Head, Hands, Feet }

/// <summary>Where a carried item sits at the top level (inside a container, it sits in that container). Append only.</summary>
public enum CarrySlot : byte { RightHand, LeftHand, BothHands, Pocket, Worn }

/// <summary>
/// What a container holds: up to <paramref name="CapacityMl"/> of things, none bigger than
/// <paramref name="MaxItemMl"/>, at most <paramref name="MaxItems"/> of them, and only things of
/// <paramref name="Accepts"/> (any category if None). <paramref name="Closes"/> if it has a door or
/// lid that must be opened before you can see in or reach in (lockers, crates, fridges); bags and
/// trays don't. Opening it makes <paramref name="OpenLoudness"/> of noise and takes
/// <paramref name="OpenTicks"/>. <paramref name="Only"/>, if given, is the only kinds it takes: a
/// flashlight's battery slot, a pistol's magazine well.
/// </summary>
public sealed record ContainerSpec(
    int CapacityMl,
    int MaxItemMl = int.MaxValue,
    int MaxItems = int.MaxValue,
    ItemCategory Accepts = ItemCategory.None,
    bool Closes = false,
    int OpenLoudness = 0,
    int OpenTicks = 0,
    IReadOnlyList<ItemKind>? Only = null);

/// <summary>
/// What a kind of item is: data, like Species. Weight in grams, volume in millilitres, all integers.
/// <para>
/// <paramref name="Stacks"/>: <c>Amount</c> is a count of identical things (3 flares), up to
/// <paramref name="MaxAmount"/>; weight and volume are per thing. Otherwise <c>Amount</c> is how
/// full or charged it is (ml of water, uses of a lighter, rounds in a magazine), 0 to
/// <paramref name="MaxAmount"/>, in <paramref name="Unit"/>; each unit adds
/// <paramref name="MilligramsPerUnit"/> to the weight and nothing to the volume.
/// </para>
/// <para>
/// <paramref name="Fills"/>: the stacking kind its fill is made of (a magazine fills with 9 mm
/// rounds); loading moves rounds from a stack into it. <paramref name="Comes"/>: what a new one
/// comes fitted with (a flashlight's two D batteries), unless made empty. <paramref name="Dose"/>:
/// how much one use takes (250 ml of water, 2 pills); <paramref name="UseTicks"/>: how long.
/// </para>
/// </summary>
public sealed record ItemDef(
    ItemKind Kind,
    string Id,
    string Name,
    string Plural,
    char Glyph,
    ItemCategory Categories,
    ItemSize Size,
    int Grams,
    int Ml,
    int MaxAmount = 1,
    int StartAmount = 1,
    bool Stacks = false,
    int MilligramsPerUnit = 0,
    string? Unit = null,
    WearSlot Wear = WearSlot.None,
    ContainerSpec? Holds = null,
    string? Note = null,
    ItemKind? Fills = null,
    IReadOnlyList<(ItemKind Kind, int Count)>? Comes = null,
    int Dose = 1,
    int UseTicks = 0)
{
    public bool Is(ItemCategory category) => (Categories & category) != 0;

    /// <summary>"a flashlight", "an energy bar".</summary>
    public string WithArticle => (Name.Length > 0 && "aeiou".Contains(char.ToLowerInvariant(Name[0])) ? "an " : "a ") + Name;
}

/// <summary>
/// One item in the run: its kind, how much of it (see <see cref="ItemDef"/>), and for containers
/// what's in them. It is in exactly one place: lying in the world, carried, or inside a container.
/// </summary>
public sealed class Item
{
    private List<Item>? _contents;

    internal Item(int id, ItemKind kind, int amount)
    {
        Id = id;
        Kind = kind;
        Amount = amount;
    }

    /// <summary>Unique in the run; given out in the order items are made, so the same every run of a seed.</summary>
    public int Id { get; }

    public ItemKind Kind { get; }

    public ItemDef Def => ItemCatalog.Of(Kind);

    /// <summary>How many (stacking kinds) or how full (the rest), in the def's terms.</summary>
    public int Amount { get; internal set; }

    /// <summary>What it's for: a keycard's facility, a key's lock. Null for most things.</summary>
    public string? Tag { get; internal set; }

    /// <summary>A keycard's access level (1–3); 0 for everything else.</summary>
    public int Level { get; internal set; }

    /// <summary>A container that won't open until unlocked.</summary>
    public bool Locked { get; internal set; }

    /// <summary>A container with a door or lid (<see cref="ContainerSpec.Closes"/>) that stands open.</summary>
    public bool IsOpen { get; internal set; }

    /// <summary>Water from a stream or pool, not yet treated with a tablet: it may make you ill.</summary>
    public bool Unsafe { get; internal set; }

    /// <summary>Giving light now: a headlamp or lantern switched on, a cracked glow stick, a burning torch.</summary>
    public bool Lit { get; internal set; }

    /// <summary>When a glow stick or torch goes out (0: never lit). Once past, it's spent.</summary>
    public long LitUntilTick { get; internal set; }

    /// <summary>A light that has burnt or glowed itself out.</summary>
    public bool Spent => LitUntilTick > 0 && !Lit;

    /// <summary>Up to when a switched-on light's batteries have been run down.</summary>
    internal long DrainedTick { get; set; }

    /// <summary>What's inside, in the order it went in. Empty for anything that isn't a container.</summary>
    public IReadOnlyList<Item> Contents => (IReadOnlyList<Item>?)_contents ?? Array.Empty<Item>();

    public bool IsContainer => Def.Holds is not null;

    /// <summary>Whether you can see and reach inside: a container without a lid, or one standing open.</summary>
    public bool IsAccessible => Def.Holds is { } holds && (!holds.Closes || IsOpen);

    /// <summary>Its weight with everything in it, grams.</summary>
    public int TotalGrams
    {
        get
        {
            var def = Def;
            long grams = def.Stacks ? (long)def.Grams * Amount : def.Grams + (long)Amount * def.MilligramsPerUnit / 1000;
            if (_contents is not null)
                foreach (var inner in _contents)
                    grams += inner.TotalGrams;
            return (int)Math.Min(int.MaxValue, grams);
        }
    }

    /// <summary>The room it takes up, ml: its own size, or what's in it if that's more (a stuffed bag).</summary>
    public int TotalMl
    {
        get
        {
            var def = Def;
            int own = def.Stacks ? def.Ml * Amount : def.Ml;
            return _contents is null ? own : Math.Max(own, ContentsMl);
        }
    }

    /// <summary>The room taken up inside it, ml.</summary>
    public int ContentsMl => _contents?.Sum(i => i.TotalMl) ?? 0;

    /// <summary>This item and everything inside it, depth first.</summary>
    public IEnumerable<Item> SelfAndNested()
    {
        yield return this;
        if (_contents is null)
            yield break;
        foreach (var inner in _contents)
            foreach (var nested in inner.SelfAndNested())
                yield return nested;
    }

    /// <summary>Whether <paramref name="other"/> is this item or anywhere inside it.</summary>
    public bool Encloses(Item other) => SelfAndNested().Contains(other);

    /// <summary>
    /// Why <paramref name="item"/> won't go in this container now, or null if it will. Checks the
    /// container's own rules only; who is carrying what is for the inventory.
    /// </summary>
    public string? RefusesToHold(Item item)
    {
        if (Def.Holds is not { } holds)
            return $"the {Def.Name} can't hold anything";
        if (item.Encloses(this))
            return $"the {item.Def.Name} can't go inside itself";
        if (item.Def.Size == ItemSize.Fixed)
            return $"the {item.Def.Name} doesn't move";
        if (holds.Accepts != ItemCategory.None && (item.Def.Categories & holds.Accepts) == 0)
            return $"the {item.Def.Name} doesn't go in the {Def.Name}";
        if (holds.Only is { } only && !only.Contains(item.Kind))
            return $"the {item.Def.Name} doesn't fit the {Def.Name}";
        if (item.TotalMl > holds.MaxItemMl)
            return $"the {item.Def.Name} is too big for the {Def.Name}";
        if (Contents.Count >= holds.MaxItems && !CanMerge(item))
            return $"the {Def.Name} is full";
        if (ContentsMl + item.TotalMl > holds.CapacityMl)
            return $"no room in the {Def.Name}";
        return null;
    }

    /// <summary>Whether a stack of the same thing inside has room for all of <paramref name="item"/>.</summary>
    internal bool CanMerge(Item item) => MergeTarget(item) is not null;

    internal Item? MergeTarget(Item item) =>
        item.Def.Stacks ? _contents?.FirstOrDefault(i => i.StacksWith(item)) : null;

    /// <summary>Whether <paramref name="other"/> could join this stack: same kind and tag, and the room in it.</summary>
    internal bool StacksWith(Item other) =>
        other != this && Def.Stacks && other.Kind == Kind && other.Tag == Tag && Amount + other.Amount <= Def.MaxAmount
        && LitUntilTick == 0 && other.LitUntilTick == 0; // a lit or spent glow stick is one of its own

    /// <summary>Puts an item in (merging it into a stack of the same if there is one). Rules are checked by the caller.</summary>
    internal void Insert(Item item)
    {
        if (MergeTarget(item) is { } stack)
        {
            stack.Amount += item.Amount;
            return;
        }
        (_contents ??= new List<Item>()).Add(item);
    }

    /// <summary>Takes an item out of this container or any container inside it. True if it was there.</summary>
    internal bool Extract(Item item)
    {
        if (_contents is null)
            return false;
        if (_contents.Remove(item))
            return true;
        foreach (var inner in _contents)
            if (inner.Extract(item))
                return true;
        return false;
    }

    /// <summary>The container directly holding <paramref name="item"/> somewhere inside this one, or null.</summary>
    internal Item? Holder(Item item)
    {
        if (_contents is null)
            return null;
        if (_contents.Contains(item))
            return this;
        foreach (var inner in _contents)
            if (inner.Holder(item) is { } holder)
                return holder;
        return null;
    }

    /// <summary>
    /// "a flashlight (2 D batteries, 87%)", "3 flares", "a water bottle (750 ml)", "a 9 mm pistol
    /// (magazine, 12 rounds)", "a locker (locked)".
    /// </summary>
    public string Describe()
    {
        var def = Def;
        if (def.Stacks)
            return (Amount == 1 ? def.WithArticle : $"{Amount} {def.Plural}") + LightState();
        string text = def.WithArticle;
        if (def.Unit is { } unit)
            text += Amount == 0 ? " (empty)" : unit == "%" ? $" ({Amount}%)" : $" ({Amount} {unit})";
        if (def.Holds is { Only: not null })
            text += Loaded();
        if (Tag is { } tag && def.Is(ItemCategory.Key))
            text += Level > 0 ? $" ({tag}, level {Level})" : $" ({tag})";
        if (Locked)
            text += " (locked)";
        if (Unsafe && Amount > 0)
            text += " (untreated)";
        text += LightState();
        return text;
    }

    /// <summary>A light's state for its description: " (on)", " (glowing)", " (burning)", " (spent)", " (burnt out)", or nothing.</summary>
    private string LightState() =>
        Lit ? Kind switch { ItemKind.Torch => " (burning)", ItemKind.GlowStick => " (glowing)", _ => " (on)" }
        : Spent ? Kind == ItemKind.Torch ? " (burnt out)" : " (spent)"
        : "";

    /// <summary>What's fitted in a slot container, for its description: " (2 D batteries, 87%)", " (empty)".</summary>
    private string Loaded()
    {
        if (_contents is not { Count: > 0 })
            return " (empty)";
        var first = _contents[0];
        string what = _contents.Count == 1 ? first.Def.Name : $"{_contents.Count} {first.Def.Plural}";
        if (first.Def.Is(ItemCategory.Power))
            return $" ({what}, {_contents.Min(i => i.Amount)}%)";
        if (first.Def.Fills is not null && first.Def.Unit is { } unit)
            return $" ({first.Def.Name}, {first.Amount} {unit})";
        return $" ({what})";
    }

    /// <summary>Whether it's loaded and charged enough to work, for something powered: every slot filled, every battery with charge.</summary>
    public bool Charged => Def.Holds is { Only: not null } holds && Contents.Count >= holds.MaxItems
        && Contents.All(i => !i.Def.Is(ItemCategory.Power) || i.Amount > 0);

    public override string ToString() => $"#{Id} {Describe()}";
}
