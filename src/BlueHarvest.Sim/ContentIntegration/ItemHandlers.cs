using BlueHarvest.Content;

namespace BlueHarvest.Sim.ContentIntegration;

/// <summary>
/// The item mechanics the sim has today, as handler keys that item definitions name (P2a). Each one is already
/// implemented: the code that does it still checks the item kind until P2b, which switches those checks to these
/// keys. A <c>use.*</c> key is what using the item on its own does (<c>Recipes.UseOf</c>); the others are the
/// kind-specific code elsewhere, named where it lives.
/// </summary>
public static class ItemHandlers
{
    // Using an item on its own (Recipes.UseOf, Simulation.ItemUse).
    public const string Eat = "use.eat";
    public const string Drink = "use.drink";
    public const string Read = "use.read";
    public const string StopBleeding = "use.stop_bleeding";
    public const string Painkillers = "use.painkillers";
    public const string Antibiotics = "use.antibiotics";
    public const string CleanWounds = "use.clean_wounds";
    public const string DressWounds = "use.dress_wounds";
    public const string Adrenaline = "use.adrenaline";
    public const string SwitchLight = "use.switch_light";
    public const string Crack = "use.crack";
    public const string LightTorch = "use.light_torch";
    public const string Bearing = "use.bearing";
    public const string Listen = "use.listen";
    public const string TreatWater = "use.treat_water";

    // Kind-specific mechanics elsewhere.
    public const string FirstAid = "treat.first_aid";
    public const string Flare = "light.flare";
    public const string GlowStick = "light.glow_stick";
    public const string Flashlight = "light.flashlight";
    public const string Headlamp = "light.headlamp";
    public const string Lantern = "light.lantern";
    public const string Torch = "light.torch";
    public const string Flame = "fire.flame";
    public const string Extinguish = "fire.extinguish";
    public const string JerryCan = "fuel.jerry_can";
    public const string Rope = "climb.rope";
    public const string Pry = "tool.pry";
    public const string ToolKit = "repair.tool_kit";
    public const string SpareParts = "repair.spare_parts";
    public const string Keycard = "key.keycard";
    public const string ParkMap = "map.park_map";
    public const string Refill = "water.refill";
    public const string RainCover = "wear.rain_cover";

    /// <summary>The use handlers, one per <see cref="Actors.ItemUse"/> way of using (two for taking medicine, two for wounds).</summary>
    public static IReadOnlyList<string> UseKeys { get; } = Array.AsReadOnly(new[]
    {
        Eat, Drink, Read, StopBleeding, Painkillers, Antibiotics, CleanWounds, DressWounds, Adrenaline, SwitchLight, Crack,
        LightTorch, Bearing, Listen, TreatWater,
    });

    public static IReadOnlyList<HandlerContract> Contracts { get; } = Array.AsReadOnly(new[]
    {
        Item(Eat, "Eaten, one at a time: its food values feed hunger and thirst (Simulation.ItemUse, Nutrition.Food)."),
        Item(Drink, "Drunk a dose at a time from its fill; unsafe water risks a gut infection (Nutrition.Drink)."),
        Item(Read, "Read: a document's text."),
        Item(StopBleeding, "Stops the bleeding of one bleeding wound."),
        Item(Painkillers, "Pills that dull pain for four game hours."),
        Item(Antibiotics, "Pills against sickness."),
        Item(CleanWounds, "Cleans and dresses wounds (antiseptic, stitches, burn gel)."),
        Item(DressWounds, "Dresses wounds without cleaning them (a splint)."),
        Item(Adrenaline, "Pain shut out and full breath for a little while."),
        Item(SwitchLight, "A worn or carried light switched on or off."),
        Item(Crack, "A glow stick cracked: it glows for eight hours."),
        Item(LightTorch, "Lit from a flame or another torch: it burns half an hour."),
        Item(Bearing, "Says which way you face."),
        Item(Listen, "Switched on and listened to: static others can hear."),
        Item(TreatWater, "Makes the water you carry safe."),
        Item(FirstAid, "Treats a wound (its own key, Simulation.TreatWound)."),
        Item(Flare, "Lit and thrown: burns red where it lands, and can start a fire (Simulation, Simulation.Fire)."),
        Item(GlowStick, "Thrown, or carried glowing: a dim light, even from a pocket (Simulation.Lights)."),
        Item(Flashlight, "A battery beam you aim; its batteries run down while it's on."),
        Item(Headlamp, "A beam where you face, worn; a dim glow when set down."),
        Item(Lantern, "A light all round, carried or set down."),
        Item(Torch, "A burning light, and a flame to light things from."),
        Item(Flame, "Lights torches and fires (a lighter, matches)."),
        Item(Extinguish, "Puts out fire, a charge at a time."),
        Item(JerryCan, "Pours fuel to burn, or fills a generator's tank."),
        Item(Rope, "Ropes you down a drop; it stays tied at the top."),
        Item(Pry, "Forces a lock: slow and loud."),
        Item(ToolKit, "Makes repairs go better and faster."),
        Item(SpareParts, "What a seized generator needs; used up by the repair."),
        Item(Keycard, "Opens doors by its site and level, and the monorail."),
        Item(ParkMap, "Charts its sheet of the island when picked up or read (Simulation.Knowledge)."),
        Item(Refill, "Filled from a stream, river or pool (unsafe until treated)."),
        Item(RainCover, "Keeps the rain off when worn on the body."),
    });

    private static HandlerContract Item(string key, string summary) => new(key, ContentKinds.Item, Implemented: true, summary);
}
