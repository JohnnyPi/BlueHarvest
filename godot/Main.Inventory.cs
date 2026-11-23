#nullable enable
using System.Collections.Generic;
using System.Linq;
using BlueHarvest.Game.Ui;
using BlueHarvest.Sim;
using BlueHarvest.Sim.Actors;
using Godot;
using BlueHarvest.Content;
using BlueHarvest.Sim.ContentIntegration;

namespace BlueHarvest.Game;

/// <summary>
/// The inventory panel (I): what you carry, and what you can do with each thing. Everything you do
/// with carried items is here now (use, switch on, combine, take out, put in a bag, put down); the
/// right-click menu on your own tile keeps what's about the place (what lies there, water, fire, sleep).
/// </summary>
public partial class Main
{
    private InventoryPanel _inventory = null!;

    private void BuildInventoryPanel(Control root)
    {
        _inventory = new InventoryPanel();
        _inventory.ItemClicked += OnInventoryItemClicked;
        root.AddChild(_inventory);
    }

    private void ToggleInventory()
    {
        _inventory.Visible = !_inventory.Visible;
        RefreshInventory();
    }

    /// <summary>Redraws the panel if it's open (after every turn).</summary>
    private void RefreshInventory()
    {
        if (_inventory.Visible)
            _inventory.Display(InventoryRows(), InventoryFooter());
    }

    private List<InventoryRow> InventoryRows()
    {
        var inventory = _playerActor.Inventory;
        var rows = new List<InventoryRow>();
        void Add(Item item, int indent)
        {
            rows.Add(new InventoryRow(Capitalize(item.Describe()), item.Id, indent, item.Lit ? HudTheme.Amber : null));
            if (item.Def.Holds is { Only: null } && item.IsContainer)
                foreach (var inner in item.Contents)
                    Add(inner, indent + 1);
        }
        void Section(string title, IEnumerable<Item> items)
        {
            var list = items.ToList();
            if (list.Count == 0)
                return;
            rows.Add(new InventoryRow(title));
            foreach (var item in list)
                Add(item, 1);
        }
        var carried = inventory.Carried;
        var hands = carried.Where(c => c.Slot is CarrySlot.RightHand or CarrySlot.LeftHand or CarrySlot.BothHands).Select(c => c.Item).ToList();
        if (hands.Count == 0)
            rows.Add(new InventoryRow("Hands: free"));
        else
            Section("Hands", hands);
        Section("Pockets", carried.Where(c => c.Slot == CarrySlot.Pocket).Select(c => c.Item));
        Section("Worn", carried.Where(c => c.Slot == CarrySlot.Worn).Select(c => c.Item));
        return rows;
    }

    private string InventoryFooter()
    {
        var inventory = _playerActor.Inventory;
        string weight = $"{inventory.Grams / 1000.0:0.0} kg";
        return inventory.Load switch
        {
            BlueHarvest.Sim.Actors.Load.Heavy => $"[color={HudTheme.Hex(HudTheme.Danger)}]{weight}: heavily laden, no sprinting or climbing[/color]",
            BlueHarvest.Sim.Actors.Load.Burdened => $"[color={HudTheme.Hex(HudTheme.Amber)}]{weight}: burdened, sprinting is harder[/color]",
            _ => $"[color={HudTheme.Hex(HudTheme.Muted)}]{weight} carried[/color]",
        };
    }

    private void OnInventoryItemClicked(int itemId, Vector2 at)
    {
        if (_playerActor.Inventory.Find(itemId) is not { } item || _mode != InputMode.Normal)
            return;
        var choices = ItemActions(item);
        if (choices.Count == 0)
            choices.Add(new MenuChoice("Nothing to do with it here", () => { }, null, false));
        _menu.Open(at, GetViewportRect().Size, Capitalize(item.Describe()), choices);
    }

