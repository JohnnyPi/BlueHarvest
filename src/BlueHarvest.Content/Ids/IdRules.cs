#nullable enable
using System.Text.RegularExpressions;

namespace BlueHarvest.Content;

/// <summary>
/// The text forms the content library accepts (contracts §1). Every pattern is anchored with <c>\z</c>, so a
/// trailing newline never slips through. The Narrative Studio (<c>tools/narrative-studio</c>) checks the same
/// patterns; change both together.
/// </summary>
public static class IdRules
{
    /// <summary>The longest pack ID, so a namespace leaves room for a local part inside 64 characters.</summary>
    public const int MaxPackIdLength = 40;

    static readonly Regex Namespace = new(@"^[a-z][a-z0-9-]*(\.[a-z][a-z0-9-]*)*\z", RegexOptions.CultureInvariant);
    static readonly Regex Kebab = new(@"^[a-z0-9]+(-[a-z0-9]+)*\z", RegexOptions.CultureInvariant);
    static readonly Regex Catalog = new(@"^[A-Z]{2,4}(-[A-Z0-9]+)+\z", RegexOptions.CultureInvariant);
    static readonly Regex Handler = new(@"^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)+\z", RegexOptions.CultureInvariant);
    static readonly Regex EventName = new(@"^[a-z][a-z0-9_-]*(\.[a-z][a-z0-9_-]*)+\z", RegexOptions.CultureInvariant);
    static readonly Regex Token = new(@"^[a-z][a-z0-9_]*\z", RegexOptions.CultureInvariant);
    static readonly Regex Fact = new(@"^[A-Z][A-Z0-9]*(-[A-Z0-9]+)*\z", RegexOptions.CultureInvariant);
    static readonly Regex Node = new(@"^[A-Za-z][A-Za-z0-9_-]{0,40}\z", RegexOptions.CultureInvariant);
    static readonly Regex Reference = new(@"^[A-Za-z0-9][A-Za-z0-9._:-]{0,63}\z", RegexOptions.CultureInvariant);
    static readonly Regex Facility = new(@"^[A-Z]{2,4}\z", RegexOptions.CultureInvariant);

    /// <summary>A pack ID and namespace: <c>core</c>, <c>island-mysteries</c>, <c>jdoe.raptor-pack</c>.</summary>
    public static bool IsNamespace(string text) => text.Length <= MaxPackIdLength && Namespace.IsMatch(text);

    /// <summary>A local part: a built-in kebab-case name or an authored catalog ID.</summary>
    public static bool IsLocal(string text) => Kebab.IsMatch(text) || Catalog.IsMatch(text);

    /// <summary>An authored catalog ID such as <c>ITM-TOL-018</c>, as kept in <c>source_ids</c>.</summary>
    public static bool IsCatalogId(string text) => Catalog.IsMatch(text);

    /// <summary>A C# handler key: dotted lower case, <c>process.transform</c>, <c>light.battery</c>.</summary>
    public static bool IsHandler(string text) => Handler.IsMatch(text);

    /// <summary>A world event name: dotted lower case, <c>ecology.plant_regrowth</c>, <c>item.picked-up</c>.</summary>
    public static bool IsEventName(string text) => EventName.IsMatch(text);

    /// <summary>A plain lower-case token: categories, states, predicates.</summary>
    public static bool IsToken(string text) => Token.IsMatch(text);

    /// <summary>A knowledge fact ID: <c>FACT-MEN-ANIMAL-FOREKNOWLEDGE</c>.</summary>
    public static bool IsFact(string text) => text.Length <= ContentId.MaxLength && Fact.IsMatch(text);

    /// <summary>A dialogue node or choice ID, local to its dialogue.</summary>
    public static bool IsNodeId(string text) => Node.IsMatch(text);

    /// <summary>A binding reference that has no registry yet (an NPC, a world object, a site).</summary>
    public static bool IsBindingReference(string text) => Reference.IsMatch(text);

    /// <summary>A facility or region code: <c>MEN</c>, <c>ADM</c>.</summary>
    public static bool IsFacility(string text) => Facility.IsMatch(text);
}
