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

    /// <summary>
    /// Loads the packs for play and installs their items (<see cref="Actors.ItemCatalog.Install"/>): what the runner,
    /// the tests and the game do before making a simulation. The load is in authoring mode until P5, because recipes,
    /// sources and devices name handlers that arrive then; the items themselves must all be runtime-ready. The
    /// catalog is installed only if the load succeeded; the result says why if it didn't.
    /// </summary>
    public static ContentLoadResult LoadForPlay(IContentSource source)
    {
        ContentLoadResult result = ContentLoader.LoadFrom(source, Options(ContentMode.Authoring));
        if (result.Registry is { } registry)
            Actors.ItemCatalog.Install(registry);
        return result;
    }
}
