namespace BlueHarvest.Sim.Actors;

/// <summary>Every kind of item. Append only (saves, map files and the state hash rely on the order).</summary>
public enum ItemKind : byte
{
    FirstAidKit,
    Flare,
    Flashlight,

    /// <summary>Rope and harness, 20 m. Used up by a rappel: it stays tied at the top.</summary>
    Rope,

    // step 20a: containers you carry
    Daypack,
    FieldPack,
    BeltPouch,
    HardCase,

    // step 20a: containers that belong to the room
    Locker,
    Crate,
    Desk,
    Fridge,
    Cabinet,
    MedicineCabinet,

    // step 20a: a first few of each kind of thing
    WaterBottle,
    EnergyBar,
    CannedFood,
    Bandage,
    Painkillers,
    Knife,
    Crowbar,
    BatteryD,
    Notebook,
}

/// <summary>Numbers for carrying. Weights in grams, volumes in millilitres. First guesses, to tune.</summary>
public static class ItemRules
{
    /// <summary>Pockets hold this much in all, and nothing bigger than <see cref="PocketItemMl"/> each.</summary>
    public const int PocketMl = 2000, PocketItemMl = 500;

    /// <summary>Up to this you carry freely; above it, sprinting costs more and climbing is slower.</summary>
    public const int BurdenedGrams = 10_000;

    /// <summary>Above this: no sprinting, slower walking, no climbing.</summary>
    public const int HeavyGrams = 20_000;

    /// <summary>Nobody carries more than this: you can't pick up what would take you over it.</summary>
    public const int MaxCarryGrams = 35_000;

    /// <summary>Burdened: a sprint step costs this much more (120% of the usual sprint).</summary>
    public const int BurdenedSprintPercent = 120;

    /// <summary>Burdened: a climb step costs this much more.</summary>
    public const int BurdenedClimbPercent = 125;

    /// <summary>Heavily laden: a walking step costs this much more.</summary>
    public const int HeavyWalkPercent = 115;

    /// <summary>Picking something up: 1 s, a second more if it's over 5 kg and another to stow it in a bag; searching a body takes longer.</summary>
    public const int PickUpTicks = 100, HeavyItemGrams = 5_000, HeavyPickUpTicks = 100, StowTicks = 100, FromBodyTicks = 50;

    public const int DropTicks = 50, PutTicks = 80;

    /// <summary>Handling things: the rustle of picking up or stowing, the thump of putting down.</summary>
    public const int PickUpLoudness = 8, DropLoudness = 5;

    /// <summary>How heavily laden a weight makes you.</summary>
    public static Load LoadOf(int grams) =>
        grams > HeavyGrams ? Load.Heavy : grams > BurdenedGrams ? Load.Burdened : Load.Light;
}

/// <summary>How laden someone is (see <see cref="ItemRules"/>).</summary>
public enum Load : byte { Light, Burdened, Heavy }

/// <summary>One item, or a run of the same, to place: what map files and loot plans say.</summary>
/// <param name="Count">How many: one stack of that many for stacking kinds, that many separate items otherwise.</param>
/// <param name="Amount">How full (non-stacking kinds); the kind's starting amount if left out.</param>
/// <param name="Inside">Goes into the container placed just before it on the same tile.</param>
public sealed record ItemSpec(ItemKind Kind, int Count = 1, int? Amount = null, string? Tag = null, int Level = 0,
    bool Locked = false, bool Open = false, bool Inside = false);

/// <summary>What every kind of item is, by kind and by id ("water-bottle").</summary>
public static class ItemCatalog
{
    private const ItemCategory Bag = ItemCategory.Wearable | ItemCategory.Container;
    private const ItemCategory Furniture = ItemCategory.Fixture | ItemCategory.Container;

