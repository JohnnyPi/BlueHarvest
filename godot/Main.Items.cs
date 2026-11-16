#nullable enable
using System.Collections.Generic;
using System.Linq;
using BlueHarvest.Game.Rendering;
using BlueHarvest.Game.Ui;
using BlueHarvest.Sim;
using BlueHarvest.Sim.Actors;
using BlueHarvest.Sim.World;
using Godot;

namespace BlueHarvest.Game;

/// <summary>Items in the log, the menu, the tooltips and the sidebar's inventory.</summary>
public partial class Main
{
    /// <summary>The most item entries the right-click menu lists (it doesn't scroll).</summary>
    private const int MaxItemChoices = 16;

    private void SubscribeToItemEvents()
    {
        _sim.ItemPickedUp += OnItemPickedUp;
        _sim.ItemDropped += OnItemDropped;
        _sim.ItemStowed += OnItemStowed;
        _sim.ContainerOpened += OnContainerOpened;
        SubscribeToItemUseEvents();
    }

    private void OnItemPickedUp(Actor actor, Item item)
    {
        if (actor == _playerActor)
            Say($"You pick up {item.Describe()}{WhereItWent(item)}.");
        else if (Shown(actor))
            Say($"{actor.Name} picks up {item.Describe()}.");
    }

    private void OnItemDropped(Actor actor, Item item)
    {
        if (actor == _playerActor)
            Say($"You put down {item.Describe()}.");
        else if (Shown(actor))
            Say($"{actor.Name} puts down {item.Describe()}.");
    }

    private void OnItemStowed(Actor actor, Item item, Item container)
    {
        if (actor == _playerActor)
            Say($"You put {item.Describe()} in the {container.Def.Name}.");
    }

    private void OnContainerOpened(Actor actor, Item container, bool open)
    {
        if (actor != _playerActor)
            return;
        if (!open)
        {
            Say($"You shut the {container.Def.Name}.");
            return;
        }
        Say(container.Contents.Count == 0
            ? $"You open the {container.Def.Name}. It's empty."
            : $"You open the {container.Def.Name}: {Listing(container.Contents)}.");
    }

    /// <summary>Where a just-picked-up item went, for the log: " (in your daypack)", " (in a pocket)".</summary>
    private string WhereItWent(Item item)
    {
        var inventory = _playerActor.Inventory;
        if (inventory.HolderOf(item) is { } bag)
            return $" (in your {bag.Def.Name})";
        return inventory.SlotOf(item) switch
        {
            CarrySlot.Pocket => " (in a pocket)",
            CarrySlot.BothHands => ", both hands full",
            CarrySlot.Worn => $" and put it on",
            CarrySlot.RightHand or CarrySlot.LeftHand => " (in your hand)",
            _ => "", // joined a stack
        };
    }

    /// <summary>Where an item would go if picked up, as a menu hint.</summary>
    private static string PlacementHint(Placement placement) => placement switch
    {
        { Refused: { } why } => why,
        { Into: { IsContainer: true } bag } => $"into your {bag.Def.Name}",
        { Into: not null } => "with the others",
        { Slot: CarrySlot.Pocket } => "in a pocket",
        { Slot: CarrySlot.Worn } => "wear it",
        { Slot: CarrySlot.BothHands } => "both hands",
        _ => "in your hand",
    };

    /// <summary>"a knife, 3 energy bars and a water bottle (1000 ml)".</summary>
    private static string Listing(IEnumerable<Item> items)
    {
        var words = items.Select(i => i.Describe()).ToList();
        return words.Count switch
        {
            0 => "nothing",
            1 => words[0],
            _ => string.Join(", ", words.Take(words.Count - 1)) + " and " + words[^1],
        };
    }

