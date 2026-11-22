using BlueHarvest.Content;
using BlueHarvest.Sim.ContentIntegration;

namespace BlueHarvest.Sim.Tests.ContentLibrary;

/// <summary>The repo's content/packs, loaded the way the game loads it (SimContent's handlers and version).</summary>
internal static class RepoContent
{
    public static readonly string Root = FindRoot();
    public static readonly string Packs = Path.Combine(Root, "content", "packs");

    static readonly Lazy<ContentLoadResult> Authoring = new(() => Load(ContentMode.Authoring));

    static string FindRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "BlueHarvest.sln")))
                return dir.FullName;
        throw new InvalidOperationException("Can't find BlueHarvest.sln above the test folder.");
    }

    public static ContentLoadResult Load(ContentMode mode) => Load(SimContent.Options(mode));

    public static ContentLoadResult Load(ContentLoadOptions options) =>
        ContentLoader.LoadFrom(new FileSystemContentSource(Packs), options);

    /// <summary>The authoring load, once per test run; it must succeed.</summary>
    public static ContentRegistry Registry
    {
        get
        {
            TestPacks.AssertLoads(Authoring.Value);
            return Authoring.Value.Registry!;
        }
    }
}