    private static readonly ItemDef[] Defs =
    {
        new(ItemKind.FirstAidKit, "first-aid-kit", "first-aid kit", "first-aid kits", '+',
            ItemCategory.Medical | ItemCategory.Usable, ItemSize.Hand, Grams: 600, Ml: 1500, MaxAmount: 4, Stacks: true,
            Note: "One treats a wound."),
        new(ItemKind.Flare, "flare", "flare", "flares", '!',
            ItemCategory.Light | ItemCategory.Throwable | ItemCategory.Usable, ItemSize.Pocket, Grams: 200, Ml: 300, MaxAmount: 12, Stacks: true,
            Note: "Burns red for 90 s where it lands."),
        new(ItemKind.Flashlight, "flashlight", "flashlight", "flashlights", '/',
            ItemCategory.Light | ItemCategory.Usable, ItemSize.Pocket, Grams: 400, Ml: 400),
        new(ItemKind.Rope, "rope", "rope and harness", "ropes", '&',
            ItemCategory.Tool | ItemCategory.Usable, ItemSize.Hand, Grams: 2500, Ml: 5000,
            Note: "20 m. Stays tied where you rope down."),

        new(ItemKind.Daypack, "daypack", "daypack", "daypacks", '[',
            Bag, ItemSize.Hand, Grams: 1000, Ml: 2500, Wear: WearSlot.Back,
            Holds: new ContainerSpec(CapacityMl: 25_000, MaxItemMl: 12_000)),
        new(ItemKind.FieldPack, "field-pack", "field pack", "field packs", '[',
            Bag, ItemSize.Hand, Grams: 1800, Ml: 3500, Wear: WearSlot.Back,
            Holds: new ContainerSpec(CapacityMl: 45_000, MaxItemMl: 20_000)),
        new(ItemKind.BeltPouch, "belt-pouch", "belt pouch", "belt pouches", '(',
            Bag, ItemSize.Pocket, Grams: 150, Ml: 300, Wear: WearSlot.Waist,
            Holds: new ContainerSpec(CapacityMl: 1500, MaxItemMl: 600)),
        new(ItemKind.HardCase, "hard-case", "hard case", "hard cases", '=',
            ItemCategory.Container, ItemSize.Hand, Grams: 1500, Ml: 9000,
            Holds: new ContainerSpec(CapacityMl: 7000, MaxItemMl: 5000)),

        new(ItemKind.Locker, "locker", "locker", "lockers", 'L',
            Furniture, ItemSize.Fixed, Grams: 30_000, Ml: 400_000,
            Holds: new ContainerSpec(CapacityMl: 150_000, Closes: true, OpenLoudness: 14, OpenTicks: 100)),
        new(ItemKind.Crate, "crate", "crate", "crates", 'X',
            ItemCategory.Container, ItemSize.TwoHanded, Grams: 8000, Ml: 80_000,
            Holds: new ContainerSpec(CapacityMl: 60_000, Closes: true, OpenLoudness: 12, OpenTicks: 150)),
        new(ItemKind.Desk, "desk", "desk", "desks", 'D',
            Furniture, ItemSize.Fixed, Grams: 40_000, Ml: 600_000,
            Holds: new ContainerSpec(CapacityMl: 30_000, MaxItemMl: 8000, Closes: true, OpenLoudness: 6, OpenTicks: 80)),
        new(ItemKind.Fridge, "fridge", "fridge", "fridges", 'F',
            Furniture, ItemSize.Fixed, Grams: 60_000, Ml: 500_000,
            Holds: new ContainerSpec(CapacityMl: 90_000, Closes: true, OpenLoudness: 6, OpenTicks: 60)),
        new(ItemKind.Cabinet, "cabinet", "cabinet", "cabinets", 'C',
            Furniture, ItemSize.Fixed, Grams: 25_000, Ml: 300_000,
            Holds: new ContainerSpec(CapacityMl: 60_000, Closes: true, OpenLoudness: 8, OpenTicks: 80)),
        new(ItemKind.MedicineCabinet, "medicine-cabinet", "medicine cabinet", "medicine cabinets", 'M',
            Furniture, ItemSize.Fixed, Grams: 6000, Ml: 40_000,
            Holds: new ContainerSpec(CapacityMl: 8000, MaxItemMl: 2000, Closes: true, OpenLoudness: 4, OpenTicks: 40)),

        new(ItemKind.WaterBottle, "water-bottle", "water bottle", "water bottles", 'b',
            ItemCategory.Potable, ItemSize.Hand, Grams: 40, Ml: 1100, MaxAmount: 1000, StartAmount: 1000,
            MilligramsPerUnit: 1000, Unit: "ml"),
        new(ItemKind.EnergyBar, "energy-bar", "energy bar", "energy bars", '%',
            ItemCategory.Edible, ItemSize.Pocket, Grams: 60, Ml: 80, MaxAmount: 12, Stacks: true),
        new(ItemKind.CannedFood, "canned-food", "can of food", "cans of food", '%',
            ItemCategory.Edible, ItemSize.Pocket, Grams: 450, Ml: 450, MaxAmount: 6, Stacks: true),
        new(ItemKind.Bandage, "bandage", "bandage", "bandages", '+',
            ItemCategory.Medical | ItemCategory.Usable, ItemSize.Pocket, Grams: 30, Ml: 60, MaxAmount: 10, Stacks: true),
        new(ItemKind.Painkillers, "painkillers", "bottle of painkillers", "bottles of painkillers", '+',
            ItemCategory.Medical | ItemCategory.Usable | ItemCategory.Edible, ItemSize.Pocket, Grams: 40, Ml: 50,
            MaxAmount: 20, StartAmount: 20, MilligramsPerUnit: 500, Unit: "pills"),
        new(ItemKind.Knife, "knife", "knife", "knives", '-',
            ItemCategory.Tool | ItemCategory.Weapon, ItemSize.Pocket, Grams: 150, Ml: 150),
        new(ItemKind.Crowbar, "crowbar", "crowbar", "crowbars", '\\',
            ItemCategory.Tool | ItemCategory.Weapon, ItemSize.Hand, Grams: 2000, Ml: 1500),
        new(ItemKind.BatteryD, "battery-d", "D battery", "D batteries", '|',
            ItemCategory.Power, ItemSize.Pocket, Grams: 140, Ml: 60, MaxAmount: 8, Stacks: true),
        new(ItemKind.Notebook, "notebook", "notebook", "notebooks", '"',
            ItemCategory.Document | ItemCategory.Usable, ItemSize.Pocket, Grams: 150, Ml: 300),
    };

