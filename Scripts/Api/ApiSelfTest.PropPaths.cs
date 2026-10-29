using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Newtonsoft.Json.Linq;
using GO = GameEngine.GameObject;

namespace Api {
    public partial class ApiSelfTest {
        async Task StandalonePropRules() {
            object P(float x, float z) => new { position = new[] { x, 0, z } };
            var ribbons = new Dictionary<string, object> { ["main"] = new[] {
                new[] { new { start = P(-710, 700), end = P(-708, 700) }, new { start = P(-710, 710), end = P(-708, 710) } },
                new[] { new { start = P(-710, 720), end = P(-708, 720) }, new { start = P(-710, 730), end = P(-708, 730) } }
            } };
            var id = (await Call("propPaths.create", new { ribbons, name = "Standalone prop test", rule = new {
                main = new { type = "line", element = new { type = "line", mesh = "missing.obj", start = 2, elemDistance = 5, maxNumber = 10, xPos = 0.5 } }
            } })).Value<int>();
            PropPath Element() => History.UndoManager.GetAliveElement(id) as PropPath;
            var element = Element();
            Check("standalone prop paths have their own API kind", (await Call("elements.get", new { @ref = id }))["kind"].Value<string>() == "propPath");
            Check("generic prop expressions continue across disconnected ribbons", element.generator.props.Count == 4
                && element.generator.props.Select(p => GO.GetPosition(p.obj).Z).SequenceEqual(new[] { 702f, 707f, 722f, 727f }));
            await CheckUndo("moving a standalone prop rule", () => Call("propPaths.move", new { element = id, offset = new[] { 3, 0, 0 } }));
            await CheckUndo("inserting a paired prop section", () => Call("propPaths.insert", new { element = id, array = "ribbons.0.segments.0.sections", index = 1 }));
            await Call("propPaths.remove", new { element = id, array = "ribbons.0.segments.0.sections", index = 1 });
            element = Element();
            builder.SelectObject(element.generator.props[0].obj, true, null);
            await Frames(3);
            builder.lowLevelEditorPanel.Update();
            Check("selecting a generated prop opens its rule owner", builder.lowLevelEditorPanel.GetElement() == element);
            Check("standalone rule editing exposes a move handle", element.IsMoveHandleShown());
            Check("standalone ribbon points are editable", element.GetAllPoints().Any(p => GO.GetLayer(p.GetObject()) == 8));
            if (DisplayServer.GetName() != "headless") {
                var center = new Vector3(-709, 0, -715);
                (builder.GetViewport().GetCamera3D() as CameraController)?.SetView(center + new Vector3(20, 30, 25), center);
                await Frames(3);
                Screenshot("standalone_prop_rule.png");
            }
            //the ribbon buttons rebuild the editor: it stays open (on the closing of the panels the editing ends)
            var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            var addRibbon = ((List<EditorPanelPage>)typeof(EditorPanel).GetField("pages", flags).GetValue(builder.lowLevelEditorPanel))
                .SelectMany(p => (List<List<EditorPanelElement>>)typeof(EditorPanelPage).GetField("elements", flags).GetValue(p)).SelectMany(row => row)
                .OfType<EditorPanelElements.Button>().Select(b => typeof(EditorPanelElements.Button).GetField("button", flags).GetValue(b) as Godot.Button)
                .FirstOrDefault(b => b.Text == StringManager.Get("PROP_PATH_ADD_RIBBON"));
            var ribbonsBefore = element.RibbonNames.Count();
            addRibbon?.EmitSignal(BaseButton.SignalName.Pressed);
            await Frames(3);
            Check("adding a ribbon from the editor (" + ribbonsBefore + " -> " + Element().RibbonNames.Count() + ") rebuilds it still open",
                addRibbon != null && Element().RibbonNames.Count() == ribbonsBefore + 1 && builder.lowLevelEditorPanel.ActiveSelf()
                && builder.lowLevelEditorPanel.IsOpen() && builder.lowLevelEditorPanel.GetElement() == Element());
            builder.CloseAllPanels();
            await Frames(2);
            Check("closing the rebuilt editor ends the editing", !builder.lowLevelEditorPanel.IsOpen() && builder.lowLevelPlacer.onFrame == null);
            await Call("propPaths.delete", new { element = id });

            //Creation-mode switching must not reuse the ordinary low-level type dropdown.
            var method = typeof(CityBuilderMenuBar).GetMethod("CreatePropPath", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            History.UndoManager.Record("create prop rule menu", () => method.Invoke(builder, null));
            await Frames(2);
            Check("the Create prop rule command selects the generic built-in type", (builder.terrainClick.modifier as ElementPlacer.LowLevelPlacer)?.createType == PropPath.TypeId);
            builder.CloseAllPanels();
        }
    }
}
