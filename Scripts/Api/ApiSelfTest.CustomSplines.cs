using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using GO = GameEngine.GameObject;
using TP = CityElements.Types.Parsers.TypeParser;

namespace Api {
    public partial class ApiSelfTest {
        async Task TrainSplines() {
            Log("train splines");
            var settings = TP.GetCustomElementTypes()["train"].typeData.settings.spline;
            Check("train curve is restricted to trains", settings.curveTypes.SequenceEqual(new[] { 3 }) &&
                !SplineCurves.Allowed(TP.GetRoadTypes()["psdl"].typeData.settings).Contains(3) &&
                !TP.GetCustomElementTypes()["decal"].typeData.settings.spline.curveTypes.Contains(3));
            var controls = new List<Vector3> { new(0,0,0), new(10,5,0), new(10,20,10), new(25,0,15), new(30,-5,25) };
            var state = new ObjectState(); state.SetInt("curveType",3);
            var curve = SplineCurves.Create(settings,state,controls);
            Check("MM2 interior reference: cubic X/Z and linear Y", curve.Position(.375f).DistanceTo(new Vector3(9.6875f,12.5f,4.6875f)) < .0001f);
            Check("MM2 endpoint reference uses wrapped neighbors", curve.Position(.125f).DistanceTo(new Vector3(3.125f,2.5f,-2.1875f)) < .0001f);
            var references = System.Environment.GetEnvironmentVariable("KM2_TRAIN_REFERENCE");
            if (!string.IsNullOrEmpty(references)) {
                int count = 0; float error = 0;
                foreach (var route in JArray.Parse(File.ReadAllText(references))) {
                    var native = route["points"].Select(p => new Vector3(-p[0].Value<float>(),p[1].Value<float>(),p[2].Value<float>())).ToList();
                    var stock = SplineCurves.Create(settings,state,native);
                    foreach (var reference in route["samples"]) {
                        var expected = new Vector3(-reference["position"][0].Value<float>(),reference["position"][1].Value<float>(),reference["position"][2].Value<float>());
                        error = Math.Max(error, stock.Position(reference["t"].Value<float>()).DistanceTo(expected)); count++;
                    }
                }
                Check("emulated London train positions (" + count + ", max " + error + " m)", count > 0 && error < .0003f);
            }
            var samples = curve.Sample(20,.05f,8);
            float deviation = 0;
            for (int i = 0; i <= 512; i++) {
                var p = curve.Position(i/512f);
                deviation = Math.Max(deviation,p.DistanceTo(GeometryHelper.ClosestPointToCurve(p,samples.points,out _)));
            }
            Check("adaptive preview follows the game curve within 5 cm", deviation <= .051f && samples.parameters.Count == samples.points.Count);
            var id = (await Call("customElement.create", new { type="train", name="Train preview", points=new { spline=controls.Select(p => new { position=SplinePosition(p+new Vector3(-450,25,-150)) }).ToArray() } })).Value<int>();
            CustomElement Element() => (CustomElement)History.UndoManager.GetAliveElement(id);
            var element = Element();
            Check("train creates a line without surface geometry", !element.generator.DrawsMesh() && element.generator.m.GetSurfaceCount() == 0 && element.splineData.sections.Length > controls.Count);
            var preview = CustomElementSplineGeometry.Build(element, out var fullSpline);
            var offset = new Vector3(-450,25,-150);
            Check("train draws only its yellow route, without a cyan control polygon", preview.lines.Count == 1 && preview.lines[0].color == GameEngine.Color.yellow);
            var displayedRoute = preview.lines[0].points;
            Check("train preview stops exactly at its second and penultimate controls", displayedRoute[0].DistanceTo(controls[1]+offset) < .0001f
                && displayedRoute[^1].DistanceTo(controls[^2]+offset) < .0001f);
            var interior = fullSpline.sections.Where(s => s.parameter >= .25f && s.parameter <= .75f).Select(s => s.Position).ToArray();
            Check("train trimming preserves the evaluated curve and full derived sections", displayedRoute.SequenceEqual(interior)
                && fullSpline.sections[0].Position.DistanceTo(controls[0]+offset) < .0001f
                && fullSpline.sections[^1].Position.DistanceTo(controls[^1]+offset) < .0001f);
            builder.CloseAllPanels(); element.SetActive(false,false); element.Select(false);
            var lines = element.generator.FindChildren("*", "", true, false).OfType<GameEngine.LineRenderer>().ToArray();
            Check("train preview remains visible when deselected", lines.Any(l => l.enabled));
            await ToSignal(GetTree(), "physics_frame"); await Frames(2);
            Check("train preview is lifted in world Y above its unchanged spline", lines.Length == 1
                && Mathf.Abs(lines[0].GetAabb().Position.Y - displayedRoute.Min(p => p.Y) - .25f) < .001f);
            var middle = element.splineData.sections[element.splineData.sections.Length/2].center;
            var position = new Vector3(middle[0],middle[1],middle[2]);
            Check("deselected train can be selected through its line collider",GameEngine.Raycast.DoRaycast(element,position+Vector3.Up*20,Vector3.Down,out var hit,40,1<<7) && hit.collider == element.generator);
            Check("train selection collider follows the preview lift", hit.collider == element.generator && Mathf.Abs(hit.point.Y-position.Y-.25f) < .001f);
            var omitted = curve.Position(.125f)+offset;
            Check("trimmed train end spans cannot be picked through the route collider", !GameEngine.Raycast.DoRaycast(element,omitted+Vector3.Up*20,Vector3.Down,out _,40,1<<7));
            builder.SelectObject(element, true, null);
            await Frames(2);
            Check("editing a train does not restore the cyan control polygon", element.generator.FindChildren("*", "", true, false).OfType<GameEngine.LineRenderer>().Count(l => l.enabled) == 1);
            builder.CloseAllPanels();
            for (int count = 0; count <= 3; count++) {
                var incomplete = (await Call("customElement.create", new { type="train", name="Short train " + count,
                    points=new { spline=controls.Take(count).Select(p => new { position=SplinePosition(p+new Vector3(-600,25,-150)) }).ToArray() } })).Value<int>();
                var shortTrain = (CustomElement)History.UndoManager.GetAliveElement(incomplete);
                builder.SelectObject(shortTrain, true, null);
                await Frames(1);
                Check("train with " + count + " controls remains editable without a route line", shortTrain.GetAllPoints().Count == count
                    && builder.customElementEditorPanel.ActiveSelf() && CustomElementSplineGeometry.Build(shortTrain,out _).lines.Count == 0);
                await Call("customElement.delete",new {element=incomplete});
            }
            Check("trains have no preset category", Operations.CustomElementPresets.Category("train") == null);
            var schema = await Call("state.schema",new { @ref=id });
            Check("train schema has no presets", schema["presetCategory"].Type == JTokenType.Null);
            Check("train tension cannot be edited", !schema["parameters"].Any(p => p["name"].Value<string>() == "hermiteTension"));
            await Expect("train rejects built-in Hermite", "invalid_value", "state.setParam",new { @ref=id,name="curveType",value=1 });
            await Expect("train rejects imported curve changes", "invalid_value", "state.import",new { @ref=id,values=new {curveType=1} });
            await Expect("train presets are disabled", "invalid_value", "presets.save",new {category="customElement/train",name="Invalid train",values=new {type="train",curveType=3} });
            await Call("customElement.create",new { type="train",name="Incomplete train" });
            await CheckUndo("adding a train point", () => Call("customElement.insert", new { element=id,array="spline",position=new[] {-410,24,-105} }));
            var export = await Call("elements.geometry",new { @ref=id });
            var authored = (JArray)export["element"]["points"]["spline"];
            var before = authored.DeepClone();
            await Call("state.setParam",new { @ref=id,name="segments",value=80 });
            var after = await Call("elements.geometry",new { @ref=id });
            Check("export JSON retains authored controls independent of tessellation",JToken.DeepEquals(before,after["element"]["points"]["spline"]));
            if (DisplayServer.GetName() != "headless") {
                await Call("view.camera.set",new {position=new[] {-432,95,-140},target=new[] {-432,30,-140} });
                await Frames(5); Screenshot("train_preview.png");
            }
            await Call("city.save"); await Call("city.reload");
            element = M.customElements.First(e => GO.GetName(e) == "Train preview");
            Check("train save/reload rebuilds the custom preview",element.state.Int("curveType") == 3 && element.GetAllPoints().Count() == authored.Count && element.splineData.sections.Length > authored.Count);
            var json = Path.Combine(folder,"..","trains_export.json");
            await Call("city.exportJson",new {path=json});
        }

