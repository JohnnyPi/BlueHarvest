#nullable enable
namespace BlueHarvest.Content;

/// <summary>
/// <c>kind: recipe</c>. A process worked at a station: inputs consumed, tools kept, outputs made, or targets
/// changed in place (a repair keeps the same item instance).
/// </summary>
public sealed record RecipeDefinition : ContentDefinition
{
    public override string Kind => ContentKinds.Recipe;

    /// <summary>The recipe's one handler key (<c>process.transform</c>, <c>process.repair</c>).</summary>
    public string Handler => Handlers.Count > 0 ? Handlers[0] : "";

    public required ContentId Station { get; init; }

    /// <summary>In sim ticks, from <c>duration_action_seconds</c> or <c>duration_game_minutes</c>.</summary>
    public long DurationTicks { get; init; }

    public IReadOnlyList<ItemQuantity> Inputs { get; init; } = [];
    public IReadOnlyList<ItemQuantity> Tools { get; init; } = [];
    public IReadOnlyList<ItemQuantity> Outputs { get; init; } = [];
    public IReadOnlyList<ItemQuantity> Byproducts { get; init; } = [];
    public IReadOnlyList<ItemQuantity> Targets { get; init; } = [];

    /// <summary><c>none</c> or <c>available</c>.</summary>
    public string Heat { get; init; } = "none";

    /// <summary><c>none</c> or <c>available</c>.</summary>
    public string Power { get; init; } = "none";

    internal override void WriteFields(CanonicalWriter w)
    {
        w.Field("station", Station);
        w.Field("duration_ticks", DurationTicks);
        CanonicalLines.Write(w, "inputs", Inputs);
        CanonicalLines.Write(w, "tools", Tools);
        CanonicalLines.Write(w, "outputs", Outputs);
        CanonicalLines.Write(w, "byproducts", Byproducts);
        CanonicalLines.Write(w, "targets", Targets);
        w.Field("heat", Heat);
        w.Field("power", Power);
    }
}
