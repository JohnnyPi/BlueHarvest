#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace BlueHarvest.Game.Ui;

/// <summary>Which corner of the map the log sits in.</summary>
public enum LogCorner
{
    LowerLeft,
    UpperLeft,
}

/// <summary>
/// The running commentary in its own fixed panel, lower left or upper left of the map. It keeps the
/// whole run and scrolls; lines from the latest turn are bright, older ones muted.
/// </summary>
public partial class LogPanel : PanelContainer
{
    public const float PanelWidth = 470f;
    public const float PanelHeight = 160f;
    public const float Margin = 10f;
    private const int MaxEntries = 300;

    private readonly List<(string Clock, string Text, int Turn)> _entries = new();
    private readonly RichTextLabel _text;
    private readonly Label _count;
    private readonly Button _move;
    private int _latestTurn;

    /// <summary>Raised when the player asks to move the panel to the other corner.</summary>
    public event Action? MoveRequested;

    public LogCorner Corner { get; private set; }

    public LogPanel()
    {
        MouseFilter = MouseFilterEnum.Stop;
        AddThemeStyleboxOverride("panel", HudTheme.Box(HudTheme.PanelSoft, HudTheme.Line, 4, 8));

        var column = HudTheme.Column(4);
        AddChild(column);

        var header = new HBoxContainer();
        var title = HudTheme.Heading("Log");
        title.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        title.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        _count = HudTheme.Text("", HudTheme.HeadingSize, HudTheme.Muted);
        _count.AutowrapMode = TextServer.AutowrapMode.Off;
        _count.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        _move = HudTheme.PlainButton("");
        _move.Flat = true;
        _move.TooltipText = "Move the log to the other corner";
        _move.Pressed += () => MoveRequested?.Invoke();
        header.AddChild(title);
        header.AddChild(_count);
        header.AddChild(_move);
        column.AddChild(header);

        _text = new RichTextLabel
        {
            BbcodeEnabled = true,
            ScrollFollowing = true,
            SelectionEnabled = false,
            FocusMode = FocusModeEnum.None,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            SizeFlagsVertical = SizeFlags.ExpandFill,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        _text.AddThemeFontSizeOverride("normal_font_size", HudTheme.SmallSize + 1);
        column.AddChild(_text);
        SetCorner(LogCorner.LowerLeft);
    }

    /// <summary>Adds a line. Lines added with the same turn number count as "what just happened".</summary>
    public void Add(string clock, string text, int turn)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;
        if (_entries.Count > 0 && _entries[^1].Text == text && _entries[^1].Turn == turn)
            return;
        _entries.Add((clock, text.Trim(), turn));
        if (_entries.Count > MaxEntries)
            _entries.RemoveRange(0, _entries.Count - MaxEntries);
        _latestTurn = turn;
        Rebuild();
    }

    public void SetCorner(LogCorner corner)
    {
        Corner = corner;
        AnchorLeft = 0f;
        AnchorRight = 0f;
        OffsetLeft = Margin;
        OffsetRight = Margin + PanelWidth;
        if (corner == LogCorner.LowerLeft)
        {
            AnchorTop = 1f;
            AnchorBottom = 1f;
            OffsetTop = -Margin - PanelHeight;
            OffsetBottom = -Margin;
            _move.Text = "move up";
        }
        else
        {
            AnchorTop = 0f;
            AnchorBottom = 0f;
            OffsetTop = Margin;
            OffsetBottom = Margin + PanelHeight;
            _move.Text = "move down";
        }
    }

    private void Rebuild()
    {
        string amber = HudTheme.Hex(HudTheme.Amber);
        string muted = HudTheme.Hex(HudTheme.Muted);
        string ink = HudTheme.Hex(HudTheme.Ink);
        _text.Text = string.Join("\n", _entries.Select(e =>
            $"[color={amber}]{e.Clock}[/color]  [color={(e.Turn == _latestTurn ? ink : muted)}]{HudTheme.Escape(e.Text)}[/color]"));
        _count.Text = _entries.Count == 1 ? "1 entry  " : $"{_entries.Count} entries  ";
        Callable.From(() => _text.ScrollToLine(Math.Max(0, _text.GetLineCount() - 1))).CallDeferred();
    }
}
