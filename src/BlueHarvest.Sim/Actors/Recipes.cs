using BlueHarvest.Content;
using BlueHarvest.Sim.ContentIntegration;

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

/// <summary>Two items that make a third, using up <see cref="UsesOfA"/> of the first and <see cref="UsesOfB"/> of the second.</summary>
public sealed record Recipe(ContentId A, ContentId B, ContentId Makes, int Ticks, int UsesOfA = 1, int UsesOfB = 1);

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
        new(CoreItems.Stick, CoreItems.DuctTape, CoreItems.Splint, Ticks: 1500, UsesOfB: 2),
        new(CoreItems.Stick, CoreItems.Rag, CoreItems.Torch, Ticks: 1000),
        new(CoreItems.Rag, CoreItems.Antiseptic, CoreItems.Bandage, Ticks: 600),
    ];

    /// <summary>The recipe for two items, either way round, or null.</summary>
    public static Recipe? Find(ContentId a, ContentId b) =>
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

    /// <summary>
    /// What using an item does: the way of using that its <c>use.*</c> handler names (<see cref="ItemHandlers.UseKeys"/>),
    /// or null if it has none (it isn't used on its own: the first-aid kit has its own key).
    /// </summary>
    public static ItemUse? UseOf(ItemDef def)
    {
        foreach (var (key, use) in Uses)
            if (def.Has(key))
                return use;
        return null;
    }

    /// <summary>Each use handler and the way of using it names: two keys take medicine, and two see to wounds.</summary>
    private static readonly (string Key, ItemUse Use)[] Uses =
    [
        (ItemHandlers.Eat, ItemUse.Eat),
        (ItemHandlers.Drink, ItemUse.Drink),
        (ItemHandlers.Read, ItemUse.Read),
        (ItemHandlers.StopBleeding, ItemUse.StopBleeding),
        (ItemHandlers.Painkillers, ItemUse.TakeMedicine),
        (ItemHandlers.Antibiotics, ItemUse.TakeMedicine),
        (ItemHandlers.CleanWounds, ItemUse.DressWounds),
        (ItemHandlers.DressWounds, ItemUse.DressWounds),
        (ItemHandlers.Adrenaline, ItemUse.Inject),
        (ItemHandlers.SwitchLight, ItemUse.SwitchLight),
        (ItemHandlers.Crack, ItemUse.Crack),
        (ItemHandlers.LightTorch, ItemUse.LightTorch),
        (ItemHandlers.Bearing, ItemUse.TakeBearing),
        (ItemHandlers.Listen, ItemUse.Listen),
        (ItemHandlers.TreatWater, ItemUse.TreatWater),
    ];
}