    private static readonly Dictionary<string, ItemDef> ById = BuildIndex();

    private static Dictionary<string, ItemDef> BuildIndex()
    {
        var kinds = Enum.GetValues<ItemKind>();
        if (Defs.Length != kinds.Length)
            throw new InvalidOperationException($"The item catalog has {Defs.Length} defs for {kinds.Length} kinds.");
        var index = new Dictionary<string, ItemDef>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < Defs.Length; i++)
        {
            if ((int)Defs[i].Kind != i)
                throw new InvalidOperationException($"The item catalog's entry {i} is {Defs[i].Kind}, out of order.");
            index.Add(Defs[i].Id, Defs[i]);
        }
        return index;
    }

    public static ItemDef Of(ItemKind kind) => Defs[(int)kind];

    public static IReadOnlyList<ItemDef> All => Defs;

    /// <summary>The kind with this id ("water-bottle"), any case; null if there's none.</summary>
    public static ItemDef? Find(string id) => ById.TryGetValue(id, out var def) ? def : null;

    /// <summary>
    /// Reads an item spec: items separated by ';', each "[>]id [xN] [amount=N] [tag=T] [level=N] [locked] [open]".
    /// A leading '>' puts the item inside the container before it. "locker locked; >flashlight; >energy-bar x3".
    /// </summary>
    public static IReadOnlyList<ItemSpec> ParseSpec(string text)
    {
        var specs = new List<ItemSpec>();
        foreach (string rawPart in text.Split(';'))
        {
            string part = rawPart.Trim();
            if (part.Length == 0)
                continue;
            bool inside = part.StartsWith('>');
            if (inside)
                part = part[1..].TrimStart();
            string[] tokens = part.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 0)
                throw new FormatException($"An empty item in '{text}'.");
            var def = Find(tokens[0]) ?? throw new FormatException($"Unknown item '{tokens[0]}'.");
            var spec = new ItemSpec(def.Kind, Inside: inside);
            foreach (string token in tokens.Skip(1))
            {
                int eq = token.IndexOf('=');
                string key = eq < 0 ? token : token[..eq];
                string value = eq < 0 ? "" : token[(eq + 1)..];
                spec = key.ToLowerInvariant() switch
                {
                    "locked" => spec with { Locked = true },
                    "open" => spec with { Open = true },
                    "tag" when value.Length > 0 => spec with { Tag = value },
                    "amount" when int.TryParse(value, out int amount) && amount >= 0 && amount <= def.MaxAmount => spec with { Amount = amount },
                    "level" when int.TryParse(value, out int level) && level is >= 0 and <= 3 => spec with { Level = level },
                    _ when eq < 0 && token.Length > 1 && (token[0] is 'x' or 'X') && int.TryParse(token[1..], out int count) && count >= 1 && count <= 100
                        => spec with { Count = count },
                    _ => throw new FormatException($"Can't read '{token}' in item '{part}'."),
                };
            }
            if (spec.Inside && specs.Count == 0)
                throw new FormatException($"'{part}' goes inside something, but nothing comes before it.");
            specs.Add(spec);
        }
        return specs;
    }

    /// <summary>Writes specs back in the form <see cref="ParseSpec"/> reads.</summary>
    public static string FormatSpec(IEnumerable<ItemSpec> specs) => string.Join("; ", specs.Select(s =>
    {
        var parts = new List<string> { (s.Inside ? ">" : "") + Of(s.Kind).Id };
        if (s.Count != 1) parts.Add($"x{s.Count}");
        if (s.Amount is { } amount) parts.Add($"amount={amount}");
        if (s.Tag is { } tag) parts.Add($"tag={tag}");
        if (s.Level != 0) parts.Add($"level={s.Level}");
        if (s.Locked) parts.Add("locked");
        if (s.Open) parts.Add("open");
        return string.Join(' ', parts);
    }));
}
