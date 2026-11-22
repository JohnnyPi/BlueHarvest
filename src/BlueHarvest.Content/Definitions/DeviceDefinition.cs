#nullable enable
namespace BlueHarvest.Content;

/// <summary>
/// <c>kind: device</c>. A type of machine that takes parts and can be activated (<c>DEV-ADM-001</c>, the main
/// pump's power controller). Its facts are requests to the read-only fact compiler, not permission to set state.
/// </summary>
public sealed record DeviceDefinition : ContentDefinition
{
    public override string Kind => ContentKinds.Device;

    public string Handler => Handlers.Count > 0 ? Handlers[0] : "";

    public IReadOnlyList<ItemQuantity> RequiredParts { get; init; } = [];

    /// <summary><c>none</c> or <c>available</c>.</summary>
    public string Power { get; init; } = "none";

    public IReadOnlyList<string> States { get; init; } = [];

    public IReadOnlyList<FactProjection> FactsWhenActive { get; init; } = [];

    internal override void WriteFields(CanonicalWriter w)
    {
        CanonicalLines.Write(w, "required_parts", RequiredParts);
        w.Field("power", Power);
        w.Field("states", States);
        w.Text("facts_when_active");
        w.Count(FactsWhenActive.Count);
        foreach (FactProjection f in FactsWhenActive)
        {
            w.Text(f.Predicate);
            w.Field("args", f.Args);
        }
    }
}
