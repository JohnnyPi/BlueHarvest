#nullable enable
namespace BlueHarvest.Content;

public enum ContentMode
{
    /// <summary>Schema and references must be right; definitions that aren't runtime-ready are reported as notes.</summary>
    Authoring,
    /// <summary>As authoring, and every definition must also be runtime-ready: what the game needs to start.</summary>
    Runtime,
}

public sealed record ContentLoadOptions
{
    public static ContentLoadOptions Authoring { get; } = new();
    public static ContentLoadOptions Runtime { get; } = new() { Mode = ContentMode.Runtime };

    public ContentMode Mode { get; init; } = ContentMode.Authoring;

    /// <summary>The game version packs' <c>requires_game</c> is checked against; null skips that check.</summary>
    public SemVersion? GameVersion { get; init; }

    public HandlerRegistry Handlers { get; init; } = HandlerRegistry.Default;

    public YamlLimits Limits { get; init; } = YamlLimits.Default;
}
