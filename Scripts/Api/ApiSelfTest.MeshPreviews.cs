using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Godot;

namespace Api {
    public partial class ApiSelfTest {
        const BindingFlags PreviewFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        static T PreviewField<T>(object owner, string field) => (T)owner.GetType().GetField(field, PreviewFlags).GetValue(owner);

        class PreviewListsPanel : EditorPanel {
            public ObjectState state = new();
            public CityElements.Types.Parameter[] parameters;
            public EditorPanelElements.MeshField plain, bound;
            public string plainValue;
            public int plainChanges;

            public PreviewListsPanel() => AddComplexElement(new IOList(this));

            public override void Initialize(Control canvas) {
                Initialize(canvas, 1);
                getObject = () => state;
                var page = GetPage(0);
                plain = page.AddMeshField(builder, "", "", plainValue, value => { plainValue = value; plainChanges++; }, false);
                page.IncreaseRow();
                bound = page.AddFieldMeshField(builder, "", "", () => state, "properties.mesh", null, true);
                page.IncreaseRow();
                float width = 0;
                foreach (var parameter in parameters ?? System.Array.Empty<CityElements.Types.Parameter>()) {
                    AddParameters(page, ref width, 1.5f, parameters, new CityElements.Types.TabElement { name = parameter.name, width = 1.5f }, () => state, false);
                }
            }

            public EditorPanelElements.ScrollList List(string name) => PreviewField<Dictionary<string, IOList.ListTuple>>(GetComplexElement<IOList>(), "listControls")[name].list;
        }

        static Dictionary<int, TextureRect> PreviewRows(EditorPanelElements.ScrollList list) =>
            PreviewField<Dictionary<int, TextureRect>>(PreviewField<ScrollListComponent>(list, "scrl"), "rowImages");

        static Rendering.MeshPreviewRenderer ListRenderer(EditorPanelElements.ScrollList list) =>
            PreviewField<ScrollListComponent>(list, "scrl").GetParent().GetChildren().OfType<Rendering.MeshPreviewRenderer>().Single();

