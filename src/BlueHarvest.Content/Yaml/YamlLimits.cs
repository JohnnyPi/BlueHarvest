#nullable enable
namespace BlueHarvest.Content;

/// <summary>The safety limits for reading one YAML file.</summary>
public sealed record YamlLimits
{
    public static YamlLimits Default { get; } = new();

    /// <summary>Largest file accepted, in bytes.</summary>
    public int MaxFileBytes { get; init; } = 1024 * 1024;

    /// <summary>Deepest nesting of lists and mappings.</summary>
    public int MaxDepth { get; init; } = 32;

    /// <summary>Most nodes in one file, counting each alias as the whole subtree it stands for.</summary>
    public long MaxNodes { get; init; } = 100_000;
}
