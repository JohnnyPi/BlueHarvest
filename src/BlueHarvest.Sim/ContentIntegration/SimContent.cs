using BlueHarvest.Content;

namespace BlueHarvest.Sim.ContentIntegration;

/// <summary>
/// How the sim loads content: with every handler this build knows (the content library's P5 contracts, which
/// aren't implemented yet, and the item mechanics, which are), and with the game's version. The runner, the
/// tests and Godot all load through this, so they agree on what's runtime-ready and on the fingerprint.
/// </summary>
public static class SimContent
{
    public static HandlerRegistry Handlers { get; } =
        new(HandlerRegistry.Default.Contracts.Concat(ItemHandlers.Contracts));

    public static SemVersion? GameVersion { get; } =
        SemVersion.TryParse(SimInfo.Version, out SemVersion version) ? version : null;

    /// <summary>The options for a load in this mode (authoring or runtime).</summary>
    public static ContentLoadOptions Options(ContentMode mode) => new()
    {
        Mode = mode,
        GameVersion = GameVersion,
        Handlers = Handlers,
    };
}
