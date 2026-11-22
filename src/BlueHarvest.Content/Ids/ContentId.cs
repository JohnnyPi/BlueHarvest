#nullable enable
namespace BlueHarvest.Content;

/// <summary>
/// The identity of an authored definition (contracts §1): always qualified, <c>namespace:local</c>, where the
/// namespace is the ID of the pack that defines it. Case-sensitive, at most 64 characters, never reused.
/// Build one with <see cref="TryParse"/> or <see cref="TryResolve"/>; the constructor doesn't check the form.
/// </summary>
public readonly record struct ContentId(string Value) : IComparable<ContentId>
{
    public const int MaxLength = 64;

    /// <summary>The pack namespace, the part before the colon.</summary>
    public string Namespace => Value[..Value.IndexOf(':')];

    /// <summary>The local part, after the colon.</summary>
    public string Local => Value[(Value.IndexOf(':') + 1)..];

    public int CompareTo(ContentId other) => string.CompareOrdinal(Value, other.Value);

    public override string ToString() => Value;

    /// <summary>Parses a qualified ID (<c>core:ITM-CRF-004</c>). Unqualified text fails.</summary>
    public static bool TryParse(string? text, out ContentId id)
    {
        id = default;
        if (text is null || text.Length > MaxLength)
            return false;
        int colon = text.IndexOf(':');
        if (colon <= 0 || text.IndexOf(':', colon + 1) >= 0)
            return false;
        if (!IdRules.IsNamespace(text[..colon]) || !IdRules.IsLocal(text[(colon + 1)..]))
            return false;
        id = new ContentId(text);
        return true;
    }

    /// <summary>
    /// Parses an ID as written in YAML: qualified, or bare, in which case it takes <paramref name="defaultNamespace"/>
    /// (the pack being read). The result is always qualified.
    /// </summary>
    public static bool TryResolve(string? text, string defaultNamespace, out ContentId id)
    {
        if (text is not null && text.IndexOf(':') < 0)
            return TryParse(defaultNamespace + ":" + text, out id);
        return TryParse(text, out id);
    }
}
