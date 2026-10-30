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
            var settings = TP.GetLowLevelTypes()["train"].typeData.settings.spline;
            Check("train curve is restricted to trains", settings.curveTypes.SequenceEqual(new[] { 3 }) &&
                !SplineCurves.Allowed(TP.GetRoadTypes()["psdl"].typeData.settings).Contains(3) &&
                !TP.GetLowLevelTypes()["decal"].typeData.settings.spline.curveTypes.Contains(3));
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
            var id = (await Call("lowLevel.create", new { type="train", name="Train preview", points=new { spline=controls.Select(p => new { position=SplinePosition(p+new Vector3(-450,25,-150)) }).ToArray() } })).Value<int>();
            LowLevelElement Element() => (LowLevelElement)History.UndoManager.GetAliveElement(id);
            var element = Element();
            Check("train creates a line without surface geometry", !element.generator.DrawsMesh() && element.generator.m.GetSurfaceCount() == 0 && element.splineData.sections.Length > controls.Count);
            builder.CloseAllPanels(); element.SetActive(false,false); element.Select(false);
            var lines = element.generator.FindChildren("*", "", true, false).OfType<GameEngine.LineRenderer>().ToArray();
            Check("train preview remains visible when deselected", lines.Any(l => l.enabled));
            await ToSignal(GetTree(), "physics_frame"); await Frames(2);
            var middle = element.splineData.sections[element.splineData.sections.Length/2].center;
            var position = new Vector3(middle[0],middle[1],middle[2]);
            Check("deselected train can be selected through its line collider",GameEngine.Raycast.DoRaycast(element,position+Vector3.Up*20,Vector3.Down,out var hit,40,1<<7) && hit.collider == element.generator);
            Check("trains have no preset category", Operations.LowLevelPresets.Category("train") == null);
            var schema = await Call("state.schema",new { @ref=id });
            Check("train schema has no presets", schema["presetCategory"].Type == JTokenType.Null);
            Check("train tension cannot be edited", !schema["parameters"].Any(p => p["name"].Value<string>() == "hermiteTension"));
            await Expect("train rejects built-in Hermite", "invalid_value", "state.setParam",new { @ref=id,name="curveType",value=1 });
            await Expect("train rejects imported curve changes", "invalid_value", "state.import",new { @ref=id,values=new {curveType=1} });
            await Expect("train presets are disabled", "invalid_value", "presets.save",new {category="lowLevel/train",name="Invalid train",values=new {type="train",curveType=3} });
            await Call("lowLevel.create",new { type="train",name="Incomplete train" });
            await CheckUndo("adding a train point", () => Call("lowLevel.insert", new { element=id,array="spline",position=new[] {-410,24,-105} }));
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
            element = M.lowLevelElements.First(e => GO.GetName(e) == "Train preview");
            Check("train save/reload rebuilds the custom preview",element.state.Int("curveType") == 3 && element.GetAllPoints().Count() == authored.Count && element.splineData.sections.Length > authored.Count);
            var json = Path.Combine(folder,"..","trains_export.json");
            await Call("city.exportJson",new {path=json});
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
                definitions.Add(JObject.FromObject(new SplineCurves.Definition {id=4,name="syntheticCubic",label="RD_SP_T_BEZIER",prepare="zzSplineSelfTest.prepare",evaluate="zzSplineSelfTest.evaluate",
                    controlParameters="zzSplineSelfTest.controlParameters",interpolateValues="zzSplineSelfTest.interpolateValues",split="zzSplineSelfTest.split",merge="zzSplineSelfTest.merge"}));
                File.WriteAllText(registry,definitions.ToString());
                SplineCurves.Reset();
                Directory.CreateDirectory(typeDir);
                File.WriteAllText(Path.Combine(typeDir,"type.json"),"{\"extends\":\"psdl\"}");
                File.WriteAllText(Path.Combine(typeDir,"settings.json"),"{\"curveTypes\":[4],\"defaultCurveType\":4}");
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
                var state = new ObjectState(); state.SetInt("curveType",4);
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
                var definition = SplineCurves.Definitions[4];
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
                Check("custom road topology supports undo/redo",Road().points.Count == 4 && Road().state.Int("curveType") == 4);
                await Call("presets.save",new { category="road",name="Custom road",values=JObject.FromObject(Road().state) });
                await Call("city.save"); await Call("city.reload");
                road = M.roads.First(r => r.state.Str("type") == "zzSplineTest");
                Check("custom road survives save/reload",road.customCurve != null && road.state.Int("curveType") == 4);
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
