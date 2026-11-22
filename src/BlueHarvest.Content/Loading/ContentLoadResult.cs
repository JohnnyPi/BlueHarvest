#nullable enable
namespace BlueHarvest.Content;

/// <summary>
/// What a load produced. <see cref="Registry"/> is set only when there were no errors; the diagnostics are
/// sorted by file and position either way.
/// </summary>
public sealed record ContentLoadResult(ContentRegistry? Registry, IReadOnlyList<ContentDiagnostic> Diagnostics)
{
    public bool Succeeded => Registry is not null;

    public IEnumerable<ContentDiagnostic> Errors => Diagnostics.Where(d => d.Severity == ContentSeverity.Error);

    public bool Has(string code) => Diagnostics.Any(d => d.Code == code);
}
