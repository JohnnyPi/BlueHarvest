#nullable enable
namespace BlueHarvest.Content;

/// <summary>An item and how many: a recipe input, tool or output, or a device's required part.</summary>
public sealed record ItemQuantity(ContentId Item, int Quantity);

/// <summary>A resource source's yield of one item per harvest, from <see cref="Min"/> to <see cref="Max"/>.</summary>
public sealed record YieldRange(ContentId Item, int Min, int Max);

/// <summary>A fact a device projects while active: <c>lit(self)</c> (a request to the fact compiler, not world state).</summary>
public sealed record FactProjection(string Predicate, IReadOnlyList<string> Args);

internal static class CanonicalLines
{
    public static void Write(CanonicalWriter w, string name, IReadOnlyList<ItemQuantity> lines)
    {
        w.Text(name);
        w.Count(lines.Count);
        foreach (ItemQuantity line in lines)
        {
            w.Text(line.Item.Value);
            w.Number(line.Quantity);
        }
    }
}
