namespace BlueHarvest.Sim.World.Generation;

/// <summary>
/// The island generator's version (contracts §3). It goes up whenever generated placements change for any seed, which in
/// practice means whenever a stage-4 golden is re-pinned; a save from another version is refused (P3b). Version 2 is the
/// Crosswake Island site program (P4c) on the P4b terrain anchors.
/// </summary>
public static class IslandGen
{
    public const int Version = 2;

    /// <summary>
    /// A generator-placed thing's <c>WorldInstanceId</c>: <c>wi:&lt;seed, 8 hex digits&gt;:g&lt;version&gt;:&lt;archetype&gt;:&lt;slot&gt;</c>.
    /// key is the archetype and slot, as <see cref="Site.Key"/> gives them ("site.watch:2").
    /// </summary>
    public static string InstanceId(uint seed, string key) =>
        string.Create(System.Globalization.CultureInfo.InvariantCulture, $"wi:{seed:x8}:g{Version}:{key}");
}