    /// <summary>The menu's item entries for a tile within reach: open and shut, pick up, take out, search a body, drop, put in.</summary>
    private void AddItemChoices(List<MenuChoice> choices, TilePos tile, int reach)
    {
        if (reach > 1)
            return;
        var me = _playerActor;
        var inventory = me.Inventory;
        var items = new List<MenuChoice>();

        foreach (var r in _sim.ItemsWithinReach(me).Where(x => x.At == tile))
        {
            var item = r.Item;
            var def = item.Def;
            var placement = inventory.PlaceFor(item);
            if (r.On is { } body)
            {
                items.Add(new MenuChoice($"Take {item.Describe()} from {body.Name}'s body", () => Submit(new PickUpAction(item.Id)),
                    PlacementHint(placement), placement.Ok));
                continue;
            }
            if (r.In is { } holder)
            {
                items.Add(new MenuChoice($"Take {item.Describe()} from the {holder.Def.Name}", () => Submit(new PickUpAction(item.Id)),
                    PlacementHint(placement), placement.Ok));
                continue;
            }
            if (def.Holds is { Closes: true })
            {
                if (item.IsOpen)
                    items.Add(new MenuChoice($"Shut the {def.Name}", () => Submit(new OpenAction(item.Id))));
                else
                    items.Add(new MenuChoice($"Open the {def.Name}", () => Submit(new OpenAction(item.Id)),
                        item.Locked ? LockHint(item) : null, !item.Locked || Simulation.KeyFor(me, item) is not null));
                AddLockChoices(items, item);
            }
            if (def.Size != ItemSize.Fixed)
                items.Add(new MenuChoice($"Pick up {item.Describe()}", () => Submit(new PickUpAction(item.Id)),
                    PlacementHint(placement), placement.Ok));

            // into an open container here: what you hold or have in your pockets
            if (item.IsAccessible)
            {
                foreach (var (held, _) in inventory.Carried.Where(c => c.Slot != CarrySlot.Worn))
                {
                    string? why = item.RefusesToHold(held);
                    items.Add(new MenuChoice($"Put {held.Describe()} in the {def.Name}", () => Submit(new PutAction(held.Id, item.Id)),
                        why, why is null));
                }
            }
        }

        if (tile == me.Position)
        {
            AddCarriedChoices(items);

            // what you hold or have loose in your pockets can go in a bag you wear
            var bag = inventory.Carried.FirstOrDefault(c => c.Slot == CarrySlot.Worn && c.Item.IsContainer).Item;
            foreach (var (held, _) in inventory.Carried.Where(c => c.Slot != CarrySlot.Worn))
            {
                if (bag is not null && bag.RefusesToHold(held) is null)
                    items.Add(new MenuChoice($"Put {held.Describe()} in your {bag.Def.Name}", () => Submit(new PutAction(held.Id, bag.Id))));
            }
            // not what's fitted into something (a flashlight's batteries): take that out first
            foreach (var carried in inventory.All.Where(i => inventory.HolderOf(i) is not { Def.Holds.Only: not null }))
                items.Add(new MenuChoice($"Put down {carried.Describe()}", () => Submit(new DropAction(carried.Id)),
                    inventory.HolderOf(carried) is { } holder ? $"from your {holder.Def.Name}" : null));
        }

        choices.AddRange(items.Take(MaxItemChoices));
        if (items.Count > MaxItemChoices)
            choices.Add(new MenuChoice($"… and {items.Count - MaxItemChoices} more", () => { }, "take or put down some first", false));
    }

    /// <summary>Tooltip lines about items on a tile: what lies there, what's in an open container, a body to search.</summary>
    private void AddItemFacts(List<(string Text, Color? Color)> facts, TilePos tile)
    {
        var state = _map.FogAt(tile);
        if (state is not (FogState.Visible or FogState.Remembered) || _map.RoofShown(tile))
            return;
        var lying = _sim.ItemsAt(tile).Where(l => l.OnDeck == _playerActor.OnDeck || !_sim.Map.Structures.HasDeck(tile)).ToList();
        var loose = lying.Where(l => !l.Item.Def.Is(ItemCategory.Fixture)).Select(l => l.Item).ToList();
        foreach (var fixture in lying.Select(l => l.Item).Where(i => i.Def.Is(ItemCategory.Fixture)))
        {
            string text = Capitalize(fixture.Describe());
            if (fixture.Def.Holds is { Closes: true } && !fixture.IsOpen)
                text += fixture.Locked ? "" : ", shut";
            else
                text += fixture.Contents.Count == 0 ? ", empty" : $": {Listing(fixture.Contents)}";
            facts.Add((text, HudTheme.Ink));
        }
        if (loose.Count > 0)
            facts.Add(($"On the ground: {Listing(loose)}", HudTheme.Ink));
        foreach (var item in loose.Where(i => i.IsContainer && i.IsAccessible && i.Contents.Count > 0))
            facts.Add(($"In the {item.Def.Name}: {Listing(item.Contents)}", null));
        var body = _sim.Actors.FirstOrDefault(a => a.IsDead && !a.LeftBehind && a.Position == tile && a.Inventory.Carried.Count > 0);
        if (body is not null && body != _playerActor)
            facts.Add(("Something on the body: right-click to search it", HudTheme.Muted));
        if (lying.Count > 0 && Chebyshev(_playerActor.Position, tile) <= 1)
            facts.Add(("Right-click to take or open", HudTheme.Muted));
    }

