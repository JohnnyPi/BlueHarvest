#nullable enable
using System;
using System.Collections.Generic;
using Godot;

namespace BlueHarvest.Game.Ui;

/// <summary>One line in the right-click menu: what it says, an optional hint after it, and what it does.</summary>
public sealed record MenuChoice(string Text, Action Run, string? Hint = null, bool Enabled = true);

/// <summary>
/// The right-click menu for a tile: a title line, then the actions that make sense there
/// ("Walk here  14 steps · 9.8 s", "Throw a flare here", "Tell Marisol: hide"...).
/// </summary>
public partial class ActionMenu : PopupMenu
{
    private readonly List<MenuChoice> _choices = new();

    public ActionMenu()
    {
        IdPressed += OnIdPressed;
    }

    /// <summary>Opens at <paramref name="at"/> (viewport pixels), kept inside <paramref name="screen"/>.</summary>
    public void Open(Vector2 at, Vector2 screen, string title, IReadOnlyList<MenuChoice> choices)
    {
        Clear();
        _choices.Clear();
        AddSeparator(title);
        for (int i = 0; i < choices.Count; i++)
        {
            var choice = choices[i];
            AddItem(choice.Hint is null ? choice.Text : $"{choice.Text}    {choice.Hint}", i);
            SetItemDisabled(GetItemIndex(i), !choice.Enabled);
            _choices.Add(choice);
        }
        ResetSize();
        var size = (Vector2)Size;
        var position = new Vector2(
            Mathf.Clamp(at.X, 0f, Mathf.Max(0f, screen.X - size.X)),
            Mathf.Clamp(at.Y, 0f, Mathf.Max(0f, screen.Y - size.Y)));
        Position = new Vector2I((int)position.X, (int)position.Y);
        Popup();
    }

    private void OnIdPressed(long id)
    {
        if (id >= 0 && id < _choices.Count && _choices[(int)id].Enabled)
            _choices[(int)id].Run();
    }
}