        async Task MeshAssetLists(string[] assets) {
            CityElements.Types.Parameter Property(string name, string type) => new() { name = name, type = type };
            CityElements.Types.Parameter Array(string name, string label, params CityElements.Types.Parameter[] properties) => new() {
                name = name, type = "array", arrayProperties = new() { elementLabel = label, elementProperties = properties }
            };
            ObjectState Entry(string label, string mesh) {
                var entry = new ObjectState();
                entry.SetStr("name", label);
                entry.SetStr("asset", mesh);
                return entry;
            }
            var parameters = new[] {
                Array("named", "name", Property("name", "string"), Property("asset", "mesh")),
                Array("designated", "asset", Property("asset", "mesh"), Property("other", "mesh")),
                Array("instances", "name", Property("name", "string"), Property("model", "objectInstance")),
                new CityElements.Types.Parameter { name = "referenced", type = "array", arrayProperties = new() { customCityPropertiesType = "checkpoint" } },
                Array("ambiguous", "name", Property("name", "string"), Property("first", "mesh"), Property("second", "mesh")),
                Array("text", "name", Property("name", "string"))
            };
            var panel = new PreviewListsPanel { parameters = parameters, plainValue = assets[0] };
            var initialInstance = new ObjectState();
            initialInstance.SetStr("name", "Initial mesh");
            initialInstance.SetStr("model", assets[1]);
            var storedInstance = new ObjectState();
            storedInstance.SetStr("name", "Stored mesh");
            var model = new ObjectState();
            model.SetStr("meshPath", assets[2]);
            storedInstance.SetState("model", model);
            var referenced = new ObjectState();
            referenced.SetState("checkpoint", (ObjectState)model.Clone());
            panel.state.SetArray("named", new[] { Entry("Named mesh", assets[0]), Entry("Empty mesh", ""), Entry("Another mesh", assets[1]) });
            panel.state.SetArray("designated", new[] { Entry("", assets[0]) });
            panel.state.SetArray("instances", new[] { initialInstance, storedInstance });
            panel.state.SetArray("referenced", new[] { referenced });
            panel.state.SetArray("ambiguous", new[] { Entry("Record", "") });
            panel.state.SetArray("text", new[] { Entry("Text", "") });
            panel.Initialize(builder);
            panel.SetActive(true);
            try {
                Check("mesh arrays keep their display names separate from asset paths", PreviewField<List<string>>(panel.List("named"), "strings")
                    .SequenceEqual(new[] { "Named mesh", "Empty mesh", "Another mesh" })
                    && PreviewField<List<string>>(panel.List("named"), "images").SequenceEqual(new[] { assets[0], "", assets[1] }));
                Check("a mesh-valued label chooses the thumbnail in a multi-mesh record", PreviewField<List<string>>(panel.List("designated"), "images").SequenceEqual(new[] { assets[0] }));
                Check("object-instance arrays accept initial and stored mesh paths", PreviewField<List<string>>(panel.List("instances"), "images").SequenceEqual(new[] { assets[1], assets[2] }));
                Check("referenced custom city property types get mesh thumbnails", PreviewField<List<string>>(panel.List("referenced"), "images").SequenceEqual(new[] { assets[2] }));
                Check("ambiguous records and non-mesh lists remain text lists", !PreviewField<bool>(panel.List("ambiguous"), "withImages") && !PreviewField<bool>(panel.List("text"), "withImages"));
                await Frames(60);
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                using (var screen = GetViewport().GetTexture().GetImage()) screen.SavePng(Path.Combine(Path.GetDirectoryName(folder), "mesh_asset_lists.png"));
                var rows = PreviewRows(panel.List("named"));
                Check("mesh array rows render thumbnails while empty entries remain selectable", rows[0].Texture != null && rows[1].Texture == null && rows[2].Texture != null);
                panel.List("named").SetValue(1);
                Check("empty mesh entries can still be selected", PreviewField<int>(panel.List("named"), "selected") == 1);
                Operations.States.ShiftArrayElement(panel.state, parameters[0], 0, true);
                Operations.States.CloneArrayElement(panel.state, parameters[0], 2);
                panel.RefreshValues();
                await Frames(3);
                Check("reordered and cloned rows keep their corresponding mesh paths", PreviewField<List<string>>(panel.List("named"), "images").SequenceEqual(new[] { "", assets[0], assets[1], assets[1] }));
                Operations.States.DeleteArrayElement(panel.state, parameters[0], 0);
                panel.RefreshValues();
                var renderer = ListRenderer(panel.List("named"));
                renderer.ClearCache();
                var pending = renderer.GetThumbnail(PathHelper.ResolveAssetPath(assets[0]));
                await Frames(1);
                panel.Hide(true);
                await Frames(2);
                Check("hidden mesh lists cancel pending rendering and release their viewports", pending.IsCompleted && renderer.GetChildCount() == 0);
                panel.Hide(false);
                await Frames(24);
                Check("mesh array thumbnails return after the panel reopens", PreviewRows(panel.List("named"))[0].Texture != null);
            } finally { panel.Destroy(); }
            await Frames(2);
        }

