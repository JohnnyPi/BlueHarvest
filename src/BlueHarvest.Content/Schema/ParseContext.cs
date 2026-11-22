#nullable enable
namespace BlueHarvest.Content;

/// <summary>What a reader needs while turning one file's YAML into a definition or manifest.</summary>
internal sealed class ParseContext
{
    public ParseContext(DiagnosticBag diagnostics, string file, string ns)
    {
        Diagnostics = diagnostics;
        File = file;
        Namespace = ns;
    }

    public DiagnosticBag Diagnostics { get; }
    public string File { get; }

    /// <summary>The pack being read: bare IDs in this file take it as their namespace. Empty for a manifest.</summary>
    public string Namespace { get; }

    /// <summary>The qualified ID of the definition being read, once known.</summary>
    public string? DefinitionId { get; set; }

    /// <summary>References found in the file, checked once every file has been read.</summary>
    public List<PendingReference> References { get; } = new();

    /// <summary>How messages name the thing being read.</summary>
    public string Who => DefinitionId ?? "This file";

    public void Error(string code, YamlNode at, string message) =>
        Diagnostics.Error(code, File, at.Line, at.Column, DefinitionId, message);

    public void Warning(string code, YamlNode at, string message) =>
        Diagnostics.Warning(code, File, at.Line, at.Column, DefinitionId, message);

    /// <summary>An error for a ready narrative entry, a warning for a draft (as the Narrative Studio does).</summary>
    public void Incomplete(bool ready, YamlNode at, string message) =>
        Diagnostics.Add(DiagnosticCodes.Incomplete, ready ? ContentSeverity.Error : ContentSeverity.Warning,
            File, at.Line, at.Column, DefinitionId, message);
}

/// <summary>A reference waiting to be resolved against the whole pack set.</summary>
internal sealed record PendingReference(
    ContentId Target, string ExpectedKind, string Field, string File, int Line, int Column, string From, string FromPack);