        //Cardinal spline through a loop of controls with tangents 0.5 * (next - previous), on X, Y and Z
        static Vector3 LoopReference(List<Vector3> c, float t) {
            int n = c.Count, span = Math.Min(n - 1, (int)Math.Floor(t * n));
            float u = t * n - span, u2 = u * u, u3 = u2 * u;
            Vector3 a = c[(span + n - 1) % n], b = c[span % n], p2 = c[(span + 1) % n], d = c[(span + 2) % n];
            return b * (2 * u3 - 3 * u2 + 1) + p2 * (-2 * u3 + 3 * u2) + (p2 - a) * .5f * (u3 - 2 * u2 + u) + (d - b) * .5f * (u3 - u2);
        }

        async Task BoatSplines() {
            Log("boat splines");
            var settings = TP.GetCustomElementTypes()["movingObject"].typeData.settings.spline;
            Check("boat is a closed spline restricted to its own curve", settings.closed && settings.curveTypes.SequenceEqual(new[] { 4 }) &&
                !SplineCurves.Allowed(TP.GetRoadTypes()["psdl"].typeData.settings).Contains(4) &&
                !TP.GetCustomElementTypes()["train"].typeData.settings.spline.curveTypes.Contains(4));
            var controls = new List<Vector3> { new(0,-2,0), new(40,3,10), new(50,-2,50), new(10,6,60), new(-20,-2,30) };
            var state = new ObjectState(); state.SetInt("curveType",4);
            var curve = SplineCurves.Create(settings,state,controls,closed:true);
            Check("closed boat curve has one control parameter more than its points", curve.controlParameters.Count == controls.Count + 1
                && curve.controlParameters[^1] == 1 && Mathf.Abs(curve.controlParameters[2] - 2f/controls.Count) < .0001f);
            Check("boat curve passes through every control", Enumerable.Range(0,controls.Count).All(i => curve.Position(i/(float)controls.Count).DistanceTo(controls[i]) < .001f));
            Check("boat loop ends where it starts", curve.Position(0).DistanceTo(curve.Position(1)) < .001f);
            Check("boat curve is cubic on X, Y and Z with wrapped neighbors", new[] { .05f, .3f, .49f, .75f, .95f }.All(t => curve.Position(t).DistanceTo(LoopReference(controls,t)) < .001f));
            Check("boat loop tangent is continuous at the seam", curve.Tangent(0).DistanceTo(curve.Tangent(1)) < .01f);
            var open = SplineCurves.Create(settings,state,controls.Take(2).ToList(),closed:true);
            Check("a loop of two points stays an open line", open.controlParameters.Count == 2);
            var offset = new Vector3(-450,25,-250);
            var id = (await Call("customElement.create", new { type="movingObject", name="Boat loop", state=new { model=2, speed=3 },
                points=new { spline=controls.Select(p => new { position=SplinePosition(p+offset) }).ToArray() } })).Value<int>();
            var element = (CustomElement)History.UndoManager.GetAliveElement(id);
            var data = element.splineData;
            Check("boat spline data is marked closed and ends at its first section", data.closed && data.sections.Length > controls.Count + 1
                && data.sections[0].Position.DistanceTo(data.sections[^1].Position) < .0001f);
            Check("boat spline pieces close the loop", data.pieces.Length == data.sections.Length - 1
                && data.pieces[^1][1].Position.DistanceTo(data.sections[0].Position) < .0001f);
            Check("boat sections follow the loop at the controls", controls.All(c => data.sections.Any(s => s.Position.DistanceTo(c+offset) < .01f)));
            var preview = CustomElementSplineGeometry.Build(element, out _);
            Check("boat draws one closed route line", preview.lines.Count == 1 && preview.lines[0].points.Count >= controls.Count + 1
                && preview.lines[0].points[0].DistanceTo(preview.lines[0].points[^1]) < .0001f);
            Check("boat has no surface geometry", !element.generator.DrawsMesh() && element.generator.m.GetSurfaceCount() == 0);
            builder.SelectObject(element, true, null); await Frames(2);
            var panelParameters = ((System.Collections.IDictionary)typeof(EditorPanel).GetField("parameterMap", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .GetValue(builder.customElementEditorPanel)).Values.Cast<CityElements.Types.Parameter>().Select(p => p.name).ToArray();
            Check("boat panel opens with its model and speed fields", builder.customElementEditorPanel.ActiveSelf()
                && panelParameters.Contains("model") && panelParameters.Contains("speed"));
            builder.CloseAllPanels();
            var schema = await Call("state.schema",new { @ref=id });
            Check("boat schema has a model, a speed and no presets", schema["presetCategory"].Type == JTokenType.Null
                && schema["parameters"].Any(p => p["name"].Value<string>() == "model") && schema["parameters"].Any(p => p["name"].Value<string>() == "speed"));
            await Expect("boat rejects the train curve", "invalid_value", "state.setParam",new { @ref=id,name="curveType",value=3 });
            for (int count = 0; count <= 2; count++) {
                var incomplete = (await Call("customElement.create", new { type="movingObject", name="Short boat " + count,
                    points=new { spline=controls.Take(count).Select(p => new { position=SplinePosition(p+offset+new Vector3(100,0,0)) }).ToArray() } })).Value<int>();
                var shortBoat = (CustomElement)History.UndoManager.GetAliveElement(incomplete);
                CustomElementSplineGeometry.Build(shortBoat, out var shortData);
                Check("boat with " + count + " controls stays editable and open", shortBoat.GetAllPoints().Count == count && !shortData.closed);
                await Call("customElement.delete",new {element=incomplete});
            }
            await CheckUndo("adding a boat point", () => Call("customElement.insert", new { element=id,array="spline",position=SplinePosition(new Vector3(-40,0,20)+offset) }));
            var export = await Call("elements.geometry",new { @ref=id });
            Check("exported boat spline is closed and keeps the authored controls", export["element"]["spline"]["closed"].Value<bool>()
                && export["element"]["points"]["spline"].Count() == controls.Count + 1);
            if (DisplayServer.GetName() != "headless") {
                await Call("view.camera.set",new {position=new[] {-430,80,-215},target=new[] {-430,20,-215} });
                await Frames(5); Screenshot("boat_loop.png");
            }
            await Call("city.save"); await Call("city.reload");
            element = M.customElements.First(e => GO.GetName(e) == "Boat loop");
            Check("boat save/reload keeps the loop and its state", element.state.Int("curveType") == 4 && element.state.Int("model") == 2
                && element.splineData.closed && element.GetAllPoints().Count() == controls.Count + 1);
            var json = Path.Combine(folder,"..","boats_export.json");
            await Call("city.exportJson",new {path=json});
        }

        //The generic closed loop with the built-in curves and a generated strip (a synthetic type based on the decal)
        async Task ClosedSplines() {
            Log("closed splines");
            var types = TP.GetCustomElementTypes();
            var source = JsonConvert.DeserializeObject<CityElements.Types.CustomElementType>(JsonConvert.SerializeObject(types["decal"].typeData));
            var settings = source.settings.spline;
            settings.closed = true; settings.curveTypes = new[] { 1, 2 }; settings.defaultCurveType = 1;
            settings.projectPoints = settings.projectSections = false;
            source.settings.controlPoints = CityElements.Types.CustomElementType.SplinePoints(true);
            const string key = "__testClosedSpline";
            types[key] = new CityElements.Types.Runtime.CustomElementType(source, key);
            var controls = new[] { new[] { 600,0,600 }, new[] { 630,0,600 }, new[] { 630,0,630 }, new[] { 600,0,630 } };
            try {
                foreach (var mode in new[] { 1, 2 }) {
                    var id = (await Call("customElement.create", new { type = key, name = "Closed " + mode, state = new { curveType = mode, width = 3, texture = "texture/decal_zigzag_l.tex" },
                        points = new { spline = controls.Select(p => new { position = p }).ToArray() } })).Value<int>();
                    var element = (CustomElement)History.UndoManager.GetAliveElement(id);
                    var data = element.splineData;
                    var name = "closed curve " + mode;
                    Check(name + " ends where it starts", data.closed && data.sections.Length >= 5 && data.sections[0].Position.DistanceTo(data.sections[^1].Position) < .0001f);
                    Check(name + " passes through the controls", controls.All(c => data.sections.Any(s => s.Position.DistanceTo(new Vector3(c[0],c[1],c[2])) < .01f)));
                    Check(name + " closes its last piece", data.pieces.Length >= 4 && data.pieces[^1][1].Position.DistanceTo(data.sections[0].Position) < .0001f);
                    Check(name + " generates its strip", element.generator.m.GetSurfaceCount() > 0);
                    if (mode == 2) Check("closed low-poly curve has the controls and the closing point", data.sections.Length == controls.Length + 1);
                    var line = CustomElementSplineGeometry.Build(element, out _).lines[0].points;
                    Check(name + " draws a closed line", line[0].DistanceTo(line[^1]) < .0001f);
                    await Call("customElement.delete",new {element=id});
                }
            } finally {
                types.Remove(key);
            }
        }

        async Task SplinePlacementHighlights(string type) {
            Log(type + " placement highlights");
            var panel = builder.customElementEditorPanel;
            var placer = builder.customElementPlacer;
            var colorField = typeof(Handle).GetField("handleColor", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Color ColorOf(TerrainPoint p) => (Color)colorField.GetValue(GameEngine.Scriptable.Cast<Handle>(p.GetObject()));
            bool Yellow(TerrainPoint p) => ColorOf(p) == GameEngine.Color.yellow;
            TerrainPoint[] Points() => placer.GetElement().GetLeaves().Select(p => p.leaf.point).ToArray();
            void Place(Vector3 position) => History.UndoManager.Record("place " + type + " point", () =>
                placer.PlaceAt(new GameEngine.Raycast.RaycastHit { point=position }));
            builder.CloseAllPanels();
            History.UndoManager.Record("start " + type, () => CallBuilder<object>("StartCustomElementCreation", type, "CREATE_CUSTOM_ELEMENT"));
            await Frames(2);
            var origin = new Vector3(600,0,type == "train" ? 600 : 750);
            Place(origin); await Frames(2);
            Check(type + " creation highlights its first placement point", Points().Length == 1 && Yellow(Points()[0]));
            for (int i = 1; i <= 3; i++) Place(origin+new Vector3(i*20,0,i*5));
            await Frames(3);
            var element = placer.GetElement();
            var id = History.ElementRegistry.GetId(element);
            var points = Points();
            Check(type + " append highlights only the last point", points.Take(points.Length-1).All(p => !Yellow(p)) && Yellow(points[^1]));
            var oldEnd = points[^1];
            oldEnd.Select(false); oldEnd.UpdateOlds();
            Check(type + " placement color survives point selection and frame color updates", Yellow(oldEnd));
            panel.SetPointPlacementMode(2); await Frames(2);
            Check(type + " prepend changes the highlighted end", Yellow(Points()[0]) && !Yellow(oldEnd));
            var oldStart = Points()[0];
            Place(origin-new Vector3(20,0,5)); await Frames(2);
            Check(type + " prepending moves the highlight to the new first point", Yellow(Points()[0]) && !Yellow(oldStart));
            var reference = Points()[2];
            builder.gizmo.ClearTargets(); builder.gizmo.AddTarget(reference.GetObject());
            foreach (var mode in new[] { 3, 4 }) {
                panel.SetPointPlacementMode(mode); await Frames(2);
                Check(type + " insertion mode " + mode + " highlights only the selected reference", Yellow(reference)
                    && Points().Where(p => p != reference).All(p => !Yellow(p)));
            }
            builder.gizmo.ClearTargets(); await Frames(2);
            Check(type + " insertion without a reference has no yellow point", Points().All(p => !Yellow(p)));
            panel.SetPointPlacementMode(0); await Frames(1);
            Check(type + " disabling placement restores all point colors", Points().All(p => !Yellow(p)));
            panel.SetPointPlacementMode(1); await Frames(1);
            History.UndoManager.Record("remove highlighted " + type + " endpoint", () => {
                Operations.CustomElements.RemovePoint(element, Points()[^1]); M.FlagAsChanged();
            });
            await Frames(3);
            Check(type + " removing the highlighted endpoint marks the remaining end", Yellow(Points()[^1]));
            History.UndoManager.Undo(); await Frames(3);
            Check(type + " undo restores placement mode and endpoint color", placer.GetElement() == History.UndoManager.GetAliveElement(id)
                && Points().Length == 5 && Yellow(Points()[^1]));
            History.UndoManager.Redo(); await Frames(3);
            Check(type + " redo refreshes endpoint color", Points().Length == 4 && Yellow(Points()[^1]));
            element = placer.GetElement();
            var shared = Points()[^1];
            CustomElement other = null;
            History.UndoManager.Record("share " + type + " placement point", () => {
                other = Operations.CustomElements.Create(M,type,origin);
                Operations.CustomElements.AppendPoint(M,other,"spline",shared.GetObject(),shared.GetPoint());
            });
            other.SetActive(false,false); other.Select(false);
            Check(type + " an inactive shared-point owner preserves the placement color", Yellow(shared));
            other.SetActive(true,true); other.SetPlacementPoint(shared);
            panel.SetPointPlacementMode(0);
            Check(type + " another active owner retains its shared-point highlight", Yellow(shared));
            other.SetActive(false,false);
            Check(type + " the shared-point color restores when both owners stop highlighting", !Yellow(shared));
            panel.SetPointPlacementMode(1); await Frames(2);
            builder.CloseAllPanels(); await Frames(2);
            Check(type + " closing the editor clears the placement highlight", !Yellow(shared));
            builder.SelectObject(element,true,null); panel.SetPointPlacementMode(2); await Frames(2);
            Check(type + " reopening follows the new placement mode", Yellow(Points()[0]) && !Yellow(shared));
            if (type == "decal") SplineLineTrimming(element);
            builder.CloseAllPanels();
            await Call("customElement.delete",new {element=id});
            await Call("customElement.delete",new {element=History.ElementRegistry.GetId(other)});
        }

        void SplineLineTrimming(CustomElement element) {
            var settings = element.Settings.spline;
            var oldLine = settings.line;
            var oldMode = element.state.Int("curveType");
            try {
                settings.line = new CityElements.Types.CustomElementSplineLine { trimStart=1, trimEnd=1 };
                var controls = element.GetLeaves().Select(p => p.leaf.point.GetPoint()).ToList();
                foreach (var mode in new[] { 0, 1, 2 }) {
                    element.state.SetInt("curveType",mode);
                    var result = CustomElementSplineGeometry.Build(element,out _);
                    var knots = GeometryHelper.GetPointParameters(controls,(GeometryHelper.CurveType)mode,element.state.Bool("subdivideEqually"));
                    Vector3 Expected(int index) {
                        var p = mode == 2 ? controls[index] : GeometryHelper.GetPointOnCurve(controls[0],controls.GetRange(1,controls.Count-2),controls[^1],knots[index],index,
                            (GeometryHelper.CurveType)mode,element.state.Float("hermiteTension",.5f),element.state.Bool("subdivideEqually"));
                        return GeometryHelper.AutoProjectOnSurface(p,settings.projectionMask,element.generator);
                    }
                    Check("generic line trimming supports projected curve " + mode, result.lines.Count == 1
                        && result.lines[0].points[0].DistanceTo(Expected(1)) < .001f && result.lines[0].points[^1].DistanceTo(Expected(controls.Count-2)) < .001f);
                }
                settings.line.trimStart = int.MaxValue;
                Check("trimming past the spline hides the line safely",CustomElementSplineGeometry.Build(element,out _).lines.Count == 0);
            } finally {
                settings.line = oldLine;
                element.state.SetInt("curveType",oldMode);
            }
        }

        async Task CustomRoadSplines() {
            Log("custom road splines");
            var core = Path.GetDirectoryName(CoreElementsDir());
            var registry = Path.Combine(core,"splineTypes.json");
            var registryBefore = File.ReadAllBytes(registry);
            var typeDir = Path.Combine(CoreElementsDir(),"roads","zzSplineTest");
            var module = Path.Combine(core,"lua","zzSplineSelfTest.lua");
            if (Directory.Exists(typeDir) || File.Exists(module)) throw new Abort("custom curve test fixtures already exist");
            try {
                File.Copy(Path.Combine(PathHelper.BasePath(),"Files","tests","customSplines.lua"),module);
                var definitions = JArray.Parse(File.ReadAllText(registry));
                definitions.Add(JObject.FromObject(new SplineCurves.Definition {id=90,name="syntheticCubic",label="RD_SP_T_BEZIER",prepare="zzSplineSelfTest.prepare",evaluate="zzSplineSelfTest.evaluate",
                    controlParameters="zzSplineSelfTest.controlParameters",interpolateValues="zzSplineSelfTest.interpolateValues",split="zzSplineSelfTest.split",merge="zzSplineSelfTest.merge"}));
                File.WriteAllText(registry,definitions.ToString());
                SplineCurves.Reset();
                Directory.CreateDirectory(typeDir);
                File.WriteAllText(Path.Combine(typeDir,"type.json"),"{\"extends\":\"psdl\"}");
                File.WriteAllText(Path.Combine(typeDir,"settings.json"),"{\"curveTypes\":[90],\"defaultCurveType\":90}");
                File.WriteAllText(Path.Combine(typeDir,"parameters.json"),"{\"parameters\":[{\"name\":\"originalCurve\",\"type\":\"string\"},{\"name\":\"originalRotations\",\"type\":\"string\"}]}");
                TP.GetRoadTypes(true);
                var validRegistry = File.ReadAllText(registry);
                try {
                    var invalidRegistry = JArray.Parse(validRegistry);
                    invalidRegistry.Add(invalidRegistry[0].DeepClone());
                    File.WriteAllText(registry,invalidRegistry.ToString()); SplineCurves.Reset();
                    bool rejected = false;
                    try { _ = SplineCurves.Definitions; } catch (ArgumentException) { rejected = true; }
                    Check("duplicate custom curve IDs are rejected",rejected);
                } finally { File.WriteAllText(registry,validRegistry); SplineCurves.Reset(); }
                var settings = TP.GetRoadTypes()["zzSplineTest"].typeData.settings;
                var points = new List<Vector3> { new(500,5,500),new(530,20,520),new(590,10,580),new(620,5,600) };
                var state = new ObjectState(); state.SetInt("curveType",90);
                var startTangent = (points[1]-points[0])*3;
                var endTangent = (points[3]-points[2])*3;
                var curve = SplineCurves.Create(settings,state,points,startTangent,endTangent);
                Check("custom evaluator receives and honors road endpoint tangents",curve.Tangent(0).DistanceTo(startTangent) < .001f && curve.Tangent(1).DistanceTo(endTangent) < .001f);
                Check("custom cubic position matches the built-in polynomial",curve.Position(.5f).DistanceTo(GeometryHelper.GetPointOnCurve(points[0],points.GetRange(1,2),points[3],.5f,0,GeometryHelper.CurveType.Bezier)) < .0001f);
                var id = (await Call("roads.create",new {type="zzSplineTest",points=points.Select(p => new { position=SplinePosition(p) }).ToArray()})).Value<int>();
                Road Road() => (Road)History.UndoManager.GetAliveElement(id);
                var road = Road();
                Check("custom road draws geometry using its registered evaluator",road.customCurve != null && road.generator.customCurve != null && road.generator.m.GetSurfaceCount() > 0);
                var schema = await Call("state.schema",new { @ref=id });
                Check("road API exposes the allowed stable custom name",schema["builtins"].First(p => p["name"].Value<string>() == "curveType")["enumValues"].Values<string>().SequenceEqual(new[] {"syntheticCubic"}));
                await Call("state.setBuiltin",new { @ref=id,name="curveType",value="syntheticCubic" });
                await Expect("custom road rejects the train evaluator", "invalid_value","state.setBuiltin",new { @ref=id,name="curveType",value="mm2Train" });
                await Expect("custom road rejects inappropriate preset", "invalid_value","presets.save",new {category="road",name="Wrong curve",values=new {type="zzSplineTest",curveType=3} });
                await Call("roads.setPointRotation",new {road=id,index=1,yaw=4,roll=8});
                Check("custom road interpolates banking values",road.customCurve.Interpolate(road.GetPointRotations(),.5f).Length() > 0);
                points = road.points.Select(GO.GetPosition).ToList();
                curve = SplineCurves.Create(settings,road.state,points);
                var oldPrepared = road.customCurve;
                await Call("roads.movePoint",new { road=id,index=1,position=SplinePosition(points[1]+new Vector3(2,0,1)) });
                Check("editing a road invalidates its prepared curve",road.customCurve != oldPrepared);
                points = road.points.Select(GO.GetPosition).ToList(); curve = SplineCurves.Create(settings,road.state,points);
                var originalHandle = road.points[0];
                var originalRotations = road.GetPointRotations();
                var split = SplineCurveOperations.Split(road,.5f);
                Check("custom split retains source handles and the exact join",split.first.sources[0] == originalHandle && split.first.points[^1].DistanceTo(curve.Position(.5f)) < .001f);
                var firstCurve = SplineCurves.Create(settings,split.first.state,split.first.points);
                Check("custom split preserves its source curve",Enumerable.Range(0,17).All(i => firstCurve.Position(i/16f).DistanceTo(curve.Position(i/32f)) < .001f));
                var temporary = new List<Road>();
                try {
                    var firstRoad = M.CreateRoad(); temporary.Add(firstRoad); firstRoad.SetState(split.first.state);
                    for (int k=0;k<split.first.points.Count;k++) firstRoad.AddPoint(split.first.points[k],-1,split.first.rotations[k]);
                    var secondRoad = M.CreateRoad(); temporary.Add(secondRoad); secondRoad.SetState(split.second.state);
                    for (int k=split.second.points.Count-1;k>=0;k--) secondRoad.AddPoint(split.second.points[k],-1,split.second.rotations[k]);
                    var reverse = SplineCurveOperations.Merge(firstRoad,secondRoad,true,false,split.first.points[^1]);
                    Check("custom merge supports a reversed second road",reverse.points.Count == points.Count && reverse.points.Zip(points).All(p => p.First.DistanceTo(p.Second) < .001f));
                } finally { foreach (var r in temporary) r.Delete(); M.FlagAsChanged(); }
                var definition = SplineCurves.Definitions[90];
                var evaluate = definition.evaluate; var splitCallback = definition.split;
                try {
                    definition.evaluate = "zzSplineSelfTest.invalid";
                    bool invalid = false;
                    try { SplineCurves.Create(settings,road.state,points).Position(.5f); } catch (ArgumentException) { invalid=true; }
                    Check("non-finite custom curve results are rejected",invalid);
                    definition.evaluate = evaluate;
                    definition.split = "zzSplineSelfTest.fail";
                    var beforeFailure = road.points.ToArray(); bool failed = false;
                    try { SplineCurveOperations.Split(road,.5f); } catch (ArgumentException) { failed=true; }
                    Check("callback failure leaves road topology unchanged",failed && road.points.SequenceEqual(beforeFailure));
                    definition.split = null; failed = false;
                    try { SplineCurveOperations.Split(road,.5f); } catch (InvalidOperationException) { failed=true; }
                    Check("missing custom split callback is rejected",failed);
                } finally { definition.evaluate=evaluate; definition.split=splitCallback; }
                var tangent = curve.Tangent(.5f);
                var result = await Call("roads.split",new {road=id,near=SplinePosition(curve.Position(.5f))});
                var iid = result["intersection"].Value<int>();
                var otherId = result["newRoad"].Value<int>();
                Check("custom road split applies its callback",Road().state.Str("originalCurve",null) != null);
                await Call("state.setBuiltin",new { @ref=id,name="endIntersectionStart",value=true });
                await Call("state.setBuiltin",new { @ref=otherId,name="startIntersectionEnd",value=true });
                await Call("intersections.setSeamless",new {intersection=iid,seamless=false});
                Check("custom roads retain intersection through-strip geometry",((Intersection)History.UndoManager.GetAliveElement(iid)).GetRoadsThrough().Count > 0);
                await Call("state.setBuiltin",new { @ref=id,name="endIntersectionStart",value=false });
                await Call("state.setBuiltin",new { @ref=otherId,name="startIntersectionEnd",value=false });
                await Call("intersections.setSeamless",new {intersection=iid,seamless=true});
                await Call("intersections.mergeRoads",new {intersection=iid,keep=id});
                Check("custom merge restores the authored cubic",Road().points.Count == 4 && GO.GetPosition(Road().points[0]).DistanceTo(points[0]) < .001f);

                Check("retained road endpoint keeps its identity",Road().points[0] == originalHandle);
                Check("custom split/merge restores banking controls",Road().GetPointRotations().Zip(originalRotations).All(p => p.First.DistanceTo(p.Second)<.001f));
                await Call("history.undo"); await Call("history.redo");
                Check("custom road topology supports undo/redo",Road().points.Count == 4 && Road().state.Int("curveType") == 90);
                await Call("presets.save",new { category="road",name="Custom road",values=JObject.FromObject(Road().state) });
                await Call("city.save"); await Call("city.reload");
                road = M.roads.First(r => r.state.Str("type") == "zzSplineTest");
                Check("custom road survives save/reload",road.customCurve != null && road.state.Int("curveType") == 90);
                var geometry = await Call("elements.geometry",new { @ref=History.ElementRegistry.GetId(road) });
                Check("custom road export retains authored control count",(geometry["element"]["b64v3a_points"] ?? geometry["element"]["points"]) != null);
                await Call("presets.delete",new {category="road",name="Custom road"});
                var psdl = (await Call("roads.create",new {points=new[] {Pos(-700,500),Pos(-600,500)}})).Value<int>();
                await Expect("ordinary roads cannot select train curves", "invalid_value","state.setBuiltin",new { @ref=psdl,name="curveType",value=3 });
                await Call("roads.delete",new {road=History.ElementRegistry.GetId(road)});
                await Call("city.save");
            } finally {
                File.WriteAllBytes(registry,registryBefore);
                File.Delete(module);
                if (Directory.Exists(typeDir)) Directory.Delete(typeDir,true);
                SplineCurves.Reset(); TP.GetRoadTypes(true);
            }
        }
    }
}