        async Task MeshPreselection(string[] assets) {
            var picker = PreviewField<EditorPanels.MeshListEditorPanel>(builder, "meshListEditorPanel");
            void Invoke(string method, params object[] args) => picker.GetType().GetMethod(method, PreviewFlags).Invoke(picker, args);
            picker.ResetEntries(); //include the synthetic cache assets, so the selected row really is beyond the first viewport
            var fields = new PreviewListsPanel { plainValue = assets[2] };
            fields.state.SetStr("mesh", assets[0]);
            fields.Initialize(builder);
            fields.SetActive(true);
            var placer = builder.meshPlacer;
            var oldMesh = placer.meshPath;
            var oldEnabled = placer.placeEnabled;
            var oldModifier = builder.terrainClick.modifier;
            try {
                await Frames(16);
                var inlineImage = PreviewField<TextureRect>(fields.plain, "image");
                var boundImage = PreviewField<TextureRect>(fields.bound, "image");
                Check("single mesh fields show static thumbnails for default and bound values", inlineImage.Texture != null && boundImage.Texture != null && !inlineImage.FlipV);
                fields.plain.SetValue(assets[0]);
                fields.plain.SetValue(assets[2]);
                fields.plain.SetValue(assets[1]);
                await Frames(12);
                var fieldRenderer = PreviewField<Rendering.MeshPreviewRenderer>(fields.plain, "renderer");
                var currentThumbnail = fieldRenderer.GetThumbnail(PathHelper.ResolveAssetPath(assets[1]));
                Check("rapid mesh field updates discard stale thumbnail results", currentThumbnail.IsCompletedSuccessfully && currentThumbnail.Result != null && inlineImage.Texture == currentThumbnail.Result);
                fields.state.SetStr("mesh", "");
                fields.RefreshValues();
                Check("clearing a single mesh field clears its thumbnail", boundImage.Texture == null);
                fields.state.SetStr("mesh", assets[0]);
                fields.RefreshValues();
                fields.Hide(true);
                await Frames(2);
                Check("hidden single mesh fields cancel their rendering", fieldRenderer.GetChildCount() == 0);
                fields.Hide(false);
                await Frames(12);
                Check("single mesh field thumbnails return when the panel reopens", inlineImage.Texture != null && boundImage.Texture != null);
                fields.plain.SetValue(assets[0]);
                PreviewField<Godot.Button>(fields.plain, "button").EmitSignal(Godot.Button.SignalName.Pressed);
                await Frames(12);
                var list = PreviewField<EditorPanelElements.ScrollList>(picker, "lst");
                var preview = PreviewField<EditorPanelElements.MeshPreview>(picker, "preview");
                var scroll = PreviewField<ScrollContainer>(list, "scroll");
                var selected = PreviewField<int>(list, "selected");
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                using (var screen = GetViewport().GetTexture().GetImage()) screen.SavePng(Path.Combine(Path.GetDirectoryName(folder), "mesh_preselected.png"));
                Check("ordinary mesh fields preselect their latest displayed value", PreviewField<string>(picker, "curVal") == assets[0] && preview.renderer.HasPreview);
                Check("preselection reveals and highlights a row beyond the initial viewport", selected >= 17 && scroll.ScrollVertical > 0
                    && selected * 25 >= scroll.ScrollVertical && (selected + 1) * 25 <= scroll.ScrollVertical + scroll.Size.Y);
                Check("opening the mesh selector leaves placement state unchanged", placer.meshPath == oldMesh && placer.placeEnabled == oldEnabled && builder.terrainClick.modifier == oldModifier);
                Invoke("Cancel");
                Check("cancelling preselection does not invoke the field setter", fields.plainChanges == 0 && fields.plainValue == assets[2] && fields.ActiveSelf());

                fields.state.SetStr("mesh", assets[2]);
                fields.RefreshValues();
                PreviewField<Godot.Button>(fields.bound, "button").EmitSignal(Godot.Button.SignalName.Pressed);
                await Frames(3);
                Check("bound mesh fields read the current state when opening", PreviewField<string>(picker, "curVal") == assets[2] && preview.renderer.HasPreview);
                Invoke("SelectAction");
                Check("confirming the current bound mesh preserves its value", fields.state.Str("mesh") == assets[2]);
                fields.SetActive(false);

                var opener = new Control();
                builder.AddChild(opener);
                string returned = null;
                var variant = assets[0].ToUpperInvariant().Replace('/', '\\');
                builder.OpenMeshSelector(null, opener, value => returned = value, null, true, variant);
                await Frames(3);
                Check("preselection matches case and separator variants without rewriting the current value", PreviewField<int>(list, "selected") >= 0
                    && PreviewField<string>(picker, "curVal") == variant && preview.renderer.HasPreview);
                Invoke("SelectAction");
                Check("confirming an unchanged variant returns the original stored path", returned == variant);
                builder.OpenMeshSelector(null, opener, value => returned = value, null, true, PathHelper.ResolveAssetPath(assets[1]));
                await Frames(3);
                Check("an absolute current mesh matches its relative picker row", PreviewField<int>(list, "selected") >= 0 && preview.renderer.HasPreview);
                Invoke("Cancel");
                var missing = "geometry/__missing_current_mesh.obj";
                builder.OpenMeshSelector(null, opener, value => returned = value, null, true, missing);
                await Frames(3);
                Check("a missing current mesh keeps its path and shows unavailable without selecting a substitute", PreviewField<string>(picker, "curVal") == missing
                    && PreviewField<int>(list, "selected") == -1 && !PreviewField<EditorPanelElements.Button>(picker, "selectBtn").GetInteractable()
                    && !preview.renderer.HasPreview && preview.renderer.display.GetChildren().OfType<Label>().Any(l => l.Visible));
                Invoke("Cancel");
                builder.OpenMeshSelector(null, opener, value => returned = value, null, true);
                Check("adding a mesh starts without a previous selection", PreviewField<int>(list, "selected") == -1 && PreviewField<string>(picker, "curVal") == "");
                Invoke("Cancel");
                opener.QueueFree();
                builder.CreateMeshInstance("instMesh", null);
                Check("new mesh placement starts without a previous selection", PreviewField<int>(list, "selected") == -1 && PreviewField<string>(picker, "curVal") == "");
                Invoke("Cancel");
            } finally { fields.Destroy(); }

            foreach (var single in new[] { false, true }) {
                var state = new ObjectState();
                state.SetStr("type", single ? "line" : "customProp");
                state.SetStr("mesh", assets[2]);
                state.SetArray("meshes", new[] { assets[0], "", assets[1] });
                var owner = new ObjectState();
                Operations.Props.GetPropRule(owner, true).SetState("main", state);
                var prop = new EditorPanels.Props.PropsEditorPanel();
                prop.Initialize(builder);
                prop.SetContainer(owner, "main");
                prop.SetActive(true);
                try {
                    var list = PreviewField<EditorPanelElements.ScrollList>(picker, "lst");
                    var preview = PreviewField<EditorPanelElements.MeshPreview>(picker, "preview");
                    System.Collections.IDictionary Controls() {
                        var complex = (System.Collections.IDictionary)typeof(EditorPanel).GetField("complexElements", PreviewFlags).GetValue(prop);
                        var io = complex.Values.Cast<object>().Single(c => c.GetType().Name == "IOList");
                        return (System.Collections.IDictionary)io.GetType().GetField("listControls", PreviewFlags).GetValue(io);
                    }
                    if (!single) {
                        var meshes = PreviewField<EditorPanelElements.ScrollList>(Controls()["meshes"], "list");
                        await Frames(32);
                        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                        using (var screen = GetViewport().GetTexture().GetImage()) screen.SavePng(Path.Combine(Path.GetDirectoryName(folder), "mesh_prop_list.png"));
                        Check("prop mesh lists show thumbnails and preserve empty entries", PreviewRows(meshes)[0].Texture != null
                            && PreviewRows(meshes)[1].Texture == null && PreviewRows(meshes)[2].Texture != null);
                        ((System.Action<string, int>)Controls()["meshes"].GetType().GetField("selectAction").GetValue(Controls()["meshes"])).Invoke("meshes", 2);
                    } else {
                        var pages = (List<EditorPanelPage>)typeof(EditorPanel).GetField("pages", PreviewFlags).GetValue(prop);
                        var field = PreviewField<Dictionary<EditorPanelElement, FieldStatus>>(pages[0], "fieldElements").First(x => x.Key is EditorPanelElements.MeshField).Key;
                        builder.OpenMeshSelector(prop, null, x => { }, null, true, ((EditorPanelElements.MeshField)field).GetValue());
                    }
                    await Frames(3);
                    Check((single ? "single" : "indexed") + " prop Change preselects its existing mesh", PreviewField<string>(picker, "curVal") == (single ? assets[2] : assets[1])
                        && PreviewField<int>(list, "selected") >= 0 && preview.renderer.HasPreview);
                    Invoke("Cancel");
                    if (!single) {
                        ((System.Action<string>)Controls()["meshes"].GetType().GetField("addAction").GetValue(Controls()["meshes"])).Invoke("meshes");
                        Check("prop Add opens without inheriting Change's selection", PreviewField<int>(list, "selected") == -1);
                        Invoke("Cancel");
                    }
                    Check("cancelling prop mesh changes preserves the state", prop.GetContainer().Str("mesh") == assets[2]
                        && prop.GetContainer().Array<string>("meshes").SequenceEqual(new[] { assets[0], "", assets[1] }));
                } finally { prop.Destroy(); }
            }
            await Frames(2);
        }