    /// <summary>Everything you can do with one carried item.</summary>
    private List<MenuChoice> ItemActions(Item item)
    {
        var inventory = _playerActor.Inventory;
        var def = item.Def;
        var choices = new List<MenuChoice>();

        // use it
        if (def.Has(ItemHandlers.Flashlight))
        {
            bool on = _playerActor.CarriedLight is { } light && _sim.Lights[light].On;
            choices.Add(new MenuChoice(on ? "Switch the flashlight off" : "Switch the flashlight on", () => Submit(new ToggleLightAction()), "X"));
        }
        else if (Recipes.UseOf(def) is { } use)
        {
            choices.Add(UseChoice(item, use));
        }

        // with each other thing carried
        var seen = new HashSet<(ContentId, ContentId, int)>();
        foreach (var other in inventory.All)
        {
            if (other == item || Recipes.Of(item, other) is not { } c || !seen.Add((c.From.Kind, c.Into.Kind, c.Into.Id)))
                continue;
            if (inventory.HolderOf(c.From) is { Def.Holds.Only: not null })
                continue; // already fitted
            string? why = c.Kind switch
            {
                CombineKind.Load => c.Into.Amount >= c.Into.Def.MaxAmount ? "full" : null,
                CombineKind.Fit => c.Into.RefusesToHold(c.From),
                _ => null,
            };
            var (a, b) = (item, other);
            choices.Add(new MenuChoice(CombineLabel(c), () => Submit(new CombineAction(a.Id, b.Id)), why, why is null));
        }

        // out of what it's in, or unloaded
        if (def.Fills is not null && item.Amount > 0)
            choices.Add(new MenuChoice($"Unload the {def.Name}", () => Submit(new UnloadAction(item.Id)), $"{item.Amount} {def.Unit}"));
        if (inventory.HolderOf(item) is { } holder)
            choices.Add(new MenuChoice($"Take it out of the {holder.Def.Name}", () => Submit(new TakeOutAction(item.Id))));

        // into a bag you wear
        foreach (var (bag, slot) in inventory.Carried)
        {
            if (slot != CarrySlot.Worn || !bag.IsContainer || bag == item || bag.Def.Holds is { Only: not null } || inventory.HolderOf(item) == bag)
                continue;
            string? why = bag.RefusesToHold(item);
            choices.Add(new MenuChoice($"Put it in your {bag.Def.Name}", () => Submit(new PutAction(item.Id, bag.Id)), why, why is null));
        }

        // down (not something fitted into another: take that out first)
        if (inventory.HolderOf(item) is not { Def.Holds.Only: not null })
            choices.Add(new MenuChoice("Put it down", () => Submit(new DropAction(item.Id)),
                item.Lit ? "it keeps shining where it lies" : null));
        return choices;
    }

    /// <summary>The use entry for an item, with its state: switch on or off, crack, light, eat, drink...</summary>
    private MenuChoice UseChoice(Item item, ItemUse use)
    {
        var def = item.Def;
        switch (use)
        {
            case ItemUse.SwitchLight:
            {
                if (item.Lit)
                    return new MenuChoice($"Switch the {def.Name} off", () => Submit(new UseItemAction(item.Id)));
                string? why = item.Contents.Count == 0 ? "no batteries" : item.Charged ? null : "batteries flat";
                int charge = item.Contents.Count == 0 ? 0 : item.Contents.Min(b => b.Amount);
                return new MenuChoice($"Switch the {def.Name} on", () => Submit(new UseItemAction(item.Id)), why ?? $"{charge}%", why is null);
            }
            case ItemUse.Crack:
                return new MenuChoice("Crack a glow stick", () => Submit(new UseItemAction(item.Id)),
                    item.Lit ? "glowing" : item.Spent ? "spent" : "8 h of light", !item.Lit && !item.Spent);
            case ItemUse.LightTorch:
            {
                var inventory = _playerActor.Inventory;
                bool flame = inventory.All.Any(i => i.Def.Has(ItemHandlers.Flame) && i.Amount > 0 || i.Def.Has(ItemHandlers.Torch) && i.Lit);
                string? why = item.Lit ? "burning" : item.Spent ? "burnt out" : flame ? null : "nothing to light it with";
                return new MenuChoice("Light the torch", () => Submit(new UseItemAction(item.Id)), why ?? "30 min of light", why is null);
            }
            default:
            {
                bool empty = !def.Stacks && def.Unit is not null && item.Amount == 0;
                return new MenuChoice(UseLabel(item, use), () => Submit(new UseItemAction(item.Id)), empty ? "empty" : UseHint(item, use), !empty);
            }
        }
    }
}
