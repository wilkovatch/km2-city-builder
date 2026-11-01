using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;

namespace Api {
    public partial class ApiSelfTest {
        //submenus of the menu bar: the mechanism, and the entries the core puts in them (createMenu and viewToggles "submenu")
        async Task Submenus() {
            var menuBar = CallBuilder<MenuBar>("GetMenuBar");
            var ran = new List<string>();
            var sub = new MenuSubmenu();
            sub.Add("Inner A", () => ran.Add("A"));
            sub.AddCheck("Inner B", () => ran.Add("B"), true);
            sub.disabled.Add(sub.Add("Inner C", () => ran.Add("C")));
            var nested = new MenuSubmenu();
            nested.Add("Deep", () => ran.Add("Deep"));
            sub.AddSubmenu("More", nested);
            var entries = new List<string> { "Top", "Group" };
            var actions = new List<System.Action> { () => ran.Add("Top"), null };
            var element = new MenuBarElement("Test", entries, actions, menuBar, 0, Colors.Black, new Dictionary<int, MenuSubmenu> { { 1, sub } });
            var popup = element.GetPopup();
            var button = popup.GetParent();
            try {
                var child = popup.GetItemSubmenuNode(1);
                Check("a menu entry opens a submenu", popup.GetItemSubmenuNode(0) == null && child != null && child.ItemCount == 4 && child.GetItemText(0) == "Inner A");
                Check("the entries of a submenu can be checkable and disabled", child.IsItemCheckable(1) && child.IsItemChecked(1) && child.IsItemDisabled(2) && !child.IsItemDisabled(0));
                child.EmitSignal(PopupMenu.SignalName.IdPressed, child.GetItemId(0));
                popup.EmitSignal(PopupMenu.SignalName.IdPressed, popup.GetItemId(0));
                Check("a submenu entry runs its own action and the parent entry its own (" + string.Join(",", ran) + ")", ran.SequenceEqual(new[] { "A", "Top" }));
                var deep = child.GetItemSubmenuNode(3);
                deep?.EmitSignal(PopupMenu.SignalName.IdPressed, deep.GetItemId(0));
                Check("submenus can be nested", deep != null && ran.Last() == "Deep");
                element.SetEntries(entries, actions, new Dictionary<int, MenuSubmenu> { { 1, sub } });
                Check("rebuilding a menu replaces its submenu popups", popup.GetChildren().OfType<PopupMenu>().Count() == 1);
                if (DisplayServer.GetName() != "headless") {
                    popup.Popup(new Rect2I(200, 60, 0, 0));
                    await Frames(3);
                    var open = popup.GetItemSubmenuNode(1);
                    open.Popup(new Rect2I(popup.Position.X + popup.Size.X, popup.Position.Y + 24, 0, 0));
                    await Frames(3);
                    Screenshot("submenu.png");
                    open.Hide();
                    popup.Hide();
                }
            } finally {
                button.GetParent().RemoveChild(button);
                button.QueueFree();
            }

            //the core: createMenu.submenu of a custom element type
            var types = CityElements.Types.Parsers.TypeParser.GetCustomElementTypes();
            const string submenuKey = "SETTINGS_MENU";
            if (types.TryGetValue("decal", out var decal)) {
                var createMenu = menuBar.FindChildren("*", nameof(MenuButton), true, false).OfType<MenuButton>()
                    .First(b => b.Text == StringManager.Get("CREATE")).GetPopup();
                var settings = decal.typeData.settings.createMenu;
                var decalLabel = settings.label != null ? StringManager.Get(settings.label) : Operations.CustomElements.GetTypeLabel("decal");
                try {
                    settings.submenu = submenuKey;
                    builder.ReloadCreateMenu();
                    var index = Enumerable.Range(0, createMenu.ItemCount).FirstOrDefault(i => createMenu.GetItemSubmenuNode(i) != null && createMenu.GetItemText(i) == StringManager.Get(submenuKey), -1);
                    var inner = index >= 0 ? createMenu.GetItemSubmenuNode(index) : null;
                    Check("a type with createMenu.submenu is listed in that Create submenu",
                        inner != null && inner.ItemCount == 1 && inner.GetItemText(0) == decalLabel && !Enumerable.Range(0, createMenu.ItemCount).Any(i => createMenu.GetItemText(i) == decalLabel));
                    inner?.EmitSignal(PopupMenu.SignalName.IdPressed, inner.GetItemId(0));
                    History.UndoManager.Record("place first spline point", () => builder.customElementPlacer.PlaceAt(new GameEngine.Raycast.RaycastHit { point = new Vector3(-700, 0, -700) }));
                    var created = builder.customElementPlacer.GetElement();
                    Check("the entry of a Create submenu starts the creation", created != null);
                    if (created != null) await Call("customElement.delete", new { element = History.ElementRegistry.GetId(created) });
                    builder.CloseAllPanels();
                } finally {
                    settings.submenu = null;
                    builder.ReloadCreateMenu();
                }
            }

            //the core: viewToggles submenu (with a toggle added in memory)
            var toggles = Rendering.ViewToggles.List();
            var toggle = new Rendering.ViewToggles.Toggle { name = "submenuTest", label = "SNAP_TO_GRID", submenu = submenuKey };
            toggles.Add(toggle);
            var viewMenu = menuBar.FindChildren("*", nameof(MenuButton), true, false).OfType<MenuButton>()
                .First(b => b.Text == StringManager.Get("VIEW")).GetPopup();
            var label = StringManager.Get(toggle.label).Split('	')[0].TrimEnd();
            try {
                viewMenu.EmitSignal(PopupMenu.SignalName.AboutToPopup);
                var index = Enumerable.Range(0, viewMenu.ItemCount).FirstOrDefault(i => viewMenu.GetItemSubmenuNode(i) != null && viewMenu.GetItemText(i) == StringManager.Get(submenuKey), -1);
                var inner = index >= 0 ? viewMenu.GetItemSubmenuNode(index) : null;
                Check("a view toggle with a submenu is a checked entry of that View submenu",
                    inner != null && inner.GetItemText(0) == label && inner.IsItemCheckable(0) && inner.IsItemChecked(0)
                    && !Enumerable.Range(0, viewMenu.ItemCount).Any(i => viewMenu.GetItemText(i) == label));
                inner?.EmitSignal(PopupMenu.SignalName.IdPressed, inner.GetItemId(0));
                await Frames(2);
                Check("the view toggle in a submenu switches it", !Rendering.ViewToggles.IsOn(toggle.name));
            } finally {
                Rendering.ViewToggles.Set(toggle.name, true);
                toggles.Remove(toggle);
                viewMenu.EmitSignal(PopupMenu.SignalName.AboutToPopup);
            }
        }
    }
}