        async Task CityPropertyMeshFields() {
            (int page, EditorPanelElements.MeshField field) FindField(EditorPanel panel, string name) {
                var pages = (List<EditorPanelPage>)typeof(EditorPanel).GetField("pages", PreviewFlags).GetValue(panel);
                for (int i = 0; i < pages.Count; i++) {
                    var fields = PreviewField<Dictionary<EditorPanelElement, FieldStatus>>(pages[i], "fieldElements");
                    foreach (var pair in fields) {
                        if (pair.Key is EditorPanelElements.MeshField field && pair.Value.fieldName.EndsWith("." + name)) return (i, field);
                    }
                }
                return (-1, null);
            }
            var properties = PreviewField<EditorPanels.CityPropertiesEditorPanel>(builder, "cityPropertiesEditorPanel");
            builder.SelectObject(M.GetDummy<CityProperties>(), true, null);
            var light = FindField(properties, "trafficLight1");
            Check("city properties exposes the traffic-light mesh field", light.field != null);
            if (light.field == null) return;
            properties.ShowPage(light.page);
            await Frames(24);
            Check("city traffic-light fields have inline mesh thumbnails", PreviewField<TextureRect>(light.field, "image").Texture != null);
            var pages = (List<EditorPanelPage>)typeof(EditorPanel).GetField("pages", PreviewFlags).GetValue(properties);
            var scroll = PreviewField<ScrollContainer>(pages[light.page], "scroll");
            scroll.ScrollVertical = (int)scroll.GetVScrollBar().MaxValue;
            await Frames(2);
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            using (var screen = GetViewport().GetTexture().GetImage()) screen.SavePng(Path.Combine(Path.GetDirectoryName(folder), "mesh_traffic_lights.png"));
            var details = properties.OpenArrayElement("trafficCars", 0);
            Check("city traffic-car details open", details != null);
            if (details != null) {
                var car = FindField(details, "carModel");
                await Frames(24);
                Check("city traffic-car detail fields have inline mesh thumbnails", car.field != null && PreviewField<TextureRect>(car.field, "image").Texture != null);
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                using (var screen = GetViewport().GetTexture().GetImage()) screen.SavePng(Path.Combine(Path.GetDirectoryName(folder), "mesh_traffic_car.png"));
                details.SetActive(false);
            }
            properties.Terminate();
            await Frames(2);
        }

