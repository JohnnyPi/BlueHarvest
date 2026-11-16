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

    // step 20b: food
    Mre,
    Crackers,
    Jerky,
    DriedFruit,
    Chocolate,
    Noodles,
    CannedPeaches,
    Coconut,

    // drink
    Canteen,
    Soda,
    Thermos,
    WaterTablets,

    // medicine
    Antiseptic,
    Splint,
    SutureKit,
    Tourniquet,
    BurnGel,
    Antibiotics,
    EpiPen,

    // lights and power
    Headlamp,
    Lantern,
    GlowStick,
    BatteryAA,
    Matches,
    Lighter,

    // tools
    Multitool,
    BoltCutters,
    WireCutters,
    ToolKit,
    DuctTape,
    Binoculars,
    Compass,
    Radio,
    JerryCan,
    FireExtinguisher,
    SpareParts,

    // guns and ammunition (carried and loaded; firing comes with combat)
    Pistol,
    Magazine9mm,
    Rounds9mm,
    Shotgun,
    Shells12,
    Rifle,
    Rounds308,
    TranqRifle,
    TranqDart,
    FlareGun,
    FlareShell,
    Machete,
    Taser,

    // clothing
    Poncho,
    HardHat,
    WorkGloves,
    Boots,
    TacticalVest,
    NightVision,

    // documents and keys
    SecurityLog,
    ResearchFile,
    DataDrive,
    ParkMap,
    Keycard,
    Key,

    // parts and makings
    Fuse,
    WireSpool,
    ScrapMetal,
    CircuitBoard,
    Stick,
    Rag,
    Torch,
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

    /// <summary>A flashlight's batteries lose 1% every this many ticks while it's on: about ten game hours on a fresh pair.</summary>
    public const int LightTicksPerPercent = 3600;

    /// <summary>Loading: each round or shell pressed in.</summary>
    public const int LoadTicksPerRound = 40;

    /// <summary>Fitting a battery or a magazine; taking something out of what it's in.</summary>
    public const int FitTicks = 150, TakeOutTicks = 100;

    /// <summary>Forcing a lock with a crowbar: slow and loud.</summary>
    public const int PryTicks = 600, PryLoudness = 30;

    /// <summary>Unlocking with the right key or card.</summary>
    public const int UnlockTicks = 50;

    /// <summary>A radio's hiss when you listen.</summary>
    public const int RadioLoudness = 12;

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
/// <param name="Empty">Made empty: no fill, and nothing fitted (a flashlight without its batteries).</param>
public sealed record ItemSpec(ItemKind Kind, int Count = 1, int? Amount = null, string? Tag = null, int Level = 0,
    bool Locked = false, bool Open = false, bool Inside = false, bool Empty = false);

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
            ItemCategory.Light | ItemCategory.Usable | ItemCategory.Container, ItemSize.Pocket, Grams: 400, Ml: 400,
            Holds: new ContainerSpec(CapacityMl: 120, MaxItems: 2, Only: [ItemKind.BatteryD]), Comes: [(ItemKind.BatteryD, 2)],
            Note: "Takes two D batteries; about ten hours on a fresh pair."),
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
            MilligramsPerUnit: 1000, Unit: "ml", Dose: 250, UseTicks: 300),
        new(ItemKind.EnergyBar, "energy-bar", "energy bar", "energy bars", '%',
            ItemCategory.Edible, ItemSize.Pocket, Grams: 60, Ml: 80, MaxAmount: 12, Stacks: true, UseTicks: 400),
        new(ItemKind.CannedFood, "canned-food", "can of food", "cans of food", '%',
            ItemCategory.Edible, ItemSize.Pocket, Grams: 450, Ml: 450, MaxAmount: 6, Stacks: true, UseTicks: 800),
        new(ItemKind.Bandage, "bandage", "bandage", "bandages", '+',
            ItemCategory.Medical | ItemCategory.Usable, ItemSize.Pocket, Grams: 30, Ml: 60, MaxAmount: 10, Stacks: true, UseTicks: 600),
        new(ItemKind.Painkillers, "painkillers", "bottle of painkillers", "bottles of painkillers", '+',
            ItemCategory.Medical | ItemCategory.Usable | ItemCategory.Edible, ItemSize.Pocket, Grams: 40, Ml: 50,
            MaxAmount: 20, StartAmount: 20, MilligramsPerUnit: 500, Unit: "pills", Dose: 2, UseTicks: 200),
        new(ItemKind.Knife, "knife", "knife", "knives", '-',
            ItemCategory.Tool | ItemCategory.Weapon, ItemSize.Pocket, Grams: 150, Ml: 150),
        new(ItemKind.Crowbar, "crowbar", "crowbar", "crowbars", '\\',
            ItemCategory.Tool | ItemCategory.Weapon, ItemSize.Hand, Grams: 2000, Ml: 1500),
        new(ItemKind.BatteryD, "battery-d", "D battery", "D batteries", '|',
            ItemCategory.Power, ItemSize.Pocket, Grams: 140, Ml: 60, MaxAmount: 100, StartAmount: 100, Unit: "%"),
        new(ItemKind.Notebook, "notebook", "notebook", "notebooks", '"',
            ItemCategory.Document | ItemCategory.Usable, ItemSize.Pocket, Grams: 150, Ml: 300, UseTicks: 500,
            Note: "Someone's field notes: feeding times, a sketch of the east fence, 'DON'T TRUST THE MONORAIL TIMETABLE'."),

        // ---- food: one at a time ----
        new(ItemKind.Mre, "mre", "MRE", "MREs", '%', ItemCategory.Edible, ItemSize.Hand, Grams: 600, Ml: 700, MaxAmount: 4, Stacks: true, UseTicks: 2000),
        new(ItemKind.Crackers, "crackers", "pack of crackers", "packs of crackers", '%', ItemCategory.Edible, ItemSize.Pocket, Grams: 200, Ml: 300, MaxAmount: 6, Stacks: true, UseTicks: 600),
        new(ItemKind.Jerky, "jerky", "strip of jerky", "strips of jerky", '%', ItemCategory.Edible, ItemSize.Pocket, Grams: 40, Ml: 60, MaxAmount: 10, Stacks: true, UseTicks: 400),
        new(ItemKind.DriedFruit, "dried-fruit", "bag of dried fruit", "bags of dried fruit", '%', ItemCategory.Edible, ItemSize.Pocket, Grams: 150, Ml: 250, MaxAmount: 6, Stacks: true, UseTicks: 500),
        new(ItemKind.Chocolate, "chocolate", "chocolate bar", "chocolate bars", '%', ItemCategory.Edible, ItemSize.Pocket, Grams: 100, Ml: 100, MaxAmount: 10, Stacks: true, UseTicks: 300),
        new(ItemKind.Noodles, "noodles", "pack of instant noodles", "packs of instant noodles", '%', ItemCategory.Edible, ItemSize.Pocket, Grams: 90, Ml: 300, MaxAmount: 6, Stacks: true, UseTicks: 800),
        new(ItemKind.CannedPeaches, "canned-peaches", "can of peaches", "cans of peaches", '%', ItemCategory.Edible, ItemSize.Pocket, Grams: 450, Ml: 450, MaxAmount: 6, Stacks: true, UseTicks: 800),
        new(ItemKind.Coconut, "coconut", "coconut", "coconuts", '%', ItemCategory.Edible | ItemCategory.Potable, ItemSize.Hand, Grams: 1200, Ml: 1500, MaxAmount: 3, Stacks: true, UseTicks: 1500),

        // ---- drink: sips from a fill ----
        new(ItemKind.Canteen, "canteen", "canteen", "canteens", 'b', ItemCategory.Potable, ItemSize.Hand, Grams: 300, Ml: 1700,
            MaxAmount: 1500, StartAmount: 1500, MilligramsPerUnit: 1000, Unit: "ml", Dose: 250, UseTicks: 300),
        new(ItemKind.Soda, "soda", "can of soda", "cans of soda", 'b', ItemCategory.Potable, ItemSize.Pocket, Grams: 15, Ml: 350,
            MaxAmount: 330, StartAmount: 330, MilligramsPerUnit: 1000, Unit: "ml", Dose: 330, UseTicks: 300),
        new(ItemKind.Thermos, "thermos", "thermos of coffee", "thermoses of coffee", 'b', ItemCategory.Potable, ItemSize.Hand, Grams: 400, Ml: 700,
            MaxAmount: 500, StartAmount: 500, MilligramsPerUnit: 1000, Unit: "ml", Dose: 250, UseTicks: 300, Note: "Cold, ten years on."),
        new(ItemKind.WaterTablets, "water-tablets", "tube of water tablets", "tubes of water tablets", '+', ItemCategory.Usable, ItemSize.Pocket,
            Grams: 20, Ml: 40, MaxAmount: 20, StartAmount: 20, MilligramsPerUnit: 500, Unit: "tablets", UseTicks: 200, Note: "One makes a litre of water safe."),

        // ---- medicine ----
        new(ItemKind.Antiseptic, "antiseptic", "bottle of antiseptic", "bottles of antiseptic", '+', ItemCategory.Medical | ItemCategory.Usable, ItemSize.Pocket,
            Grams: 30, Ml: 150, MaxAmount: 10, StartAmount: 10, MilligramsPerUnit: 10_000, Unit: "uses", UseTicks: 400),
        new(ItemKind.Splint, "splint", "splint", "splints", '+', ItemCategory.Medical | ItemCategory.Usable, ItemSize.Hand, Grams: 300, Ml: 1200, MaxAmount: 3, Stacks: true, UseTicks: 1500),
        new(ItemKind.SutureKit, "suture-kit", "suture kit", "suture kits", '+', ItemCategory.Medical | ItemCategory.Usable, ItemSize.Pocket,
            Grams: 60, Ml: 200, MaxAmount: 5, StartAmount: 5, MilligramsPerUnit: 8000, Unit: "uses", UseTicks: 1500),
        new(ItemKind.Tourniquet, "tourniquet", "tourniquet", "tourniquets", '+', ItemCategory.Medical | ItemCategory.Usable, ItemSize.Pocket, Grams: 80, Ml: 120, MaxAmount: 4, Stacks: true, UseTicks: 400),
        new(ItemKind.BurnGel, "burn-gel", "tube of burn gel", "tubes of burn gel", '+', ItemCategory.Medical | ItemCategory.Usable, ItemSize.Pocket,
            Grams: 15, Ml: 80, MaxAmount: 5, StartAmount: 5, MilligramsPerUnit: 9000, Unit: "uses", UseTicks: 400),
        new(ItemKind.Antibiotics, "antibiotics", "bottle of antibiotics", "bottles of antibiotics", '+', ItemCategory.Medical | ItemCategory.Usable | ItemCategory.Edible, ItemSize.Pocket,
            Grams: 30, Ml: 50, MaxAmount: 14, StartAmount: 14, MilligramsPerUnit: 500, Unit: "pills", UseTicks: 200),
        new(ItemKind.EpiPen, "epipen", "adrenaline auto-injector", "adrenaline auto-injectors", '+', ItemCategory.Medical | ItemCategory.Usable, ItemSize.Pocket, Grams: 50, Ml: 60, MaxAmount: 3, Stacks: true, UseTicks: 100),

        // ---- lights and power ----
        new(ItemKind.Headlamp, "headlamp", "headlamp", "headlamps", '/', ItemCategory.Light | ItemCategory.Wearable | ItemCategory.Container, ItemSize.Pocket,
            Grams: 120, Ml: 200, Wear: WearSlot.Head, Holds: new ContainerSpec(CapacityMl: 40, MaxItems: 3, Only: [ItemKind.BatteryAA]), Comes: [(ItemKind.BatteryAA, 3)]),
        new(ItemKind.Lantern, "lantern", "lantern", "lanterns", '*', ItemCategory.Light | ItemCategory.Container, ItemSize.Hand,
            Grams: 900, Ml: 3000, Holds: new ContainerSpec(CapacityMl: 240, MaxItems: 4, Only: [ItemKind.BatteryD]), Comes: [(ItemKind.BatteryD, 4)]),
        new(ItemKind.GlowStick, "glow-stick", "glow stick", "glow sticks", '!', ItemCategory.Light | ItemCategory.Throwable, ItemSize.Pocket, Grams: 30, Ml: 40, MaxAmount: 10, Stacks: true),
        new(ItemKind.BatteryAA, "battery-aa", "AA battery", "AA batteries", '|', ItemCategory.Power, ItemSize.Pocket, Grams: 25, Ml: 10, MaxAmount: 100, StartAmount: 100, Unit: "%"),
        new(ItemKind.Matches, "matches", "box of matches", "boxes of matches", '-', ItemCategory.Tool | ItemCategory.Fuel, ItemSize.Pocket,
            Grams: 10, Ml: 30, MaxAmount: 40, StartAmount: 40, MilligramsPerUnit: 100, Unit: "matches"),
        new(ItemKind.Lighter, "lighter", "lighter", "lighters", '-', ItemCategory.Tool, ItemSize.Pocket, Grams: 30, Ml: 30, MaxAmount: 40, StartAmount: 40, Unit: "uses"),

        // ---- tools ----
        new(ItemKind.Multitool, "multitool", "multitool", "multitools", '-', ItemCategory.Tool, ItemSize.Pocket, Grams: 250, Ml: 100),
        new(ItemKind.BoltCutters, "bolt-cutters", "pair of bolt cutters", "pairs of bolt cutters", '\\', ItemCategory.Tool, ItemSize.Hand, Grams: 3500, Ml: 3000),
        new(ItemKind.WireCutters, "wire-cutters", "pair of wire cutters", "pairs of wire cutters", '-', ItemCategory.Tool, ItemSize.Pocket, Grams: 300, Ml: 150),
        new(ItemKind.ToolKit, "tool-kit", "tool kit", "tool kits", '=', ItemCategory.Tool, ItemSize.Hand, Grams: 4000, Ml: 6000, Note: "Spanners, drivers, a meter: repairs go better and faster."),
        new(ItemKind.DuctTape, "duct-tape", "roll of duct tape", "rolls of duct tape", 'o', ItemCategory.Tool | ItemCategory.Part, ItemSize.Pocket,
            Grams: 30, Ml: 400, MaxAmount: 20, StartAmount: 20, MilligramsPerUnit: 13_500, Unit: "uses"),
        new(ItemKind.Binoculars, "binoculars", "pair of binoculars", "pairs of binoculars", '8', ItemCategory.Tool, ItemSize.Hand, Grams: 700, Ml: 1200),
        new(ItemKind.Compass, "compass", "compass", "compasses", '-', ItemCategory.Tool | ItemCategory.Usable, ItemSize.Pocket, Grams: 60, Ml: 60, UseTicks: 100),
        new(ItemKind.Radio, "radio", "two-way radio", "two-way radios", '"', ItemCategory.Usable | ItemCategory.Container, ItemSize.Pocket, Grams: 350, Ml: 400,
            Holds: new ContainerSpec(CapacityMl: 50, MaxItems: 4, Only: [ItemKind.BatteryAA]), Comes: [(ItemKind.BatteryAA, 4)], UseTicks: 300),
        new(ItemKind.JerryCan, "jerry-can", "jerry can", "jerry cans", 'J', ItemCategory.Fuel, ItemSize.Hand, Grams: 1200, Ml: 22_000,
            MaxAmount: 20, StartAmount: 20, MilligramsPerUnit: 750_000, Unit: "L", Note: "Diesel. Fills a generator's day tank, or a fuse."),
        new(ItemKind.FireExtinguisher, "fire-extinguisher", "fire extinguisher", "fire extinguishers", '&', ItemCategory.Tool, ItemSize.Hand, Grams: 3000, Ml: 8000,
            MaxAmount: 6, StartAmount: 6, MilligramsPerUnit: 500_000, Unit: "charges"),
        new(ItemKind.SpareParts, "spare-parts", "set of spare parts", "sets of spare parts", '=', ItemCategory.Part, ItemSize.Hand, Grams: 5000, Ml: 4000, MaxAmount: 3, Stacks: true,
            Note: "Gaskets, a starter relay, injector seals: what a seized generator needs."),

        // ---- guns: carried and loaded only ----
        new(ItemKind.Pistol, "pistol", "9 mm pistol", "9 mm pistols", 'p', ItemCategory.Weapon | ItemCategory.Container, ItemSize.Hand, Grams: 800, Ml: 600,
            Holds: new ContainerSpec(CapacityMl: 200, MaxItems: 1, Only: [ItemKind.Magazine9mm])),
        new(ItemKind.Magazine9mm, "magazine-9mm", "9 mm magazine", "9 mm magazines", '\'', ItemCategory.Ammo, ItemSize.Pocket, Grams: 100, Ml: 80,
            MaxAmount: 15, StartAmount: 0, MilligramsPerUnit: 8000, Unit: "rounds", Fills: ItemKind.Rounds9mm),
        new(ItemKind.Rounds9mm, "rounds-9mm", "9 mm round", "9 mm rounds", ',', ItemCategory.Ammo, ItemSize.Pocket, Grams: 8, Ml: 2, MaxAmount: 50, Stacks: true),
        new(ItemKind.Shotgun, "shotgun", "pump shotgun", "pump shotguns", 'P', ItemCategory.Weapon, ItemSize.TwoHanded, Grams: 3200, Ml: 9000,
            MaxAmount: 6, StartAmount: 0, MilligramsPerUnit: 40_000, Unit: "shells", Fills: ItemKind.Shells12),
        new(ItemKind.Shells12, "shells-12", "12-gauge shell", "12-gauge shells", ',', ItemCategory.Ammo, ItemSize.Pocket, Grams: 40, Ml: 10, MaxAmount: 25, Stacks: true),
        new(ItemKind.Rifle, "rifle", "bolt-action rifle", "bolt-action rifles", 'R', ItemCategory.Weapon, ItemSize.TwoHanded, Grams: 3800, Ml: 10_000,
            MaxAmount: 5, StartAmount: 0, MilligramsPerUnit: 25_000, Unit: "rounds", Fills: ItemKind.Rounds308),
        new(ItemKind.Rounds308, "rounds-308", ".308 round", ".308 rounds", ',', ItemCategory.Ammo, ItemSize.Pocket, Grams: 25, Ml: 5, MaxAmount: 20, Stacks: true),
        new(ItemKind.TranqRifle, "tranq-rifle", "tranquilliser rifle", "tranquilliser rifles", 'R', ItemCategory.Weapon, ItemSize.TwoHanded, Grams: 3000, Ml: 10_000,
            MaxAmount: 1, StartAmount: 0, MilligramsPerUnit: 30_000, Unit: "dart", Fills: ItemKind.TranqDart, Note: "One dart at a time. Enough for a raptor; a Rex would need several."),
        new(ItemKind.TranqDart, "tranq-dart", "tranquilliser dart", "tranquilliser darts", ',', ItemCategory.Ammo, ItemSize.Pocket, Grams: 30, Ml: 30, MaxAmount: 10, Stacks: true),
        new(ItemKind.FlareGun, "flare-gun", "flare gun", "flare guns", 'p', ItemCategory.Weapon | ItemCategory.Light, ItemSize.Hand, Grams: 500, Ml: 600,
            MaxAmount: 1, StartAmount: 0, MilligramsPerUnit: 60_000, Unit: "shell", Fills: ItemKind.FlareShell),
        new(ItemKind.FlareShell, "flare-shell", "flare shell", "flare shells", ',', ItemCategory.Ammo | ItemCategory.Light, ItemSize.Pocket, Grams: 60, Ml: 60, MaxAmount: 10, Stacks: true),
        new(ItemKind.Machete, "machete", "machete", "machetes", '-', ItemCategory.Weapon | ItemCategory.Tool, ItemSize.Hand, Grams: 700, Ml: 900),
        new(ItemKind.Taser, "taser", "taser", "tasers", 'p', ItemCategory.Weapon | ItemCategory.Container, ItemSize.Pocket, Grams: 300, Ml: 300,
            Holds: new ContainerSpec(CapacityMl: 30, MaxItems: 2, Only: [ItemKind.BatteryAA]), Comes: [(ItemKind.BatteryAA, 2)]),

        // ---- clothing ----
        new(ItemKind.Poncho, "poncho", "rain poncho", "rain ponchos", '[', ItemCategory.Wearable, ItemSize.Pocket, Grams: 300, Ml: 400, Wear: WearSlot.Body),
        new(ItemKind.HardHat, "hard-hat", "hard hat", "hard hats", '^', ItemCategory.Wearable, ItemSize.Hand, Grams: 400, Ml: 3000, Wear: WearSlot.Head),
        new(ItemKind.WorkGloves, "work-gloves", "pair of work gloves", "pairs of work gloves", '[', ItemCategory.Wearable, ItemSize.Pocket, Grams: 200, Ml: 300, Wear: WearSlot.Hands),
        new(ItemKind.Boots, "boots", "pair of boots", "pairs of boots", '[', ItemCategory.Wearable, ItemSize.Hand, Grams: 1500, Ml: 4000, Wear: WearSlot.Feet),
        new(ItemKind.TacticalVest, "tactical-vest", "tactical vest", "tactical vests", '[', ItemCategory.Wearable | ItemCategory.Container, ItemSize.Hand,
            Grams: 1500, Ml: 4000, Wear: WearSlot.Body, Holds: new ContainerSpec(CapacityMl: 4000, MaxItemMl: 600)),
        new(ItemKind.NightVision, "night-vision", "pair of night-vision goggles", "pairs of night-vision goggles", '8', ItemCategory.Wearable | ItemCategory.Container, ItemSize.Hand,
            Grams: 600, Ml: 1000, Wear: WearSlot.Head, Holds: new ContainerSpec(CapacityMl: 30, MaxItems: 2, Only: [ItemKind.BatteryAA]), Comes: [(ItemKind.BatteryAA, 2)]),

        // ---- documents and keys ----
        new(ItemKind.SecurityLog, "security-log", "security log", "security logs", '"', ItemCategory.Document | ItemCategory.Usable, ItemSize.Pocket, Grams: 200, Ml: 300, UseTicks: 600,
            Note: "Shift reports. The last page: 'Perimeter power fluctuating. Told to keep the gates shut and the radio off.'"),
        new(ItemKind.ResearchFile, "research-file", "research file", "research files", '"', ItemCategory.Document | ItemCategory.Usable, ItemSize.Hand, Grams: 500, Ml: 1500, UseTicks: 1000,
            Note: "Sequencing results, half of each page blacked out. The project codes don't match anything on the park plan."),
        new(ItemKind.DataDrive, "data-drive", "data drive", "data drives", '"', ItemCategory.Document, ItemSize.Pocket, Grams: 30, Ml: 20,
            Note: "Encrypted. Someone will pay for what's on it."),
        new(ItemKind.ParkMap, "park-map", "park map", "park maps", '"', ItemCategory.Document | ItemCategory.Usable, ItemSize.Pocket, Grams: 50, Ml: 100, UseTicks: 400,
            Note: "The visitor map, folded soft. Half the attractions were never built."),
        new(ItemKind.Keycard, "keycard", "keycard", "keycards", '-', ItemCategory.Key, ItemSize.Pocket, Grams: 10, Ml: 5),
        new(ItemKind.Key, "key", "key", "keys", '-', ItemCategory.Key, ItemSize.Pocket, Grams: 15, Ml: 5),

        // ---- parts and makings ----
        new(ItemKind.Fuse, "fuse", "fuse", "fuses", ',', ItemCategory.Part, ItemSize.Pocket, Grams: 20, Ml: 10, MaxAmount: 10, Stacks: true),
        new(ItemKind.WireSpool, "wire-spool", "spool of wire", "spools of wire", 'o', ItemCategory.Part, ItemSize.Hand, Grams: 1500, Ml: 1500),
        new(ItemKind.ScrapMetal, "scrap-metal", "piece of scrap metal", "pieces of scrap metal", '~', ItemCategory.Part, ItemSize.Hand, Grams: 2000, Ml: 2000, MaxAmount: 5, Stacks: true),
        new(ItemKind.CircuitBoard, "circuit-board", "circuit board", "circuit boards", '#', ItemCategory.Part, ItemSize.Pocket, Grams: 150, Ml: 200, MaxAmount: 5, Stacks: true),
        new(ItemKind.Stick, "stick", "stout stick", "stout sticks", '|', ItemCategory.Part, ItemSize.Hand, Grams: 600, Ml: 1500, MaxAmount: 4, Stacks: true),
        new(ItemKind.Rag, "rag", "rag", "rags", '~', ItemCategory.Part, ItemSize.Pocket, Grams: 50, Ml: 100, MaxAmount: 10, Stacks: true),
        new(ItemKind.Torch, "torch", "torch", "torches", '|', ItemCategory.Light | ItemCategory.Usable, ItemSize.Hand, Grams: 800, Ml: 1600, Note: "A rag bound to a stick. Light it with a lighter or a match: it burns half an hour."),
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
    /// Reads an item spec: items separated by ';', each "[>]id [xN] [amount=N] [tag=T] [level=N] [locked] [open] [empty]".
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
                    "empty" => spec with { Empty = true },
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
        if (s.Empty) parts.Add("empty");
        return string.Join(' ', parts);
    }));
}
