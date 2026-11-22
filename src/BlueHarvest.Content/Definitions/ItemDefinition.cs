#nullable enable
namespace BlueHarvest.Content;

/// <summary>
/// <c>kind: item</c>. A type of thing; each one in the world is an instance with its own ID. It carries every
/// mechanical field the sim's item catalog has (P2a), so the catalog can be built from content.
/// </summary>
public sealed record ItemDefinition : ContentDefinition
{
    public override string Kind => ContentKinds.Item;

    /// <summary>The words that say what it is and what it can do (<c>medical</c>, <c>usable</c>, <c>container</c>…); see <see cref="ItemWords.Categories"/>.</summary>
    public IReadOnlyList<string> Categories { get; init; } = [];

    /// <summary>"first-aid kits"; null when not given.</summary>
    public string? Plural { get; init; }

    /// <summary>The one character it's drawn with on the map; null when not given.</summary>
    public string? Glyph { get; init; }

    /// <summary><c>pocket</c>, <c>hand</c>, <c>two_handed</c> or <c>fixed</c>.</summary>
    public string Size { get; init; } = "hand";

    public int MassG { get; init; }

    public int VolumeMl { get; init; }

    /// <summary>How many fit in one stack; 1 for a thing that doesn't stack.</summary>
    public int StackLimit { get; init; } = 1;

    /// <summary><c>stack</c> or <c>unique</c>.</summary>
    public string Stacking { get; init; } = "unique";

    public bool Stacks => Stacking == "stack";

    /// <summary>What it's filled or charged with (water, pills, rounds, charge), for things that don't stack.</summary>
    public ItemFill? Fill { get; init; }

    /// <summary>The sim's <c>MaxAmount</c>: the fill's capacity, the stack limit, or 1.</summary>
    public int MaxAmount => Fill?.Capacity ?? (Stacks ? StackLimit : 1);

    /// <summary>The sim's <c>StartAmount</c>: a new one's fill, or 1.</summary>
    public int StartAmount => Fill?.Start ?? 1;

    /// <summary>How much one use takes (250 ml of water, 2 pills).</summary>
    public int Dose { get; init; } = 1;

    /// <summary>How long one use takes, in ticks (1/100 s of action time).</summary>
    public int UseTicks { get; init; }

    /// <summary>Where it's worn: <c>back</c>, <c>waist</c>, <c>body</c>, <c>head</c>, <c>hands</c>, <c>feet</c>, or null.</summary>
    public string? Wear { get; init; }

    /// <summary>What it holds, if it's a container.</summary>
    public ItemContainer? Holds { get; init; }

    /// <summary>What a new one comes fitted with (a flashlight's two D batteries), unless made empty.</summary>
    public IReadOnlyList<ItemQuantity> ComesWith { get; init; } = [];

    /// <summary>What eating one gives.</summary>
    public ItemFood? Food { get; init; }

    /// <summary>What drinking from it gives; null for plain water (1 ml of water per ml).</summary>
    public ItemDrink? Drink { get; init; }

    /// <summary>How much warmth it gives when worn (points, added up over what's worn).</summary>
    public int Warmth { get; init; }

    public bool Is(string category) => Categories.Contains(category);

