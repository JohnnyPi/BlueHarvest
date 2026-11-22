#nullable enable
namespace BlueHarvest.Content;

public enum ContentSeverity
{
    /// <summary>The pack set can't load.</summary>
    Error,
    /// <summary>Loads, but probably not what the author meant.</summary>
    Warning,
    /// <summary>Information, such as a definition that isn't runtime-ready yet (authoring mode only).</summary>
    Note,
}

/// <summary>
/// One finding from loading content. <see cref="File"/> is the path inside the content source (forward slashes);
/// <see cref="Line"/> and <see cref="Column"/> are 1-based, 0 when there's no position.
/// </summary>
public sealed record ContentDiagnostic(
    string Code, ContentSeverity Severity, string? File, int Line, int Column, string? DefinitionId, string Message)
{
    public string SeverityLabel => Severity switch
    {
        ContentSeverity.Error => "ERROR",
        ContentSeverity.Warning => "WARNING",
        _ => "NOTE",
    };

    /// <summary>Where it is, as <c>file:line:column</c>, or just the file when there's no position.</summary>
    public string Location => File is null ? "(content)" : Line > 0 ? $"{File}:{Line}:{Column}" : File;

    /// <summary>
    /// Two lines, the way the runner prints it:
    /// <c>BH-CONTENT-014 ERROR core/recipes/twist-vine-cordage.yaml:12:11</c>, then the message, indented.
    /// </summary>
    public override string ToString() => $"{Code} {SeverityLabel} {Location}{Environment.NewLine}  {Message}";
}
