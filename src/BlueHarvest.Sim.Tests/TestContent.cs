using BlueHarvest.Content;
using BlueHarvest.Sim.Actors;
using BlueHarvest.Sim.Tests.ContentLibrary;

namespace BlueHarvest.Sim.Tests;

/// <summary>
/// The sim's items come from content/packs (P2b), read through a session (P3a): every test simulation is made in a
/// session of its own on the repo's packs, as the runner and the game make theirs. If the packs don't load, every
/// test that makes a session fails saying why (RepoPackTests says it too).
/// </summary>
internal static class TestContent
{
    /// <summary>The repo's packs, loaded once.</summary>
    public static ContentRegistry Registry => RepoContent.Registry;

    /// <summary>The repo packs' items: the same catalog every test session uses.</summary>
    public static ItemCatalog Catalog => ItemCatalog.For(Registry);

    /// <summary>A new session on the repo's packs, for one simulation.</summary>
    public static WorldSession NewSession() => new(Registry);
}
