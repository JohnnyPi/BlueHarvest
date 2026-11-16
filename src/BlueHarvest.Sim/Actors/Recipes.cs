namespace BlueHarvest.Sim.Actors;

/// <summary>How two items go together. Append only.</summary>
public enum CombineKind : byte
{
    /// <summary>Rounds or shells pressed into what fills with them (a magazine, a shotgun).</summary>
    Load,
    /// <summary>A part fitted into the slot that takes it (batteries into a flashlight, a magazine into a pistol).</summary>
    Fit,
    /// <summary>Two things made into a third (a stick and duct tape into a splint).</summary>
    Make,
}

/// <summary>Two kinds that make a third, using up <see cref="UsesOfA"/> of the first and <see cref="UsesOfB"/> of the second.</summary>
public sealed record Recipe(ItemKind A, ItemKind B, ItemKind Makes, int Ticks, int UsesOfA = 1, int UsesOfB = 1);

/// <summary>A way two carried items go together: what goes <see cref="Into"/> what, or the recipe that makes something new.</summary>
public readonly record struct Combination(CombineKind Kind, Item Into, Item From, Recipe? Recipe = null);

/// <summary>What using an item does. Append only.</summary>
public enum ItemUse : byte
{
    Eat,
    Drink,
    /// <summary>A bandage or tourniquet on a bleeding wound: it stops the bleeding (a red wound still needs a kit).</summary>
    StopBleeding,
    /// <summary>Pills swallowed: painkillers dull pain for four game hours (antibiotics wait for sickness).</summary>
    TakeMedicine,
    /// <summary>Antiseptic, stitches, burn gel, a splint on your wounds (effects to come).</summary>
    DressWounds,
    Read,
    /// <summary>A compass: which way you face.</summary>
    TakeBearing,
    /// <summary>A radio: switched on and listened to.</summary>
    Listen,
    /// <summary>A tablet dropped into the water you carry.</summary>
    TreatWater,
    /// <summary>An adrenaline auto-injector: pain shut out and full breath for a little while.</summary>
    Inject,
    /// <summary>A headlamp or lantern switched on or off.</summary>
    SwitchLight,
    /// <summary>A glow stick cracked: it glows for eight hours.</summary>
    Crack,
    /// <summary>A torch lit with a lighter, a match or another torch: it burns half an hour.</summary>
    LightTorch,
}

/// <summary>What goes with what, and what using a thing does.</summary>
public static class Recipes
{
    /// <summary>Things you can make from two others.</summary>
    public static readonly IReadOnlyList<Recipe> All =
    [
        new(ItemKind.Stick, ItemKind.DuctTape, ItemKind.Splint, Ticks: 1500, UsesOfB: 2),
        new(ItemKind.Stick, ItemKind.Rag, ItemKind.Torch, Ticks: 1000),
        new(ItemKind.Rag, ItemKind.Antiseptic, ItemKind.Bandage, Ticks: 600),
    ];

    /// <summary>The recipe for two kinds, either way round, or null.</summary>
    public static Recipe? Find(ItemKind a, ItemKind b) =>
        All.FirstOrDefault(r => r.A == a && r.B == b || r.A == b && r.B == a);

    /// <summary>
    /// How two items go together, if they do: rounds load into what fills with them; a part fits the
    /// slot that takes it; or a recipe makes something of them. Null if they don't go together.
    /// </summary>
    public static Combination? Of(Item a, Item b)
    {
        if (a == b)
            return null;
        if (b.Def.Fills == a.Kind)
            return new Combination(CombineKind.Load, b, a);
        if (a.Def.Fills == b.Kind)
            return new Combination(CombineKind.Load, a, b);
        if (b.Def.Holds is { Only: { } bTakes } && bTakes.Contains(a.Kind))
            return new Combination(CombineKind.Fit, b, a);
        if (a.Def.Holds is { Only: { } aTakes } && aTakes.Contains(b.Kind))
            return new Combination(CombineKind.Fit, a, b);
        if (Find(a.Kind, b.Kind) is { } recipe)
            return recipe.A == a.Kind ? new Combination(CombineKind.Make, a, b, recipe) : new Combination(CombineKind.Make, b, a, recipe);
        return null;
    }

    /// <summary>What using a kind does, or null if it isn't used on its own (the first-aid kit has its own key).</summary>
    public static ItemUse? UseOf(ItemDef def) => def.Kind switch
    {
        ItemKind.Bandage or ItemKind.Tourniquet => ItemUse.StopBleeding,
        ItemKind.Painkillers or ItemKind.Antibiotics => ItemUse.TakeMedicine,
        ItemKind.Antiseptic or ItemKind.SutureKit or ItemKind.BurnGel or ItemKind.Splint => ItemUse.DressWounds,
        ItemKind.EpiPen => ItemUse.Inject,
        ItemKind.Headlamp or ItemKind.Lantern => ItemUse.SwitchLight,
        ItemKind.GlowStick => ItemUse.Crack,
        ItemKind.Torch => ItemUse.LightTorch,
        ItemKind.Compass => ItemUse.TakeBearing,
        ItemKind.Radio => ItemUse.Listen,
        ItemKind.WaterTablets => ItemUse.TreatWater,
        ItemKind.FirstAidKit => null,
        _ when def.Is(ItemCategory.Document) && def.Is(ItemCategory.Usable) => ItemUse.Read,
        _ when def.Is(ItemCategory.Potable) && !def.Stacks => ItemUse.Drink,
        _ when def.Is(ItemCategory.Edible) => ItemUse.Eat,
        _ => null,
    };
}