        void PreviewGeometry() {
            var root = new LODGroup { Position = new Vector3(30, -4, 9), Scale = new Vector3(-2, 3, 1) };
            var nested = new Node3D { Position = new Vector3(7, 6, -2), Rotation = new Vector3(0.3f, 0.4f, 0) };
            root.AddChild(nested);
            using var textureImage = Godot.Image.CreateEmpty(2, 2, false, Godot.Image.Format.Rgba8);
            textureImage.Fill(new Color(1, 0.2f, 0.1f, 0.5f));
            var material = new StandardMaterial3D { AlbedoTexture = ImageTexture.CreateFromImage(textureImage), Transparency = BaseMaterial3D.TransparencyEnum.Alpha };
            var high = new MeshInstance3D { Mesh = new BoxMesh(), Position = new Vector3(2, 3, 4), MaterialOverride = material };
            high.SetSurfaceOverrideMaterial(0, material);
            nested.AddChild(high);
            var low = new MeshInstance3D { Mesh = new BoxMesh(), Position = new Vector3(1000, 0, 0) };
            root.AddChild(low);
            root.SetLODs(new[] { 0.5f, 0.01f }, new[] { new List<GeometryInstance3D> { high }, new List<GeometryInstance3D> { low } });
            high.Visible = false;
            low.Visible = true;
            Node3D copy = null;
            try {
                copy = GameEngine.Mesh.CreatePreviewGeometry(root);
                var meshes = copy.GetChildren().OfType<MeshInstance3D>().ToArray();
                Check("preview takes highest-detail geometry even when the editor LOD hides it", meshes.Length == 1 && meshes[0].Mesh == high.Mesh);
                Check("preview preserves nested transforms, reflections and nonuniform scale", meshes.Length == 1
                    && meshes[0].Transform.IsEqualApprox(root.Transform * nested.Transform * high.Transform));
                Check("preview preserves textures, transparency and material overrides", meshes.Length == 1
                    && meshes[0].MaterialOverride == material && meshes[0].GetSurfaceOverrideMaterial(0) == material);
                Check("preview has no physics or LOD nodes and does not change the source", copy.GetChildren().All(n => n is MeshInstance3D)
                    && !high.Visible && low.Visible && root.UpdaterIndex == -1);
            } finally {
                copy?.Free();
                root.Free();
            }
        }

