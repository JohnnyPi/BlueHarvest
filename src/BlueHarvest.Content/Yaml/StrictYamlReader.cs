#nullable enable
using System.Text;
using System.Text.RegularExpressions;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;

namespace BlueHarvest.Content;

/// <summary>
/// Reads one YAML file into a <see cref="YamlNode"/> tree, strictly. Built on YamlDotNet's low-level event parser,
/// never its object deserializer, so YAML can't construct anything. It rejects:
/// files over the size limit; invalid UTF-8; more than one document, or none; explicit tags (<c>!foo</c>,
/// <c>!!str</c>, <c>!</c>); duplicate keys; keys that aren't plain values; nesting past the depth limit;
/// aliases to undefined anchors, and aliases that expand past the node limit (so "billion laughs" stops early).
/// Line breaks are normalised to <c>\n</c> before parsing, so a CRLF checkout reads the same as an LF one.
/// It stops at the first problem in a file and reports it with its line and column.
/// </summary>
internal static class StrictYamlReader
{
    static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
    static readonly Regex MarkPrefix = new(
        @"^\(Line: \d+, Col: \d+, Idx: \d+\) - \(Line: \d+, Col: \d+, Idx: \d+\): ", RegexOptions.CultureInvariant);

    public static YamlNode? Read(byte[] bytes, string path, YamlLimits limits, DiagnosticBag diagnostics)
    {
        if (bytes.Length > limits.MaxFileBytes)
        {
            diagnostics.Error(DiagnosticCodes.FileTooLarge, path, 0, 0, null,
                $"The file is {bytes.Length} bytes; the limit is {limits.MaxFileBytes}.");
            return null;
        }
        string text;
        try
        {
            text = Utf8.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            diagnostics.Error(DiagnosticCodes.NotUtf8, path, 0, 0, null, "The file isn't valid UTF-8.");
            return null;
        }
        if (text.Length > 0 && text[0] == '﻿')
            text = text[1..];
        text = text.Replace("\r\n", "\n").Replace('\r', '\n');

        var builder = new Builder(path, limits, diagnostics);
        var parser = new Parser(new StringReader(text));
        try
        {
            while (parser.MoveNext())
            {
                if (parser.Current is not { } e)
                    continue;
                if (!builder.Accept(e))
                    return null;
            }
        }
        catch (YamlException ex)
        {
            // In case the parser itself ever reports an unknown alias, it keeps the alias code.
            string code = ex.GetType().Name == "AnchorNotFoundException" ? DiagnosticCodes.YamlAlias : DiagnosticCodes.YamlSyntax;
            diagnostics.Error(code, path, (int)ex.Start.Line, (int)ex.Start.Column, null, MarkPrefix.Replace(ex.Message, ""));
            return null;
        }
        return builder.Finish();
    }

    sealed class Builder
    {
        readonly string path;
        readonly YamlLimits limits;
        readonly DiagnosticBag diagnostics;
        readonly Dictionary<string, YamlNode> anchors = new(StringComparer.Ordinal);
        readonly Stack<Frame> stack = new();
        YamlNode? root;
        int documents;
        long nodes;

        public Builder(string path, YamlLimits limits, DiagnosticBag diagnostics)
        {
            this.path = path;
            this.limits = limits;
            this.diagnostics = diagnostics;
        }

