using System.Runtime.CompilerServices;
using BlueHarvest.Sim.Actors;
using BlueHarvest.Sim.Tests.ContentLibrary;

namespace BlueHarvest.Sim.Tests;

/// <summary>
/// The sim's items come from content/packs (P2b), so the test assembly installs the repo's packs before any test
/// runs, as the runner and the game do at startup. If they don't load, nothing is installed: every test that makes
/// an item then fails with "No item catalog", and RepoPackTests says what's wrong with the packs.
/// </summary>
internal static class TestContent
{
    /// <summary>Why the packs couldn't be installed, or null.</summary>
    public static Exception? InstallError { get; private set; }

#pragma warning disable CA2255 // a test assembly is the application here: the hook runs before any test
    [ModuleInitializer]
    internal static void InstallItems()
    {
        try
        {
            ItemCatalog.Install(RepoContent.Registry);
        }
        catch (Exception e)
        {
            InstallError = e;
        }
    }
#pragma warning restore CA2255
}
