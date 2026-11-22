#nullable enable
namespace BlueHarvest.Content;

/// <summary>
/// <c>kind: resource_source</c>. A type of gatherable place (<c>SRC-005</c> Vine Thicket). Each placed one is an
/// instance with its own stock; regrowth is a world event, never a refill on reload.
/// </summary>
public sealed record ResourceSourceDefinition : ContentDefinition
{
    public override string Kind => ContentKinds.ResourceSource;

    public string Handler => Handlers.Count > 0 ? Handlers[0] : "";

    /// <summary>The tool a harvest needs, if any.</summary>
    public ContentId? RequiredTool { get; init; }

    /// <summary><c>finite</c> or <c>world_event</c>.</summary>
    public string RenewalMode { get; init; } = "finite";

    /// <summary>The world event that renews it, for <c>world_event</c> renewal.</summary>
    public string? RenewalEvent { get; init; }

    /// <summary>Harvests before it's empty (and, when renewable, before it needs its event).</summary>
    public int MaxBatches { get; init; } = 1;

    public IReadOnlyList<YieldRange> Yields { get; init; } = [];

    internal override void WriteFields(CanonicalWriter w)
    {
        w.Field("required_tool", RequiredTool);
        w.Field("renewal_mode", RenewalMode);
        w.Text("renewal_event");
        if (RenewalEvent is null)
            w.Nothing();
        else
            w.Text(RenewalEvent);
        w.Field("max_batches", MaxBatches);
        w.Text("yields");
        w.Count(Yields.Count);
        foreach (YieldRange y in Yields)
        {
            w.Text(y.Item.Value);
            w.Number(y.Min);
            w.Number(y.Max);
        }
    }
}
