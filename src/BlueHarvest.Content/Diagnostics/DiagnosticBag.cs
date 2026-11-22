#nullable enable
namespace BlueHarvest.Content;

/// <summary>Collects diagnostics while loading. Not thread-safe; one bag per load.</summary>
internal sealed class DiagnosticBag
{
    readonly List<ContentDiagnostic> items = new();

    public int ErrorCount { get; private set; }

    public void Add(string code, ContentSeverity severity, string? file, int line, int column, string? definitionId, string message)
    {
        items.Add(new ContentDiagnostic(code, severity, file, line, column, definitionId, message));
        if (severity == ContentSeverity.Error)
            ErrorCount++;
    }

    public void Error(string code, string? file, int line, int column, string? definitionId, string message) =>
        Add(code, ContentSeverity.Error, file, line, column, definitionId, message);

    public void Warning(string code, string? file, int line, int column, string? definitionId, string message) =>
        Add(code, ContentSeverity.Warning, file, line, column, definitionId, message);

    /// <summary>Sorted by file, then position, then code, so the same content always reports the same way.</summary>
    public IReadOnlyList<ContentDiagnostic> Sorted() =>
        items
            .Select((d, i) => (d, i))
            .OrderBy(p => p.d.File ?? "", StringComparer.Ordinal)
            .ThenBy(p => p.d.Line)
            .ThenBy(p => p.d.Column)
            .ThenBy(p => p.d.Code, StringComparer.Ordinal)
            .ThenBy(p => p.i)
            .Select(p => p.d)
            .ToList();
}
