using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Godot;
using Newtonsoft.Json.Linq;
using TP = CityElements.Types.Parsers.TypeParser;

namespace Api {
    public partial class ApiSelfTest {
        //the values of the spline of a road are in its instance state: the presets neither store them nor change them
        async Task SplineInstanceState() {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            T Field<T>(object obj, string name) => (T)obj.GetType().GetField(name, flags).GetValue(obj);
            var builtins = Operations.Builtins.RoadSplineProperties;
            bool NoneIn(ObjectState state) => !builtins.Any(state.properties.ContainsKey);
            var type = TP.GetRoadTypes()["psdl"];
            var settings = type.typeData.settings;
            var panel = Field<EditorPanels.RoadEditorPanel>(builder, "roadEditorPanel");

            //the defaults come from the settings of the type
            Check("the core sets the defaults of the psdl roads", settings.segments == 10 && settings.project && settings.makeCoplanar && settings.defaultCurveType == 0);
            var oldSegments = settings.segments;
            Road defaultRoad;
            try {
                settings.segments = 33;
                defaultRoad = (Road)History.UndoManager.GetAliveElement((await Call("roads.create", new { points = new[] { Pos(-900, -900), Pos(-860, -900) } })).Value<int>());
            } finally { settings.segments = oldSegments; }
            Check("a new road takes its values from the settings of its type", defaultRoad.instanceState.Int("segments") == 33 && defaultRoad.instanceState.Bool("project")
                && defaultRoad.instanceState.Bool("makeCoplanar") && defaultRoad.instanceState.Float("hermiteTension") == settings.hermiteTension);
            Check("and has none of them in its state", NoneIn(defaultRoad.state));
            await Call("roads.delete", new { road = History.ElementRegistry.GetId(defaultRoad) });

            //the API and the fields of the state
            var id = (await Call("roads.create", new { points = new[] { Pos(-900, -800), Pos(-850, -780), Pos(-800, -800) } })).Value<int>();
            Road Road() => (Road)History.UndoManager.GetAliveElement(id);
            await Call("state.setBuiltin", new { @ref = id, name = "curveType", value = "hermite" });
            await Call("state.setBuiltin", new { @ref = id, name = "segments", value = 17 });
            await Call("state.setBuiltin", new { @ref = id, name = "lpf", value = 2 });
            await Call("state.setBuiltin", new { @ref = id, name = "hermiteTension", value = 0.25f });
            await Call("state.setBuiltin", new { @ref = id, name = "project", value = false });
            await Call("state.setBuiltin", new { @ref = id, name = "makeCoplanar", value = false });
            await Call("state.setParam", new { @ref = id, name = "warp", value = true });
            bool Kept() => Road().instanceState.Int("curveType") == 1 && Road().instanceState.Int("lpf") == 2 && Road().instanceState.Float("hermiteTension") == 0.25f
                && !Road().instanceState.Bool("project") && !Road().instanceState.Bool("makeCoplanar") && Road().instanceState.Bool("warp");
            Check("the API sets them in the instance state", Kept() && Road().instanceState.Int("segments") == 17 && NoneIn(Road().state) && !Road().state.properties.ContainsKey("warp"));
            Check("and reads them from there", (await Call("state.getParam", new { @ref = id, name = "segments" })).Value<int>() == 17
                && (await Call("state.getParam", new { @ref = id, name = "warp" })).Value<bool>());
            Check("the schema flags them as instance specific", (await Call("state.schema", new { @ref = id }))["builtins"].First(p => p["name"].Value<string>() == "segments")["instanceSpecific"].Value<bool>());

            await Call("state.setBuiltin", new { @ref = id, name = "segments", value = 18 });
            await Call("history.undo");
            Check("undo restores them", Road().instanceState.Int("segments") == 17);
            await Call("history.redo");
            Check("redo too", Road().instanceState.Int("segments") == 18);
            await Call("history.undo");

            //the presets
            const string presetName = "Spline test";
            var values = JObject.FromObject(Road().state);
            values["segments"] = 99;
            values["project"] = true;
            await Call("presets.save", new { category = "road", name = presetName, values });
            var saved = (await Call("presets.get", new { category = "road", name = presetName })).ToObject<ObjectState>();
            Check("a saved preset doesn't store them", NoneIn(saved));
            await Call("state.applyPreset", new { @ref = id, name = presetName });
            Check("loading a preset keeps them", Kept() && Road().instanceState.Int("segments") == 17);
            Check("and the road still matches its preset", Road().state.Equals(PresetManager.GetPresetByName(presetName, "road")));
            await Call("state.setBuiltin", new { @ref = id, name = "lpf", value = 3 });
            Check("changing them doesn't unlink the road from its preset", Road().state.Equals(PresetManager.GetPresetByName(presetName, "road")));
            await Call("state.setBuiltin", new { @ref = id, name = "lpf", value = 2 });
            var changed = saved.Clone() as ObjectState;
            changed.SetFloat("roadWidth", saved.Float("roadWidth") + 1);
            await Call("presets.save", new { category = "road", name = presetName, values = JObject.FromObject(changed) });
            await Call("presets.sync", new { category = "road", name = presetName });
            Check("syncing a preset changes the road but not them", Road().state.Float("roadWidth") == changed.Float("roadWidth") && Kept() && Road().instanceState.Int("segments") == 17);

            //split and merge
            var split = await Call("roads.split", new { road = id, near = new { x = -850, y = 0, z = -780 } });
            var other = (Road)History.UndoManager.GetAliveElement(split["newRoad"].Value<int>());
            Check("both parts of a split road keep them", Kept() && other.instanceState.Int("curveType") == 1 && other.instanceState.Int("segments") == 17 && other.instanceState.Bool("warp"));
            var crossing = split["intersection"].Value<int>();
            await Call("state.setParam", new { @ref = crossing, name = "warp", value = true });
            var intersection = (Intersection)History.UndoManager.GetAliveElement(crossing);
            Check("the warp of an intersection is in its instance state", intersection.instanceState.Bool("warp") && !intersection.state.properties.ContainsKey("warp"));
            await Call("state.setParam", new { @ref = crossing, name = "warp", value = false });
            await Call("intersections.mergeRoads", new { intersection = crossing, keep = id });
            Check("merging adds the segments of the parts", Kept() && Road().instanceState.Int("segments") == 34);

            //the last road selected
            var last = Road();
            History.UndoManager.Record("select road", () => builder.SelectObject(last));
            await Frames(3);
            var placer = (ElementPlacer.RoadPlacer)builder.terrainClick.modifier;
            last.instanceState.SetInt("segments", 21); //edited after the selection
            last.instanceState.SetFloat("intersectionMove", 4);
            Road Created() {
                History.UndoManager.Record("create road", () => CallBuilder<object>("CreateRoad"));
                return placer.GetRoad(false);
            }
            var created = Created();
            await Frames(2);
            Check("a road created in the editor takes the values of the last road selected", created != null && created != last && created.instanceState.Int("segments") == 21
                && created.instanceState.Int("curveType") == 1 && created.instanceState.Int("lpf") == 2 && !created.instanceState.Bool("project")
                && !created.instanceState.Bool("makeCoplanar") && created.instanceState.Bool("warp"));
            Check("but not the other instance values", !created.instanceState.properties.ContainsKey("intersectionMove"));
            History.UndoManager.Record("end", panel.Terminate);
            await Frames(2);
            last.instanceState.SetInt("segments", 17);
            last.instanceState.RemoveProperty("intersectionMove");

            //the source is still there when the road is deleted
            var deletedRoad = (await Call("roads.create", new { points = new[] { Pos(-900, -650), Pos(-860, -650) } })).Value<int>();
            await Call("state.setBuiltin", new { @ref = deletedRoad, name = "segments", value = 41 });
            History.UndoManager.Record("select road", () => builder.SelectObject((Road)History.UndoManager.GetAliveElement(deletedRoad)));
            await Frames(3);
            History.UndoManager.Record("end", panel.Terminate);
            await Call("roads.delete", new { road = deletedRoad });
            await Frames(2);
            History.UndoManager.Record("select tool", () => builder.SetSelectObjectMode());
            created = Created();
            await Frames(2);
            Check("a road created after deleting the last one selected still takes its values", created != null && created.instanceState.Int("segments") == 41);
            History.UndoManager.Record("end", panel.Terminate);
            await Frames(2);

            var scripted = (await Call("roads.create", new { points = new[] { Pos(-900, -600), Pos(-860, -600) } })).Value<int>();
            Check("the API always uses the defaults", ((Road)History.UndoManager.GetAliveElement(scripted)).instanceState.Int("segments") == settings.segments);
            await Call("roads.delete", new { road = scripted });

            //what the type allows
            var limited = M.CreateRoad();
            var source = new ObjectState();
            source.SetInt("curveType", 3);
            limited.ReuseSplineValues(source);
            Check("a curve type the type doesn't allow falls back to its default", limited.instanceState.Int("curveType") == settings.defaultCurveType);
            var internalSettings = type.parameterContainer.internalParametersSettings;
            try {
                type.parameterContainer.internalParametersSettings = new[] { new CityElements.Types.InternalParameterSettings { name = "makeCoplanar", enabled = false } };
                source = new ObjectState();
                source.SetBool("makeCoplanar", !settings.makeCoplanar);
                limited.ReuseSplineValues(source);
                Check("makeCoplanar is not taken by a type hiding it", limited.instanceState.Bool("makeCoplanar") == settings.makeCoplanar);
            } finally { type.parameterContainer.internalParametersSettings = internalSettings; }
            limited.Delete();
            M.FlagAsChanged();

            //the editor of the presets
            History.UndoManager.Record("edit presets", builder.EditRoadPresets);
            await Frames(3);
            var pages = (IList)typeof(EditorPanel).GetField("pages", flags).GetValue(panel);
            var fields = pages.Cast<object>().SelectMany(page => Field<Dictionary<EditorPanelElement, FieldStatus>>(page, "fieldElements")).ToList();
            var splineFields = fields.Where(p => p.Value.fieldName.StartsWith("instanceState.properties.") && builtins.Contains(p.Value.fieldName.Substring("instanceState.properties.".Length))).ToList();
            Check("the editor of the presets disables them", splineFields.Count == builtins.Length - 1 && splineFields.All(p => !p.Key.GetInteractable()));
            Check("including the curve type (a dropdown)", !Field<EditorPanelElements.Dropdown>(panel, "curveTypeDropdown").GetInteractable());
            History.UndoManager.Record("end", panel.Terminate);
            await Frames(2);

            //save and reload
            await Call("state.setBuiltin", new { @ref = id, name = "segments", value = 55 });
            await Call("city.save");
            await Call("city.reload");
            var reloaded = M.roads.First(r => r.instanceState.Int("segments") == 55);
            Check("save and reload keep them", reloaded.instanceState.Int("curveType") == 1 && reloaded.instanceState.Int("lpf") == 2 && reloaded.instanceState.Bool("warp") && NoneIn(reloaded.state));
            Check("a reload forgets the last road selected", PresetManager.lastRoadInstanceState == null);
            await Call("roads.delete", new { road = History.ElementRegistry.GetId(reloaded) });
            await Call("presets.delete", new { category = "road", name = presetName });

            //the building lines
            var lineId = (await Call("lines.create", new { points = new[] { Pos(-900, -500), Pos(-860, -500) } })).Value<int>();
            var line = (BuildingLine)History.UndoManager.GetAliveElement(lineId);
            await Call("state.setBuiltin", new { @ref = lineId, name = "projectToGround", value = true });
            Check("the projection of a line is in its instance state", line.IsProjectedToGround() && line.instanceState.Bool("projectToGround") && !line.state.properties.ContainsKey("projectToGround"));
            await Call("state.applyPreset", new { @ref = lineId, name = "Default" });
            Check("loading a preset keeps it", line.IsProjectedToGround());
            var lineValues = JObject.FromObject(line.state);
            lineValues["projectToGround"] = true;
            await Call("presets.save", new { category = "buildingLine", name = presetName, values = lineValues });
            Check("a saved preset of a line doesn't store it", !(await Call("presets.get", new { category = "buildingLine", name = presetName })).ToObject<ObjectState>().properties.ContainsKey("projectToGround"));
            await Call("presets.delete", new { category = "buildingLine", name = presetName });
            await Call("lines.delete", new { line = lineId });

            //the custom element splines keep them in their single state, but not in the presets
            var decal = Operations.CustomElementPresets.DefaultState("decal");
            decal.SetInt("curveType", 1);
            decal.SetInt("segments", 77);
            decal.SetBool("subdivideEqually", true);
            var decalPreset = Operations.CustomElementPresets.Extract(decal, "decal");
            Check("a preset of a custom element spline doesn't store them", !new[] { "curveType", "segments", "segmentsPer100m", "hermiteTension", "subdivideEqually" }.Any(decalPreset.properties.ContainsKey));
            Operations.CustomElementPresets.Apply(decal, decalPreset, "decal");
            Check("and loading it keeps them", decal.Int("curveType") == 1 && decal.Int("segments") == 77 && decal.Bool("subdivideEqually"));
        }
    }
}
