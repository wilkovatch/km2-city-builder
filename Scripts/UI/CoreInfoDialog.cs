using Godot;
using System.Collections.Generic;
using SM = StringManager;

//Information about an installed core: folder, version, repository and the changelog of its releases (scrollable).
public static class CoreInfoDialog {
    public static void Show(Control parent, CoreManager.CoreInfo core, List<CoreManager.ChangelogEntry> changelog) {
        if (parent.GetNodeOrNull<AcceptDialog>("CoreInfo") != null) return;
        var dialog = new AcceptDialog {
            Name = "CoreInfo", Title = core.name,
            Exclusive = true, Transient = true, Unresizable = false,
            MinSize = new Vector2I(420, 300), OkButtonText = SM.Get("CLOSE")
        };
        dialog.AddThemeStyleboxOverride("panel", UILayout.Box(new Color(0.9f, 0.9f, 0.9f), 12, 12, 12, 12));
        dialog.VisibilityChanged += () => { if (!dialog.Visible) dialog.QueueFree(); };
        var content = new VBoxContainer();
        content.AddThemeConstantOverride("separation", 8);
        dialog.AddChild(content);
        dialog.GetOkButton().Hide();

        //everything but the button scrolls: wrapped labels outside a scroll container would make the dialog taller than its size
        var scroll = new ScrollContainer {
            Name = "Changelog", HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill
        };
        scroll.AddThemeStyleboxOverride("panel", UILayout.Box(new Color(0.96f, 0.96f, 0.96f)));
        content.AddChild(scroll);
        var margin = new MarginContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        foreach (var side in new[] { "left", "right", "top", "bottom" }) margin.AddThemeConstantOverride("margin_" + side, 10);
        scroll.AddChild(margin);
        var rows = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        rows.AddThemeConstantOverride("separation", 6);
        margin.AddChild(rows);
        Text(rows, SM.Get("PREF_CORES_INFO_FOLDER") + core.folder);
        Text(rows, SM.Get("PREF_CORES_INFO_VERSION") + core.version);
        if (core.repo != null) Text(rows, SM.Get("PREF_CORES_INFO_REPO") + core.repo);
        rows.AddChild(new HSeparator());
        Heading(rows, SM.Get("PREF_CORES_INFO_CHANGELOG"));
        if (changelog.Count == 0) Text(rows, SM.Get("PREF_CORES_INFO_NO_CHANGELOG"));
        foreach (var entry in changelog) {
            var title = entry.tag;
            if (entry.installed) title += " " + SM.Get("PREF_CORES_INFO_INSTALLED");
            else if (entry.newer) title += " " + SM.Get("PREF_CORES_INFO_AVAILABLE");
            Heading(rows, title);
            if (!string.IsNullOrWhiteSpace(entry.changes)) Text(rows, entry.changes);
        }

        var close = new Button {
            Name = "CloseCoreInfo", Text = SM.Get("CLOSE"),
            CustomMinimumSize = new Vector2(160, 30), SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter
        };
        UILayout.StyleButton(close);
        close.Pressed += () => dialog.Hide();
        content.AddChild(close);

        parent.AddChild(dialog);
        var available = (Vector2I)parent.GetViewportRect().Size - new Vector2I(40, 60);
        dialog.PopupCentered(new Vector2I(Mathf.Min(640, available.X), Mathf.Min(520, available.Y)));
        UIDetection.NotifyPopupOpen();
    }

    static void Text(VBoxContainer rows, string text) {
        var label = UILayout.MakeLabel(text, HorizontalAlignment.Left);
        label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        label.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        rows.AddChild(label);
    }

    static void Heading(VBoxContainer rows, string text) {
        var label = UILayout.MakeLabel(text, HorizontalAlignment.Left);
        label.AddThemeFontOverride("font", UILayout.BoldFont());
        label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        label.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        rows.AddChild(label);
    }
}
