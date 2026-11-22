#nullable enable
namespace BlueHarvest.Content;

/// <summary>
/// The canonical semantic fingerprint of a loaded pack set (YAML plan §5.1 phase 8): the packs and their versions
/// in load order, the handler contracts this build knows, and every definition's meaning, sorted by ID. It doesn't
/// see file paths, file order, whitespace, comments, quoting, key order, or which way a list or duration is written.
/// A save records it and refuses to load against a different one (contracts §8).
/// </summary>
public static class ContentFingerprint
{
    /// <summary>Changes whenever the way the fingerprint is computed changes.</summary>
    public const string Format = "blueharvest.content-fingerprint/1";

    public static string Compute(IReadOnlyList<PackManifest> packs, IReadOnlyList<ContentDefinition> definitions, HandlerRegistry handlers)
    {
        using var w = new CanonicalWriter();
        w.Text(Format);
        w.Count(packs.Count);
        foreach (PackManifest pack in packs)
        {
            w.Text(pack.Id);
            w.Text(pack.Version.ToString());
        }
        w.Count(handlers.Contracts.Count);
        foreach (HandlerContract contract in handlers.Contracts)
        {
            w.Text(contract.Key);
            w.Text(contract.Kind);
            w.Flag(contract.Implemented);
        }
        var sorted = definitions.OrderBy(d => d.Id.Value, StringComparer.Ordinal).ToList();
        w.Count(sorted.Count);
        foreach (ContentDefinition definition in sorted)
            definition.WriteCanonical(w);
        return w.Finish();
    }
}
