using BlueHarvest.Content;
using BlueHarvest.Sim.Actors;

namespace BlueHarvest.Sim.ContentIntegration;

/// <summary>
/// Reads an item definition as the sim's <see cref="ItemDef"/> (P2b). P2a's parity tests showed the YAML says what
/// the old code table said, field by field, under this same reading; this is that reading, the other way round.
/// </summary>
public static class ItemDefBuilder
{
    /// <summary>The glyph of an item that doesn't give one: the family of tools, parts and small containers.</summary>
    public const char DefaultGlyph = '(';

    public static ItemDef From(ItemDefinition d)
    {
        ArgumentNullException.ThrowIfNull(d);
        return new ItemDef(
            d.Id,
            d.Name,
            d.Plural ?? d.Name + "s",
            GlyphOf(d),
            CategoriesOf(d.Categories, d),
            SizeOf(d),
            Grams: d.MassG,
            Ml: d.VolumeMl,
            MaxAmount: d.MaxAmount,
            StartAmount: d.StartAmount,
            Stacks: d.Stacks,
            MilligramsPerUnit: d.Fill?.MgPerUnit ?? 0,
            Unit: d.Fill?.Unit,
            Wear: WearOf(d),
            Holds: d.Holds is { } h ? HoldsOf(h, d) : null,
            Note: d.Description.Length == 0 ? null : d.Description,
            Fills: d.Fill?.FilledBy,
            Comes: d.ComesWith.Count == 0 ? null : d.ComesWith.Select(c => (Kind: c.Item, Count: c.Quantity)).ToList().AsReadOnly(),
            Dose: d.Dose,
            UseTicks: d.UseTicks)
        {
            Handlers = d.Handlers.ToHashSet(StringComparer.Ordinal),
            Food = d.Food is { } food ? (food.Kcal, food.WaterMl, food.AlertMinutes) : (0, 0, 0),
            Drink = d.Drink,
            Warmth = d.Warmth,
        };
    }

    private static char GlyphOf(ItemDefinition d) => d.Glyph switch
    {
        null => DefaultGlyph,
        { Length: 1 } g => g[0],
        var g => throw Bad(d, $"its glyph '{g}' isn't one character"),
    };

    private static ItemCategory CategoriesOf(IEnumerable<string> words, ItemDefinition d)
    {
        var categories = ItemCategory.None;
        foreach (string word in words)
        {
            if (!Enum.TryParse(word, ignoreCase: true, out ItemCategory category) || category == ItemCategory.None)
                throw Bad(d, $"the sim has no item category '{word}'");
            categories |= category;
        }
        return categories;
    }

    private static ItemSize SizeOf(ItemDefinition d) => d.Size switch
    {
        "pocket" => ItemSize.Pocket,
        "hand" => ItemSize.Hand,
        "two_handed" => ItemSize.TwoHanded,
        "fixed" => ItemSize.Fixed,
        var size => throw Bad(d, $"the sim has no item size '{size}'"),
    };

    private static WearSlot WearOf(ItemDefinition d)
    {
        if (d.Wear is null)
            return WearSlot.None;
        if (Enum.TryParse(d.Wear, ignoreCase: true, out WearSlot slot) && slot != WearSlot.None)
            return slot;
        throw Bad(d, $"the sim has no wear slot '{d.Wear}'");
    }

    private static ContainerSpec HoldsOf(ItemContainer h, ItemDefinition d) => new(
        h.CapacityMl,
        h.MaxItemMl,
        h.MaxItems,
        CategoriesOf(h.Accepts, d),
        h.Closes,
        h.OpenLoudness,
        h.OpenTicks,
        h.Only.Count == 0 ? null : h.Only.ToList().AsReadOnly());

    private static InvalidOperationException Bad(ItemDefinition d, string why) =>
        new($"{d.Id} ({d.SourcePath}): {why}.");
}
