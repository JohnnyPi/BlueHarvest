#nullable enable
using System.Collections.Generic;
using BlueHarvest.Game.Ui;
using BlueHarvest.Sim.Actors;

namespace BlueHarvest.Game;

/// <summary>Hunger, thirst, sleep, pain and breath in the log, the sidebar and the menu.</summary>
public partial class Main
{
    private static readonly NeedKind[] ShownNeeds = [NeedKind.Thirst, NeedKind.Hunger, NeedKind.Sleep, NeedKind.Pain, NeedKind.Stamina];

    private void SubscribeToNeedEvents()
    {
        _sim.NeedChanged += OnNeedChanged;
        _sim.FellAsleep += OnFellAsleep;
        _sim.WokeUp += OnWokeUp;
        _sim.NeedBlackedOut += OnNeedBlackedOut;
    }

    private void OnNeedChanged(Actor actor, NeedKind kind, int stage, int was)
    {
        if (actor != _playerActor)
            return;
        string? line = stage > was ? Worse(kind, stage) : Better(kind, stage, was);
        if (line is not null)
            Say(line);
    }

    private static string? Worse(NeedKind kind, int stage) => (kind, stage) switch
    {
        (NeedKind.Hunger, 1) => "Your stomach growls. You're hungry.",
        (NeedKind.Hunger, 2) => "Hunger gnaws at you: you're getting weak.",
        (NeedKind.Hunger, _) => "You're starving. Every step is an effort.",
        (NeedKind.Thirst, 1) => "Your mouth is dry. You're thirsty.",
        (NeedKind.Thirst, 2) => "Your throat is parched: you need water.",
        (NeedKind.Thirst, 3) => "Your head pounds: you're dehydrated. Find water.",
        (NeedKind.Thirst, _) => "You're dying of thirst.",
        (NeedKind.Sleep, 1) => "You're getting tired.",
        (NeedKind.Sleep, 2) => "You're exhausted: you need to sleep.",
        (NeedKind.Sleep, 3) => "You're dead on your feet. Your eyes keep closing.",
        (NeedKind.Sleep, _) => "You can barely stand. Sleep soon, or you'll drop.",
        (NeedKind.Pain, 3) => "The pain is overwhelming.",
        (NeedKind.Stamina, 2) => "You're out of breath: no more sprinting until you get it back.",
        _ => null, // aching and hurting come with the wound's own line; winded is in the sidebar
    };

    private string? Better(NeedKind kind, int stage, int was) => (kind, stage) switch
    {
        (NeedKind.Hunger, 0) => "You've eaten enough.",
        (NeedKind.Thirst, 0) => "Your thirst is gone.",
        (NeedKind.Sleep, 0) => null, // waking rested says so
        (NeedKind.Pain, _) when _playerActor.Needs is { } needs && needs.Relief(_sim.CurrentTick) > 0 && was >= 2 =>
            stage == 0 ? "The pain fades to nothing." : "The pain dulls.",
        (NeedKind.Pain, 0) => "The pain fades.",
        (NeedKind.Stamina, 0) when was == 2 => "You've got your breath back.",
        (NeedKind.Stamina, _) => null,
        (NeedKind.Pain, _) => null,
        (_, _) => $"You feel a little better: {NeedRules.Word(kind, stage).ToLowerInvariant()} now.",
    };

    private void OnFellAsleep(Actor actor)
    {
        if (actor == _playerActor)
            Say("You drift off.");
    }

    private void OnWokeUp(Actor actor, string why)
    {
        if (actor != _playerActor)
            return;
        string line = why switch
        {
            "rested" => "You wake, rested.",
            "pain" => "Pain jolts you awake!",
            "thirst" => "You wake with a raging thirst.",
            "hunger" => "Hunger wakes you.",
            _ when why.StartsWith("a noise: ") => $"You wake with a start: {why["a noise: ".Length..]}.",
            _ => "You wake.",
        };
        if (_rest is not null)
            StopRest(line);
        else
            Say(line);
    }

    private void OnNeedBlackedOut(Actor actor, NeedKind kind)
    {
        if (actor != _playerActor)
            return;
        Say(kind == NeedKind.Sleep
            ? "Your eyes close by themselves... you jerk awake moments later."
            : "Your vision greys and you sway: the thirst is getting to you.");
    }

    /// <summary>The sidebar's Condition lines: asleep, then each need that has something to say, then breath.</summary>
    private string ConditionText()
    {
        if (_playerActor.Needs is not { } needs)
            return "";
        long now = _sim.CurrentTick;
        var lines = new List<string>();
        if (needs.Asleep)
            lines.Add($"[color={HudTheme.Hex(HudTheme.Muted)}]Asleep[/color]");
        foreach (var kind in ShownNeeds)
        {
            int stage = _sim.NeedStage(_playerActor, kind);
            if (stage == 0)
                continue;
            int top = NeedRules.TopStage(kind);
            var color = stage >= top || kind != NeedKind.Stamina && stage >= 3 ? HudTheme.Danger : HudTheme.Amber;
            string extra = kind == NeedKind.Pain && needs.OnAdrenaline(now) ? " (adrenaline)"
                : kind == NeedKind.Pain && needs.OnPainkillers(now) ? " (dulled)"
                : "";
            lines.Add($"[color={HudTheme.Hex(color)}]{NeedRules.Word(kind, stage)}{extra}[/color]");
        }
        if (needs.OnPainkillers(now) && _sim.NeedStage(_playerActor, NeedKind.Pain) == 0 && Needs.WoundPain(_playerActor.Health) > 0)
            lines.Add($"[color={HudTheme.Hex(HudTheme.Muted)}]Painkillers working[/color]");
        if (lines.Count == 0)
            lines.Add($"[color={HudTheme.Hex(HudTheme.Safe)}]Fed, watered and rested[/color]");
        if (needs.Stamina < NeedRules.MaxStamina)
            lines.Add($"[color={HudTheme.Hex(HudTheme.Muted)}]Breath {needs.Stamina / 10}%[/color]");
        return string.Join("\n", lines);
    }

    /// <summary>The menu entry for sleeping, on your own tile.</summary>
    private void AddNeedChoices(List<MenuChoice> choices)
    {
        if (_playerActor.Needs is not { } needs)
            return;
        string? why = _sim.SleepBlock(_playerActor);
        int hours = (needs.Fatigue / NeedRules.SleepRecoveryPerMinute + 59) / 60;
        choices.Add(new MenuChoice("Sleep until rested", StartSleep, why ?? $"about {hours} h", why is null));
    }
}