    /// <summary>The sidebar's inventory: hands, pockets, what's worn and in it, and the weight.</summary>
    private string InventoryText()
    {
        var me = _playerActor;
        var inventory = me.Inventory;
        string muted = HudTheme.Hex(HudTheme.Muted);
        string KeyCap(string k) => $"[color={muted}][lb]{k}[rb][/color]";
        bool lightOn = me.CarriedLight is { } light && _sim.Lights[light].On;

        var lines = new List<string>();
        var keys = new List<string>
        {
            $"{KeyCap("T")} kit {inventory.Count(ItemKind.FirstAidKit)}",
            $"{KeyCap("F")} flare {inventory.Count(ItemKind.Flare)}",
        };
        if (inventory.Has(ItemKind.Flashlight))
            keys.Add($"{KeyCap("X")} light {(lightOn ? $"[color={HudTheme.Hex(HudTheme.Amber)}]on[/color]" : "off")}");
        if (inventory.Has(ItemKind.Rope))
            keys.Add($"{KeyCap("R")} rope");
        lines.Add(string.Join("  ", keys));

        string Items(IEnumerable<Item> items) => HudTheme.Escape(Listing(items));
        var hands = inventory.Carried.Where(c => c.Slot is CarrySlot.RightHand or CarrySlot.LeftHand or CarrySlot.BothHands).Select(c => c.Item).ToList();
        var pockets = inventory.Carried.Where(c => c.Slot == CarrySlot.Pocket).Select(c => c.Item).ToList();
        lines.Add($"[color={muted}]Hands[/color]  {(hands.Count == 0 ? $"[color={muted}]free[/color]" : Items(hands))}");
        if (pockets.Count > 0)
            lines.Add($"[color={muted}]Pockets[/color]  {Items(pockets)}");
        foreach (var (worn, _) in inventory.Carried.Where(c => c.Slot == CarrySlot.Worn))
        {
            string where = worn.Def.Wear switch { WearSlot.Back => "Back", WearSlot.Waist => "Belt", _ => "Worn" };
            string text = HudTheme.Escape(worn.Def.Name);
            if (worn.Def.Holds is { } holds)
                text += $" [color={muted}]{worn.ContentsMl / 1000.0:0.#}/{holds.CapacityMl / 1000.0:0.#} L[/color]"
                    + (worn.Contents.Count > 0 ? $": {Items(worn.Contents)}" : $" [color={muted}](empty)[/color]");
            lines.Add($"[color={muted}]{where}[/color]  {text}");
        }

        int grams = inventory.Grams;
        string weight = $"{grams / 1000.0:0.0} kg";
        lines.Add(inventory.Load switch
        {
            BlueHarvest.Sim.Actors.Load.Heavy => $"[color={HudTheme.Hex(HudTheme.Danger)}]{weight}: heavily laden, no sprinting or climbing[/color]",
            BlueHarvest.Sim.Actors.Load.Burdened => $"[color={HudTheme.Hex(HudTheme.Amber)}]{weight}: burdened, sprinting is harder[/color]",
            _ => $"[color={muted}]{weight} carried · {KeyCap("Shift")} sprint[/color]",
        });
        return string.Join("\n", lines);
    }
}