        async Task PreviewRowReassignment() {
            var owner = new Control { Size = new Vector2(250, 400) };
            builder.AddChild(owner);
            var pending = new TaskCompletionSource<Texture2D>();
            using var image = Godot.Image.CreateEmpty(2, 2, false, Godot.Image.Format.Rgba8);
            image.Fill(Colors.Red);
            var oldTexture = ImageTexture.CreateFromImage(image);
            image.Fill(Colors.Blue);
            var newTexture = ImageTexture.CreateFromImage(image);
            var list = new EditorPanelElements.ScrollList("", new List<string>(), new List<string>(), null, owner, Vector2.Zero);
            var driver = PreviewField<ScrollListComponent>(list, "scrl");
            var rows = PreviewField<Dictionary<int, TextureRect>>(driver, "rowImages");
            Check("texture list thumbnails retain their original vertical orientation", rows.Values.All(r => r.FlipV));
            list.SetThumbnailProvider(p => p == "old" ? pending.Task : Task.FromResult<Texture2D>(newTexture));
            list.SetItems(new List<string> { "old" }, new List<string> { "old" });
            await Frames(2);
            list.SetItems(new List<string>(), new List<string>());
            pending.SetResult(oldTexture);
            await Frames(2);
            Check("a thumbnail finishing after its row disappears is discarded", rows.Values.All(r => r.Texture == null));
            list.SetItems(new List<string> { "new" }, new List<string> { "new" });
            await Frames(2);
            Check("reassigned rows show the new thumbnail upright", rows[0].Texture == newTexture && !rows[0].FlipV);
            list.SetThumbnailProvider(p => Task.FromResult<Texture2D>(int.Parse(p) % 2 == 0 ? oldTexture : newTexture));
            var items = Enumerable.Range(0, 60).Select(i => i.ToString()).ToList();
            list.SetItems(items, items);
            var scroll = PreviewField<ScrollContainer>(list, "scroll");
            await Frames(2);
            scroll.ScrollVertical = 30 * 25;
            list.UpdateRows();
            await Frames(24);
            var values = PreviewField<Dictionary<int, int>>(list, "curRowsValues");
            Check("scrolling reuses rows without stale thumbnails", values.Values.Any(i => i >= 30)
                && values.All(p => p.Value < 0 || rows[p.Key].Texture == (p.Value % 2 == 0 ? oldTexture : newTexture)));
            owner.QueueFree();
            await Frames(1);
        }

