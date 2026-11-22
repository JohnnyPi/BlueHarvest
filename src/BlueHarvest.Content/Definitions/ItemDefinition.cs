#nullable enable
namespace BlueHarvest.Content;

/// <summary><c>kind: item</c>. A type of thing; each one in the world is an instance with its own ID.</summary>
public sealed record ItemDefinition : ContentDefinition
{
    public override string Kind => ContentKinds.Item;

    /// <summary>A lower-case token: <c>material</c>, <c>tool</c>, <c>food</c>.</summary>
    public required string Category { get; init; }

    public int MassG { get; init; }

    /// <summary>How many fit in one stack; 1 for a unique item.</summary>
    public int StackLimit { get; init; } = 1;

    /// <summary><c>stack</c> or <c>unique</c>.</summary>
    public string Stacking { get; init; } = "unique";

    internal override void WriteFields(CanonicalWriter w)
    {
        w.Field("category", Category);
        w.Field("mass_g", MassG);
        w.Field("stack_limit", StackLimit);
        w.Field("stacking", Stacking);
    }
}
