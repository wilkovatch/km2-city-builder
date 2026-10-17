using System.Collections;
using System.Collections.Generic;
using Godot;
public partial class MenuBar : Control {
    // White strip anchored to the top of the screen, 20px tall.
    public const float Height = 20;
    // Entry that draws a horizontal line in a dropdown list instead of an item (its action should be null).
    public const string Divider = null;

    List<MenuBarElement> elements = new List<MenuBarElement>();
    Control panel;
    HBoxContainer row;

    public void Initialize() {
        if (panel != null) return;
        SetAnchorsPreset(Control.LayoutPreset.TopWide);
        OffsetBottom = Height;
        CustomMinimumSize = new Vector2(0, Height);
        panel = new Panel { Name = "Panel" };
        UILayout.SetRect(panel, 0, 1, 1, 1, 0, 0, 0, Height, 0, 1);
        panel.AddThemeStyleboxOverride("panel", UILayout.Flat(Colors.White));
        AddChild(panel);
        // Elements are placed side by side with no gap (x = previous x + previous width).
        row = new HBoxContainer { Name = "MenuItems" };
        row.AddThemeConstantOverride("separation", 0);
        UILayout.Stretch(row);
        panel.AddChild(row);
    }

    public int GetElementCount() {
        return elements.Count;
    }

    public void EnableElement(int i, bool enabled) {
        elements[i].SetInteractable(enabled);
    }

    public void EnableEntry(int i, int entry, bool enabled) {
        elements[i].SetEntryInteractable(entry, enabled);
    }

    public void SetOnOpen(int i, System.Action action) {
        elements[i].SetOnOpen(action);
    }

    public void ShowElement(int i, bool enabled) {
        elements[i].SetActive(enabled);
    }

    public void SetText(int i, string newText) {
        elements[i].SetText(newText);
    }

    public void SetEntries(int i, List<string> entries, List<System.Action> actions) {
        elements[i].SetEntries(entries, actions);
    }

    public void AddElement(string title, List<string> entries, List<System.Action> actions, Color color) {
        var pos = elements.Count == 0 ? 0 : elements[elements.Count - 1].GetNextPosition();
        elements.Add(new MenuBarElement(title, entries, actions, row, pos, color));
    }

    public void SetAsLastSibling() {
        if (panel != null) MoveChild(panel, GetChildCount() - 1);
    }

    public override void _Process(double delta) {
    }
}