        public bool Accept(ParsingEvent e)
        {
            int line = (int)e.Start.Line, column = (int)e.Start.Column;
            switch (e)
            {
                case DocumentStart:
                    documents++;
                    if (documents > 1)
                        return Fail(DiagnosticCodes.DocumentCount, line, column,
                            "The file has more than one YAML document; put one definition in each file.");
                    return true;
                case Scalar s:
                    if (!CheckNode(s, line, column))
                        return false;
                    var scalar = new YamlScalar(s.Value, s.Style == ScalarStyle.Plain, line, column);
                    Remember(s.Anchor, scalar);
                    return Add(scalar);
                case SequenceStart q:
                    if (!CheckNode(q, line, column) || !Push(new Frame(new YamlSequence(line, column), q.Anchor), line, column))
                        return false;
                    return true;
                case MappingStart m:
                    if (!CheckNode(m, line, column) || !Push(new Frame(new YamlMapping(line, column), m.Anchor), line, column))
                        return false;
                    return true;
                case SequenceEnd:
                case MappingEnd:
                {
                    Frame done = stack.Pop();
                    if (done.PendingKey is not null)
                        return Fail(DiagnosticCodes.YamlSyntax, line, column, "A key has no value.");
                    Remember(done.Anchor, done.Node);
                    return Add(done.Node);
                }
                case AnchorAlias a:
                {
                    string name = a.Value.IsEmpty ? "" : a.Value.Value;
                    if (!anchors.TryGetValue(name, out YamlNode? target))
                        return Fail(DiagnosticCodes.YamlAlias, line, column,
                            $"The alias *{name} has no anchor &{name} before it (an alias can't point into its own node).");
                    nodes += target.Weight;
                    if (nodes > limits.MaxNodes)
                        return Fail(DiagnosticCodes.YamlAlias, line, column,
                            $"Aliases expand the file past {limits.MaxNodes} nodes.");
                    return Add(target);
                }
                default:
                    return true; // stream start and end, document end, comments
            }
        }

        public YamlNode? Finish()
        {
            if (root is null)
            {
                diagnostics.Error(DiagnosticCodes.DocumentCount, path, 0, 0, null, "The file is empty.");
                return null;
            }
            return root;
        }

        bool CheckNode(NodeEvent e, int line, int column)
        {
            if (!e.Tag.IsEmpty)
                return Fail(DiagnosticCodes.YamlTag, line, column,
                    $"YAML tags aren't allowed in content (found {e.Tag.Value}).");
            nodes++;
            if (nodes > limits.MaxNodes)
                return Fail(DiagnosticCodes.YamlTooDeep, line, column, $"The file has more than {limits.MaxNodes} nodes.");
            return true;
        }

        bool Push(Frame frame, int line, int column)
        {
            if (stack.Count >= limits.MaxDepth)
                return Fail(DiagnosticCodes.YamlTooDeep, line, column, $"Nested deeper than {limits.MaxDepth} levels.");
            stack.Push(frame);
            return true;
        }

        void Remember(AnchorName anchor, YamlNode node)
        {
            if (!anchor.IsEmpty)
                anchors[anchor.Value] = node;
        }

        bool Add(YamlNode node)
        {
            if (stack.Count == 0)
            {
                root = node;
                return true;
            }
            Frame top = stack.Peek();
            top.Node.Weight = Math.Min(top.Node.Weight + node.Weight, long.MaxValue / 2);
            if (top.Node is YamlSequence sequence)
            {
                sequence.Items.Add(node);
                return true;
            }
            var mapping = (YamlMapping)top.Node;
            if (top.PendingKey is null)
            {
                if (node is not YamlScalar key)
                    return Fail(DiagnosticCodes.ComplexKey, node.Line, node.Column,
                        $"A mapping key must be a plain name, not {node.Describe}.");
                if (mapping.KeyNode(key.Value) is { } first)
                    return Fail(DiagnosticCodes.DuplicateKey, key.Line, key.Column,
                        $"The key '{key.Value}' appears twice in this mapping (first at line {first.Line}).");
                top.PendingKey = key;
                return true;
            }
            mapping.Entries.Add(new KeyValuePair<YamlScalar, YamlNode>(top.PendingKey, node));
            top.PendingKey = null;
            return true;
        }

        bool Fail(string code, int line, int column, string message)
        {
            diagnostics.Error(code, path, line, column, null, message);
            return false;
        }
    }

    sealed class Frame
    {
        public Frame(YamlNode node, AnchorName anchor)
        {
            Node = node;
            Anchor = anchor;
        }

        public YamlNode Node { get; }
        public AnchorName Anchor { get; }
        public YamlScalar? PendingKey { get; set; }
    }
}
