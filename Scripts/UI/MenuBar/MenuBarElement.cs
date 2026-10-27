using System.Collections;
using System.Collections.Generic;
using Godot;

public class MenuBarElement {
    MenuButton obj;
    List<System.Action> actions;
    PopupMenu dropdown;

    public MenuBarElement(string text, List<string> options, List<System.Action> actions, Control parent, float pos, Color color) {
        this.actions = actions;
        // Flat white button, 14px label centred, 20px tall and
        // as wide as the label's preferred width + 20 (rounded down to an even number).
        obj = new MenuButton { Text = text, Flat = false, Alignment = HorizontalAlignment.Center, ClipText = false };
        obj.AddThemeFontSizeOverride("font_size", 14);
        obj.AddThemeColorOverride("font_color", color);
        obj.AddThemeColorOverride("font_hover_color", color);
        obj.AddThemeColorOverride("font_hover_pressed_color", color);
        obj.AddThemeColorOverride("font_pressed_color", color);
        obj.AddThemeColorOverride("font_focus_color", color);
        obj.AddThemeColorOverride("font_disabled_color", new Color(color, color.A * 0.5f));
        obj.AddThemeStyleboxOverride("normal", UILayout.Flat(Colors.White));
        var hoverStyle = UILayout.Flat(new Color(0.8980393f, 0.95294124f, 1.0f));
        obj.AddThemeStyleboxOverride("hover", hoverStyle);
        obj.AddThemeStyleboxOverride("hover_pressed", hoverStyle);
        obj.AddThemeStyleboxOverride("pressed", UILayout.Flat(UILayout.PressedColor));
        obj.AddThemeStyleboxOverride("disabled", UILayout.Flat(new Color(UILayout.DisabledColor, 0.5f)));
        obj.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        parent.AddChild(obj);
        UpdateWidth();
        obj.SetDisableShortcuts(true); //the accelerators are only displayed, the shortcuts are handled by CityBuilderMenuBar
        dropdown = obj.GetPopup();
        UILayout.StylePopup(dropdown);
        dropdown.AddThemeColorOverride("font_accelerator_color", new Color(UILayout.TextColor, 0.55f));
        var divider = new StyleBoxLine { Color = new Color(UILayout.TextColor, 0.25f), Thickness = 1, GrowBegin = -6, GrowEnd = -6 };
        dropdown.AddThemeStyleboxOverride("separator", divider);
        SetEntries(options, actions);
        dropdown.IdPressed += OnItemPressed;
    }

    void UpdateWidth() {
        var font = obj.GetThemeFont("font");
        var fontSize = obj.GetThemeFontSize("font_size");
        var width = Mathf.Ceil(font.GetStringSize(obj.Text, HorizontalAlignment.Left, -1, fontSize).X + 20);
        if (width % 2 != 0) width -= 1;
        obj.CustomMinimumSize = new Vector2(width, MenuBar.Height);
    }

    public void SetInteractable(bool interactable) {
        obj.Disabled = !interactable;
    }

    public void SetEntryInteractable(int entry, bool interactable) {
        if (entry >= 0 && entry < dropdown.ItemCount) dropdown.SetItemDisabled(entry, !interactable);
    }

    //shows the entry with a check box (after SetEntries, which clears it)
    public void SetEntryChecked(int entry, bool isChecked) {
        if (entry < 0 || entry >= dropdown.ItemCount) return;
        dropdown.SetItemAsCheckable(entry, true);
        dropdown.SetItemChecked(entry, isChecked);
    }

    //called before the dropdown opens, to update the state of its entries
    public void SetOnOpen(System.Action action) {
        dropdown.AboutToPopup += action;
    }

    public void SetActive(bool active) {
        obj.Visible = active;
    }

    public void SetText(string newText) {
        obj.Text = newText;
    }

    public void RecalculateWidth(List<string> options) {
        //calculate the width of the widest text
        //to calculate the width for each one need to assign the text to the label first
        // PopupMenu sizes itself from the widest item label.
    }

    public void SetEntries(List<string> options, List<System.Action> actions) {
        this.actions = actions;
        dropdown.Clear();
        for (var i = 0; i < options.Count; i++) {
            //the item index matches the entry index also for dividers, so EnableEntry indices stay valid
            if (options[i] == MenuBar.Divider) {
                dropdown.AddSeparator("", i);
                continue;
            }
            SplitShortcut(options[i], out var label, out var accel);
            dropdown.AddItem(label, i, accel);
        }
    }

    //the shortcut info is aligned with tabs (e.g. "Road\t\t\t(Ctrl+R)"), which PopupMenu doesn't expand:
    //the shortcut is shown in the accelerator column instead
    static void SplitShortcut(string option, out string label, out Key accel) {
        label = option;
        accel = Key.None;
        var tab = option.IndexOf('\t');
        if (tab < 0) return;
        var shortcut = option.Substring(tab).Trim('\t', ' ');
        label = option.Substring(0, tab).TrimEnd();
        accel = OS.FindKeycodeFromString(shortcut.Trim('(', ')'));
        if (accel == Key.None) label += "    " + shortcut;
    }

    public float GetNextPosition() {
        return 0;
    }

    void OnItemPressed(long id) {
        if (id >= 0 && id < actions.Count) actions[(int)id]?.Invoke();
    }
}
