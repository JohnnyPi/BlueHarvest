#nullable enable
using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;

namespace BlueHarvest.Content;

/// <summary>
/// The frozen result of a successful load: every definition by its qualified ID. Built once at startup and never
/// changed; the game never rereads YAML during play.
/// </summary>
public sealed class ContentRegistry
{
    readonly FrozenDictionary<ContentId, ContentDefinition> byId;

    internal ContentRegistry(IReadOnlyList<PackManifest> packs, IEnumerable<ContentDefinition> definitions,
        IEnumerable<ContentId> notRuntimeReady, HandlerRegistry handlers)
    {
        Packs = Array.AsReadOnly(packs.ToArray());
        Definitions = Array.AsReadOnly(definitions.OrderBy(d => d.Id.Value, StringComparer.Ordinal).ToArray());
        byId = Definitions.ToFrozenDictionary(d => d.Id);
        NotRuntimeReady = notRuntimeReady.ToFrozenSet();
        Handlers = handlers;
        Fingerprint = ContentFingerprint.Compute(Packs, Definitions, handlers);
    }

    /// <summary><c>sha256:</c> and 64 hex digits; see <see cref="ContentFingerprint"/>.</summary>
    public string Fingerprint { get; }

    /// <summary>In load order: dependencies first.</summary>
    public IReadOnlyList<PackManifest> Packs { get; }

    /// <summary>Sorted by ID.</summary>
    public IReadOnlyList<ContentDefinition> Definitions { get; }

    /// <summary>Definitions that name an unknown or unimplemented handler, or are drafts. Empty after a runtime load.</summary>
    public IReadOnlySet<ContentId> NotRuntimeReady { get; }

    public HandlerRegistry Handlers { get; }

    public int Count => Definitions.Count;

    public bool Contains(ContentId id) => byId.ContainsKey(id);

    public bool IsRuntimeReady(ContentId id) => byId.ContainsKey(id) && !NotRuntimeReady.Contains(id);

    public bool TryGet<T>(ContentId id, [NotNullWhen(true)] out T? definition) where T : ContentDefinition
    {
        if (byId.TryGetValue(id, out ContentDefinition? found) && found is T typed)
        {
            definition = typed;
            return true;
        }
        definition = null;
        return false;
    }

    /// <summary>The definition, or <see cref="KeyNotFoundException"/> if there's none of that kind.</summary>
    public T Get<T>(ContentId id) where T : ContentDefinition =>
        TryGet(id, out T? definition) ? definition : throw new KeyNotFoundException($"No {typeof(T).Name} {id} in the content registry.");

    /// <summary>A qualified ID as text (<c>core:ITM-CRF-004</c>).</summary>
    public T Get<T>(string id) where T : ContentDefinition =>
        ContentId.TryParse(id, out ContentId parsed) ? Get<T>(parsed) : throw new ArgumentException($"'{id}' isn't a qualified content ID.", nameof(id));

    /// <summary>All definitions of one type, sorted by ID.</summary>
    public IEnumerable<T> All<T>() where T : ContentDefinition => Definitions.OfType<T>();
}