    internal override void WriteFields(CanonicalWriter w)
    {
        w.SetField("categories", Categories);
        w.Text("plural");
        w.OptionalText(Plural);
        w.Text("glyph");
        w.OptionalText(Glyph);
        w.Field("size", Size);
        w.Field("mass_g", MassG);
        w.Field("volume_ml", VolumeMl);
        w.Field("stack_limit", StackLimit);
        w.Field("stacking", Stacking);
        w.Text("fill");
        if (Fill is { } f)
        {
            w.Field("capacity", f.Capacity);
            w.Field("start", f.Start);
            w.Field("unit", f.Unit);
            w.Field("mg_per_unit", f.MgPerUnit);
            w.Field("filled_by", f.FilledBy);
        }
        else
            w.Nothing();
        w.Field("dose", Dose);
        w.Field("use_ticks", UseTicks);
        w.Text("wear");
        w.OptionalText(Wear);
        w.Text("holds");
        if (Holds is { } h)
        {
            w.Field("capacity_ml", h.CapacityMl);
            w.Field("max_item_ml", h.MaxItemMl);
            w.Field("max_items", h.MaxItems);
            w.SetField("accepts", h.Accepts);
            w.Field("closes", h.Closes);
            w.Field("open_loudness", h.OpenLoudness);
            w.Field("open_ticks", h.OpenTicks);
            w.SetField("only", h.Only.Select(id => id.Value).ToList());
        }
        else
            w.Nothing();
        CanonicalLines.Write(w, "comes_with", ComesWith);
        w.Text("food");
        if (Food is { } food)
        {
            w.Number(food.Kcal);
            w.Number(food.WaterMl);
            w.Number(food.AlertMinutes);
        }
        else
            w.Nothing();
        w.Text("drink");
        if (Drink is { } drink)
        {
            w.Number(drink.PerMl);
            w.Number(drink.Kcal);
            w.Number(drink.WaterMl);
            w.Number(drink.AlertMinutes);
        }
        else
            w.Nothing();
        w.Field("warmth", Warmth);
    }
}

/// <summary>
/// A fill: up to <see cref="Capacity"/> of <see cref="Unit"/> (ml, pills, rounds, %), <see cref="Start"/> in a new
/// one, each unit adding <see cref="MgPerUnit"/> milligrams. <see cref="FilledBy"/> is the stacking item it's
/// loaded with (a magazine with 9 mm rounds), if any.
/// </summary>
public sealed record ItemFill(int Capacity, int Start, string Unit, int MgPerUnit, ContentId? FilledBy);

/// <summary>
/// What a container holds: up to <see cref="CapacityMl"/>, nothing bigger than <see cref="MaxItemMl"/>, at most
/// <see cref="MaxItems"/> things (each <see cref="int.MaxValue"/> when not given); only things with one of the
/// <see cref="Accepts"/> categories (any, when empty), and only <see cref="Only"/> items when that's given. One that
/// <see cref="Closes"/> has a door or lid: opening it takes <see cref="OpenTicks"/> and makes <see cref="OpenLoudness"/>.
/// </summary>
public sealed record ItemContainer(
    int CapacityMl, int MaxItemMl, int MaxItems, IReadOnlyList<string> Accepts, bool Closes, int OpenLoudness, int OpenTicks,
    IReadOnlyList<ContentId> Only);

/// <summary>What one eaten gives: kcal, ml of water, and minutes of tiredness taken off.</summary>
public sealed record ItemFood(int Kcal, int WaterMl, int AlertMinutes);

/// <summary>
/// What a drink gives, by the ml: for every <see cref="PerMl"/> ml drunk, <see cref="Kcal"/>, <see cref="WaterMl"/>
/// and <see cref="AlertMinutes"/>, each worked out as <c>value × ml / PerMl</c>, rounded down.
/// </summary>
public sealed record ItemDrink(int PerMl, int Kcal, int WaterMl, int AlertMinutes)
{
    public (int Kcal, int WaterMl, int AlertMinutes) For(int ml) => (Kcal * ml / PerMl, WaterMl * ml / PerMl, AlertMinutes * ml / PerMl);
}

/// <summary>The fixed words item fields use.</summary>
public static class ItemWords
{
    /// <summary>The categories, as the sim's <c>ItemCategory</c> flags have them, in their order there.</summary>
    public static IReadOnlyList<string> Categories { get; } = Array.AsReadOnly(new[]
    {
        "usable", "wearable", "potable", "edible", "medical", "tool", "light", "weapon", "ammo", "power", "fuel",
        "key", "document", "container", "throwable", "fixture", "part",
    });

    public static IReadOnlyList<string> Sizes { get; } = Array.AsReadOnly(new[] { "pocket", "hand", "two_handed", "fixed" });

    public static IReadOnlyList<string> WearSlots { get; } = Array.AsReadOnly(new[] { "back", "waist", "body", "head", "hands", "feet" });
}
