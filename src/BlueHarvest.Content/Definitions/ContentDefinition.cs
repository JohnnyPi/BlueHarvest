#nullable enable
namespace BlueHarvest.Content;

/// <summary>
/// An authored definition: immutable, shared, and identified by its <see cref="Id"/> (contracts §2). Instances in
/// the world refer to it; it never holds world state.
/// </summary>
public abstract record ContentDefinition
{
    public required ContentId Id { get; init; }

    /// <summary>The display name (a narrative entry's title).</summary>
    public required string Name { get; init; }

    /// <summary>The pack this definition came from. Normally <c>Id.Namespace</c>; a replacement's own pack otherwise.</summary>
    public required string PackId { get; init; }

    /// <summary>The file it was read from, inside the content source. Not part of the fingerprint.</summary>
    public required string SourcePath { get; init; }

    public int SourceLine { get; init; }

    public string Description { get; init; } = "";

    public IReadOnlyList<string> Tags { get; init; } = [];

    /// <summary>Provenance: the catalog IDs this definition stands for (<c>source_ids: [ITM-TOL-018]</c>).</summary>
    public IReadOnlyList<string> SourceIds { get; init; } = [];

    /// <summary>The C# handler keys this definition names. Each must be known and implemented to be runtime-ready.</summary>
    public IReadOnlyList<string> Handlers { get; init; } = [];

    /// <summary>One of <see cref="ContentKinds"/>.</summary>
    public abstract string Kind { get; }

    internal void WriteCanonical(CanonicalWriter w)
    {
        w.Field("kind", Kind);
        w.Field("id", Id);
        w.Field("pack", PackId);
        w.Field("name", Name);
        w.Field("description", Description);
        w.SetField("tags", Tags);
        w.SetField("source_ids", SourceIds);
        w.SetField("handlers", Handlers);
        WriteFields(w);
    }

    /// <summary>The kind's own fields, in a fixed order, with defaults filled in.</summary>
    internal abstract void WriteFields(CanonicalWriter w);
}
