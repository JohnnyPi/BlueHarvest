using BlueHarvest.Content;

namespace BlueHarvest.Sim.ContentIntegration;

/// <summary>
/// The core pack's items that code names: what a loadout carries, what a loot table or a recipe makes, what the
/// tests set up (P2b). They are only names for content IDs (the 91 kinds the code had before content). Mechanics
/// never check for one of these: they check for a handler (<see cref="ItemHandlers"/>), so that any item with the
/// handler works. A test checks each one is an item in content/packs.
/// </summary>
public static class CoreItems
{
    public static readonly ContentId FirstAidKit = new("core:first-aid-kit");
    public static readonly ContentId Flare = new("core:flare");
    public static readonly ContentId Flashlight = new("core:flashlight");

    /// <summary>Rope and harness, 20 m. Used up by a rappel: it stays tied at the top.</summary>
    public static readonly ContentId Rope = new("core:rope");

    // step 20a: containers you carry
    public static readonly ContentId Daypack = new("core:daypack");
    public static readonly ContentId FieldPack = new("core:field-pack");
    public static readonly ContentId BeltPouch = new("core:belt-pouch");
    public static readonly ContentId HardCase = new("core:hard-case");

    // step 20a: containers that belong to the room
    public static readonly ContentId Locker = new("core:locker");
    public static readonly ContentId Crate = new("core:crate");
    public static readonly ContentId Desk = new("core:desk");
    public static readonly ContentId Fridge = new("core:fridge");
    public static readonly ContentId Cabinet = new("core:cabinet");
    public static readonly ContentId MedicineCabinet = new("core:medicine-cabinet");

    // step 20a: a first few of each kind of thing
    public static readonly ContentId WaterBottle = new("core:water-bottle");
    public static readonly ContentId EnergyBar = new("core:energy-bar");
    public static readonly ContentId CannedFood = new("core:canned-food");
    public static readonly ContentId Bandage = new("core:bandage");
    public static readonly ContentId Painkillers = new("core:painkillers");
    public static readonly ContentId Knife = new("core:knife");
    public static readonly ContentId Crowbar = new("core:crowbar");
    public static readonly ContentId BatteryD = new("core:battery-d");
    public static readonly ContentId Notebook = new("core:notebook");

    // step 20b: food
    public static readonly ContentId Mre = new("core:mre");
    public static readonly ContentId Crackers = new("core:crackers");
    public static readonly ContentId Jerky = new("core:jerky");
    public static readonly ContentId DriedFruit = new("core:dried-fruit");
    public static readonly ContentId Chocolate = new("core:chocolate");
    public static readonly ContentId Noodles = new("core:noodles");
    public static readonly ContentId CannedPeaches = new("core:canned-peaches");
    public static readonly ContentId Coconut = new("core:coconut");

    // drink
    public static readonly ContentId Canteen = new("core:canteen");
    public static readonly ContentId Soda = new("core:soda");
    public static readonly ContentId Thermos = new("core:thermos");
    public static readonly ContentId WaterTablets = new("core:water-tablets");

    // medicine
    public static readonly ContentId Antiseptic = new("core:antiseptic");
    public static readonly ContentId Splint = new("core:splint");
    public static readonly ContentId SutureKit = new("core:suture-kit");
    public static readonly ContentId Tourniquet = new("core:tourniquet");
    public static readonly ContentId BurnGel = new("core:burn-gel");
    public static readonly ContentId Antibiotics = new("core:antibiotics");
    public static readonly ContentId EpiPen = new("core:epi-pen");

    // lights and power
    public static readonly ContentId Headlamp = new("core:headlamp");
    public static readonly ContentId Lantern = new("core:lantern");
    public static readonly ContentId GlowStick = new("core:glow-stick");
    public static readonly ContentId BatteryAA = new("core:battery-aa");
    public static readonly ContentId Matches = new("core:matches");
    public static readonly ContentId Lighter = new("core:lighter");

    // tools
    public static readonly ContentId Multitool = new("core:multitool");
    public static readonly ContentId BoltCutters = new("core:bolt-cutters");
    public static readonly ContentId WireCutters = new("core:wire-cutters");
    public static readonly ContentId ToolKit = new("core:tool-kit");
    public static readonly ContentId DuctTape = new("core:duct-tape");
    public static readonly ContentId Binoculars = new("core:binoculars");
    public static readonly ContentId Compass = new("core:compass");
    public static readonly ContentId Radio = new("core:radio");
    public static readonly ContentId JerryCan = new("core:jerry-can");
    public static readonly ContentId FireExtinguisher = new("core:fire-extinguisher");
    public static readonly ContentId SpareParts = new("core:spare-parts");

    // guns and ammunition (carried and loaded; firing comes with combat)
    public static readonly ContentId Pistol = new("core:pistol");
    public static readonly ContentId Magazine9mm = new("core:magazine-9mm");
    public static readonly ContentId Rounds9mm = new("core:rounds-9mm");
    public static readonly ContentId Shotgun = new("core:shotgun");
    public static readonly ContentId Shells12 = new("core:shells-12");
    public static readonly ContentId Rifle = new("core:rifle");
    public static readonly ContentId Rounds308 = new("core:rounds-308");
    public static readonly ContentId TranqRifle = new("core:tranq-rifle");
    public static readonly ContentId TranqDart = new("core:tranq-dart");
    public static readonly ContentId FlareGun = new("core:flare-gun");
    public static readonly ContentId FlareShell = new("core:flare-shell");
    public static readonly ContentId Machete = new("core:machete");
    public static readonly ContentId Taser = new("core:taser");

    // clothing
    public static readonly ContentId Poncho = new("core:poncho");
    public static readonly ContentId HardHat = new("core:hard-hat");
    public static readonly ContentId WorkGloves = new("core:work-gloves");
    public static readonly ContentId Boots = new("core:boots");
    public static readonly ContentId TacticalVest = new("core:tactical-vest");
    public static readonly ContentId NightVision = new("core:night-vision");

    // documents and keys
    public static readonly ContentId SecurityLog = new("core:security-log");
    public static readonly ContentId ResearchFile = new("core:research-file");
    public static readonly ContentId DataDrive = new("core:data-drive");
    public static readonly ContentId ParkMap = new("core:park-map");
    public static readonly ContentId Keycard = new("core:keycard");
    public static readonly ContentId Key = new("core:key");

    // parts and makings
    public static readonly ContentId Fuse = new("core:fuse");
    public static readonly ContentId WireSpool = new("core:wire-spool");
    public static readonly ContentId ScrapMetal = new("core:scrap-metal");
    public static readonly ContentId CircuitBoard = new("core:circuit-board");
    public static readonly ContentId Stick = new("core:stick");
    public static readonly ContentId Rag = new("core:rag");
    public static readonly ContentId Torch = new("core:torch");
}
