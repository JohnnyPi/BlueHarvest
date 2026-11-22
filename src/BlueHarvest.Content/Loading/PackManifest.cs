#nullable enable
namespace BlueHarvest.Content;

/// <summary>
/// A pack's <c>pack.yaml</c> (<c>schema: blueharvest.pack/v1</c>). The pack ID is the namespace of every ID the
/// pack defines. <see cref="Priority"/> orders packs that don't depend on each other; it never lets one pack
/// silently override another: that takes an explicit <see cref="Replaces"/> entry.
/// </summary>
public sealed record PackManifest
{
    public required string Id { get; init; }
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
    public required SemVersion Version { get; init; }

    /// <summary>The game versions the pack works with, or null for any.</summary>
    public VersionRange? RequiresGame { get; init; }

    public IReadOnlyList<PackDependency> Dependencies { get; init; } = [];
    public int Priority { get; init; }

    /// <summary>Folders inside the pack that hold its definitions, relative to the pack folder.</summary>
    public IReadOnlyList<string> ContentRoots { get; init; } = [];

    /// <summary>Definitions in a dependency's namespace that this pack replaces, qualified.</summary>
    public IReadOnlyList<ContentId> Replaces { get; init; } = [];

    /// <summary>The manifest's path inside the content source.</summary>
    public required string ManifestPath { get; init; }

    /// <summary>The pack folder inside the content source, ending in '/', or "" when the source is the pack.</summary>
    public required string Root { get; init; }

    public bool DependsOn(string packId) => Dependencies.Any(d => d.Id == packId);
}

/// <summary>A dependency: a pack ID and, optionally, the versions accepted.</summary>
public sealed record PackDependency(string Id, VersionRange? Version);
