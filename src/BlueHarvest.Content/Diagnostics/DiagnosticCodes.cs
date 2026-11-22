#nullable enable
namespace BlueHarvest.Content;

/// <summary>
/// Every diagnostic code the loader emits. Codes are stable: a new check takes a new number, and a retired one
/// is never reused.
/// </summary>
public static class DiagnosticCodes
{
    // Reading a file: 001-009.
    /// <summary>The file is over the size limit, or the source couldn't give it.</summary>
    public const string FileTooLarge = "BH-CONTENT-001";
    /// <summary>The file isn't valid UTF-8.</summary>
    public const string NotUtf8 = "BH-CONTENT-002";
    /// <summary>YAML syntax error, as reported by the parser.</summary>
    public const string YamlSyntax = "BH-CONTENT-003";
    /// <summary>An explicit YAML tag (<c>!foo</c>, <c>!!str</c>); content never uses tags.</summary>
    public const string YamlTag = "BH-CONTENT-004";
    /// <summary>An alias to an anchor that isn't defined yet, or aliases that expand past the node limit.</summary>
    public const string YamlAlias = "BH-CONTENT-005";
    /// <summary>The same key twice in one mapping.</summary>
    public const string DuplicateKey = "BH-CONTENT-006";
    /// <summary>Nested deeper than the depth limit, or more nodes than the node limit.</summary>
    public const string YamlTooDeep = "BH-CONTENT-007";
    /// <summary>An empty file, or more than one YAML document in a file.</summary>
    public const string DocumentCount = "BH-CONTENT-008";
    /// <summary>A mapping key that isn't a plain value (a list or mapping used as a key).</summary>
    public const string ComplexKey = "BH-CONTENT-009";

    // Shape of a definition: 010-013.
    /// <summary><c>schema</c> or <c>kind</c> missing or not one the loader knows.</summary>
    public const string Envelope = "BH-CONTENT-010";
    /// <summary>A field the schema doesn't have.</summary>
    public const string UnknownField = "BH-CONTENT-011";
    /// <summary>A required field is missing or empty.</summary>
    public const string MissingField = "BH-CONTENT-012";
    /// <summary>A field has the wrong type, or a value outside its allowed range or set.</summary>
    public const string BadValue = "BH-CONTENT-013";

    // Identity and references: 014-021.
    /// <summary>A reference to an ID nothing defines.</summary>
    public const string UnresolvedReference = "BH-CONTENT-014";
    /// <summary>Text that isn't a valid content ID.</summary>
    public const string BadId = "BH-CONTENT-015";
    /// <summary>Two definitions with the same ID.</summary>
    public const string DuplicateId = "BH-CONTENT-016";
    /// <summary>A reference to a definition of the wrong kind (a recipe used as an item).</summary>
    public const string WrongKind = "BH-CONTENT-017";
    /// <summary>A reference into a pack that isn't one of the referring pack's dependencies.</summary>
    public const string UndeclaredDependency = "BH-CONTENT-018";
    /// <summary>A definition in another pack's namespace that the manifest doesn't list in <c>replaces</c>.</summary>
    public const string ForeignNamespace = "BH-CONTENT-019";
    /// <summary>A known handler used on the wrong kind of definition, or with data its contract rejects.</summary>
    public const string HandlerMismatch = "BH-CONTENT-020";
    /// <summary>NOT_RUNTIME_READY: an unknown or unimplemented handler, or a draft. A note when authoring, an error at run time.</summary>
    public const string NotRuntimeReady = "BH-CONTENT-021";

    // Narrative: 022-023.
    /// <summary>A broken dialogue graph: a missing start or target node, a repeated node or choice ID.</summary>
    public const string DialogueGraph = "BH-CONTENT-022";
    /// <summary>A dialogue graph that loads but looks wrong: unreachable nodes, ignored choices, dead ends.</summary>
    public const string DialogueShape = "BH-CONTENT-023";
    /// <summary>A narrative entry that isn't finished (no title, no body). An error for ready entries, a warning for drafts.</summary>
    public const string Incomplete = "BH-CONTENT-024";

    // Packs: 030-039.
    /// <summary>No pack manifest found, or a manifest that can't be read.</summary>
    public const string Manifest = "BH-CONTENT-030";
    /// <summary>Two packs with the same ID.</summary>
    public const string DuplicatePack = "BH-CONTENT-031";
    /// <summary>A dependency that isn't in the pack set, or a pack skipped because one of its dependencies failed.</summary>
    public const string MissingDependency = "BH-CONTENT-032";
    /// <summary>Packs that depend on each other in a cycle.</summary>
    public const string DependencyCycle = "BH-CONTENT-033";
    /// <summary>A dependency whose version is outside the range asked for.</summary>
    public const string DependencyVersion = "BH-CONTENT-034";
    /// <summary>An unsafe path (<c>..</c>, absolute), or a YAML file outside every content root.</summary>
    public const string ContentPath = "BH-CONTENT-035";
    /// <summary>The pack's <c>requires_game</c> doesn't include this game version.</summary>
    public const string GameVersion = "BH-CONTENT-036";
    /// <summary>A <c>replaces</c> entry that doesn't work: no target, not a dependency's, wrong kind, replaced twice, unused.</summary>
    public const string Replace = "BH-CONTENT-037";
}
