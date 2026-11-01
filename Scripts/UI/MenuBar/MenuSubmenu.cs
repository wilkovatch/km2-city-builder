using System.Collections.Generic;

//The content of a submenu of a menu bar dropdown: same shape as a top-level menu (parallel entries and actions lists,
//MenuBar.Divider for separators). It is declared at an index of the parent list, whose entry is the submenu label and
//whose action is null. The menus are rebuilt each time they open, so the check marks and disabled entries are data too
public class MenuSubmenu {
    public List<string> entries = new List<string>();
    public List<System.Action> actions = new List<System.Action>();
    public Dictionary<int, MenuSubmenu> submenus = new Dictionary<int, MenuSubmenu>();
    public HashSet<int> checks = new HashSet<int>(); //the entries shown with a check box, the ones in checked are ticked
    public HashSet<int> checkedEntries = new HashSet<int>();
    public HashSet<int> disabled = new HashSet<int>();

    public int Add(string entry, System.Action action) {
        entries.Add(entry);
        actions.Add(action);
        return entries.Count - 1;
    }

    public int AddCheck(string entry, System.Action action, bool isChecked) {
        var i = Add(entry, action);
        checks.Add(i);
        if (isChecked) checkedEntries.Add(i);
        return i;
    }

    //adds an entry that opens the given submenu
    public int AddSubmenu(string label, MenuSubmenu submenu) {
        var i = Add(label, null);
        submenus[i] = submenu;
        return i;
    }

    //the same actions wrapped by the given function (e.g. to record them as undo steps), also in the nested submenus
    public MenuSubmenu Wrapped(System.Func<string, System.Action, System.Action> wrap) {
        var res = new MenuSubmenu { entries = entries, checks = checks, checkedEntries = checkedEntries, disabled = disabled };
        for (int i = 0; i < actions.Count; i++) res.actions.Add(actions[i] == null ? null : wrap(entries[i], actions[i]));
        foreach (var pair in submenus) res.submenus[pair.Key] = pair.Value.Wrapped(wrap);
        return res;
    }
}