        async Task MeshPreviews() {
            PreviewGeometry();
            if (!Rendering.CityRenderer.Available) {
                Log("     mesh preview rendering needs a window");
                return;
            }
            await PreviewRowReassignment();
            var directory = Path.Combine(folder, "geometry");
            Directory.CreateDirectory(directory);
            var fixturePaths = new List<string>();
            var sizes = new[] { new Vector3(7, 3, 2), new Vector3(0.1f, 30, 0.2f), new Vector3(40, 0.01f, 20) };
            foreach (var (size, index) in sizes.Select((s, i) => (s, i))) {
                var asset = Path.Combine(directory, "__mesh_preview_" + index + ".obj");
                var offset = new Vector3(40, -8, 90);
                var vertices = Enumerable.Range(0, 8).Select(i => offset + new Vector3((i & 1) == 0 ? 0 : size.X,
                    (i & 2) == 0 ? 0 : size.Y, (i & 4) == 0 ? 0 : size.Z)).ToArray();
                SelectionObj(asset, vertices, new[] { new[] { 0, 2, 3, 0, 3, 1, 4, 5, 7, 4, 7, 6, 0, 1, 5, 0, 5, 4, 2, 6, 7, 2, 7, 3, 0, 4, 6, 0, 6, 2, 1, 3, 7, 1, 7, 5 } });
                fixturePaths.Add(asset);
            }
            var empty = Path.Combine(directory, "__mesh_preview_empty.obj");
            File.WriteAllText(empty, "# empty preview fixture\n");
            var malformed = Path.Combine(directory, "__mesh_preview_malformed.obj");
            File.WriteAllText(malformed, "v not-a-number 0 0\nf 1 2 3\n");
            var before = await Snapshot();
            var history = (await Call("history.state")).DeepClone();
            var poolCount = MeshManager.GetInstance().GetAllMeshes().Length;
            var editorCamera = GetViewport().GetCamera3D();
            var editorTransform = editorCamera.GlobalTransform;
            var cityLod = new LODGroup { Position = editorCamera.GlobalPosition + Vector3.Right * 1000 };
            var cityHigh = new MeshInstance3D { Mesh = new BoxMesh() };
            var cityLow = new MeshInstance3D { Mesh = new BoxMesh() };
            cityLod.AddChild(cityHigh);
            cityLod.AddChild(cityLow);
            cityLod.SetLODs(new[] { 0.9f, 0.000001f }, new[] { new List<GeometryInstance3D> { cityHigh }, new List<GeometryInstance3D> { cityLow } });
            M.AddChild(cityLod);
            await Frames(2);
            Check("city LOD fixture follows the editor camera", !cityHigh.Visible && cityLow.Visible);
            var picker = PreviewField<EditorPanels.MeshListEditorPanel>(builder, "meshListEditorPanel");
            void Invoke(string method, params object[] args) => picker.GetType().GetMethod(method, PreviewFlags).Invoke(picker, args);
            builder.CreateMeshInstance("instMesh", null);
            await Frames(1);
            var stockPaths = PreviewField<List<string>>(picker, "absolutePaths").Where(p => !p.Contains("__mesh_preview_") && File.Exists(p))
                .OrderBy(p => p, System.StringComparer.OrdinalIgnoreCase).ToArray();
            Invoke("Search", "__mesh_preview_[0-2]\\.obj$");
            var preview = PreviewField<EditorPanelElements.MeshPreview>(picker, "preview");
            var renderer = preview.renderer;
            var list = PreviewField<EditorPanelElements.ScrollList>(picker, "lst");
            for (int i = 0; i < 3; i++) {
                list.SetValue(i);
                await Frames(4);
                Check("mesh picker shows generated fixture " + i, renderer.HasPreview);
                if (!renderer.HasPreview) continue;
                var viewport = PreviewField<SubViewport>(renderer, "selectedViewport");
                var camera = viewport.GetCamera3D();
                var pivot = PreviewField<Node3D>(renderer, "pivot");
                var fits = true;
                for (int turn = 0; turn < 12; turn++) {
                    renderer._Process(10.0 / 12);
                    foreach (MeshInstance3D mesh in pivot.GetChild(0).GetChildren()) {
                        var box = mesh.GetAabb();
                        for (int corner = 0; corner < 8; corner++) {
                            var point = box.Position + new Vector3((corner & 1) == 0 ? 0 : box.Size.X,
                                (corner & 2) == 0 ? 0 : box.Size.Y, (corner & 4) == 0 ? 0 : box.Size.Z);
                            var pixel = camera.UnprojectPosition(mesh.GlobalTransform * point);
                            fits &= pixel.IsFinite() && pixel.X > 0 && pixel.Y > 0 && pixel.X < viewport.Size.X && pixel.Y < viewport.Size.Y;
                        }
                    }
                }
                Check("complete mesh fits throughout a full turn, fixture " + i, fits);
                var angle = renderer.Angle;
                await Frames(3);
                Check("mesh preview rotates automatically, fixture " + i, !Mathf.IsEqualApprox(angle, renderer.Angle));
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                using var pixels = viewport.GetTexture().GetImage();
                var data = pixels.GetData();
                Check("mesh preview draws visible pixels, fixture " + i, Enumerable.Range(0, data.Length / 4).Count(p => data[p * 4 + 3] > 10) > 10);
                pixels.SavePng(Path.Combine(Path.GetDirectoryName(folder), "mesh_preview_fixture_" + i + ".png"));
            }
            list.SetValue(0);
            list.ManualScroll(true);
            await Frames(3);
            Check("keyboard navigation updates the mesh preview", renderer.HasPreview && PreviewField<string>(picker, "curVal").EndsWith("1.obj"));
            Invoke("Search", "__mesh_preview_0\\.obj$");
            Check("filtering clears the selected preview", !renderer.HasPreview && PreviewField<string>(picker, "curVal") == "");
            list.SetValue(0);
            await Frames(3);
            Invoke("RefreshList");
            Check("rescan clears the preview and thumbnail cache", !renderer.HasPreview && renderer.CachedThumbnails == 0);
            Invoke("Search", "__mesh_preview_[0-2]\\.obj$");
            list.SetValue(2);
            list.SetValue(1);
            list.SetValue(0);
            await Frames(3);
            Check("rapid selection ends on the latest mesh", renderer.HasPreview && PreviewField<string>(picker, "curVal").EndsWith("0.obj"));
            var fixtureThumbnail = renderer.GetThumbnail(fixturePaths[0]);
            for (int i = 0; i < 120 && !fixtureThumbnail.IsCompleted; i++) await Frames(1);
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            using (var screen = GetViewport().GetTexture().GetImage()) screen.SavePng(Path.Combine(Path.GetDirectoryName(folder), "mesh_preview_picker.png"));
            foreach (var path in new[] { Path.Combine(directory, "missing.obj"), empty, malformed }) {
                preview.SetValue(path);
                await Frames(3);
                Check("failed or empty import has no preview: " + Path.GetFileName(path), !renderer.HasPreview);
            }
            var random = new System.Random(7351);
            var samples = stockPaths.OrderBy(_ => random.Next()).Take(10).ToArray();
            using var sheet = Godot.Image.CreateEmpty(5 * 128, 2 * 128, false, Godot.Image.Format.Rgba8);
            sheet.Fill(new Color(0.7f, 0.7f, 0.7f));
            for (int i = 0; i < samples.Length; i++) {
                preview.SetValue(samples[i]);
                await Frames(3);
                if (!renderer.HasPreview) {
                    var imported = MeshImporter.LoadMesh(samples[i], null, true, false);
                    try {
                        //Some packages contain only special pieces that the core assigns to no LOD.
                        var hasDetail = imported != null && imported.FindChildren("*", nameof(MeshInstance3D), true, false)
                            .OfType<MeshInstance3D>().Any(m => m.Mesh != null && LODGroup.IsHighestDetailRenderer(m));
                        Check("unsupported mesh has the unavailable placeholder: " + Path.GetFileName(samples[i]), !hasDetail
                            && renderer.display.GetChildren().OfType<Label>().Any(l => l.Visible));
                    } finally { imported?.Free(); }
                    continue;
                }
                var thumbnailTask = renderer.GetThumbnail(samples[i]);
                for (int frame = 0; frame < 120 && !thumbnailTask.IsCompleted; frame++) await Frames(1);
                Check("stock mesh preview and thumbnail " + Path.GetFileName(samples[i]), thumbnailTask.IsCompletedSuccessfully && thumbnailTask.Result != null);
                if (!thumbnailTask.IsCompletedSuccessfully || thumbnailTask.Result == null) continue;
                using var pixels = thumbnailTask.Result.GetImage();
                pixels.Resize(128, 128);
                sheet.BlendRect(pixels, new Rect2I(0, 0, 128, 128), new Vector2I(i % 5 * 128, i / 5 * 128));
            }
            Log("     sampled " + samples.Length + " stock meshes with seed 7351");
            sheet.SavePng(Path.Combine(Path.GetDirectoryName(folder), "mesh_preview_samples.png"));
            Check("browsing does not populate the placed-mesh cache", MeshManager.GetInstance().GetAllMeshes().Length == poolCount);
            Check("previewing leaves the editor camera and city LODs unchanged", editorCamera.GlobalTransform.IsEqualApprox(editorTransform)
                && !cityHigh.Visible && cityLow.Visible);
            cityLod.QueueFree();
            var thumbnail = renderer.GetThumbnail(fixturePaths[0]);
            for (int i = 0; i < 120 && !thumbnail.IsCompleted; i++) await Frames(1);
            Check("static thumbnails render and stay within the cache limit", thumbnail.IsCompletedSuccessfully && thumbnail.Result != null && renderer.CachedThumbnails <= 128);
            for (int i = 0; i < 129; i++) {
                var path = Path.Combine(directory, "__mesh_cache_" + i + ".obj");
                File.Copy(fixturePaths[0], path);
                var task = renderer.GetThumbnail(path);
                for (int frame = 0; frame < 120 && !task.IsCompleted; frame++) await Frames(1);
                if (!task.IsCompletedSuccessfully || task.Result == null) {
                    Check("synthetic thumbnail generation completes", false);
                    break;
                }
            }
            Check("thumbnail cache evicts older entries at 128", renderer.CachedThumbnails == 128 && !renderer.GetThumbnail(fixturePaths[0]).IsCompleted);
            Invoke("Cancel");
            await Frames(2);
            Check("closing the picker releases its viewports", !renderer.HasPreview && renderer.GetChildCount() == 0);
            var opener = new Control();
            builder.AddChild(opener);
            string picked = null;
            bool after = false;
            picker.disablePlacer = true;
            picker.Open(opener, value => picked = value, () => after = true);
            Invoke("Search", "__mesh_preview_0\\.obj$");
            list.SetValue(0);
            await Frames(3);
            Check("field selection reopens a working preview", renderer.HasPreview && !opener.Visible);
            Invoke("SelectAction");
            Check("field selection returns its value and restores the opener", picked != null && picked.EndsWith("0.obj") && after && opener.Visible && !renderer.HasPreview);
            opener.QueueFree();
            builder.CreateMeshInstance("instMesh", null);
            Invoke("Search", "__mesh_preview_0\\.obj$");
            list.SetValue(0);
            await Frames(3);
            Invoke("SelectAction");
            await Frames(2);
            Check("placement opens its options and stops the hidden preview", picker.propEditor.ActiveSelf() && !renderer.HasPreview && renderer.GetChildCount() == 0);
            picker.propEditor.Terminate();
            var assetNames = fixturePaths.Select(p => "geometry/" + Path.GetFileName(p)).ToArray();
            await MeshAssetLists(assetNames);
            await MeshPreselection(assetNames);
            await CityPropertyMeshFields();
            var afterCity = await Snapshot();
            Check("previewing and picking do not change city state or history", SameToken(before, afterCity, "", new List<string>())
                && SameToken(history, await Call("history.state"), "", new List<string>()));
        }
    }
}
