using System.Collections.Generic;
using System.Linq;
using Godot;
using LowLevel;
using CityElements.Types;

//Standalone prop rules use the same point ownership, attachments and editor as low-level geometry. Their
//built-in type is independent of the core; only the prop containers and placement expressions come from it.
public partial class PropPath : LowLevelElement {
    public const string TypeId = "__propPath";

    public static CityElements.Types.Runtime.LowLevelType CreateType() {
        var section = new LowLevelPointNode {
            name = "sections", label = "PROP_PATH_SECTIONS",
            array = new LowLevelArraySettings { min = 2, defaultCount = 2, defaultStep = new float[] { 0, 0, 10 } },
            children = new[] {
                new LowLevelPointNode { name = "start", label = "PROP_PATH_START", defaultPosition = new float[] { -1, 0, 0 } },
                new LowLevelPointNode { name = "end", label = "PROP_PATH_END", defaultPosition = new float[] { 1, 0, 0 } },
                new LowLevelPointNode { name = "reference", label = "PROP_PATH_REFERENCE" }
            }
        };
        var data = new CityElements.Types.LowLevelType {
            settings = new LowLevelSettings {
                creatable = false, propsContainers = new[] { "main" },
                controlPoints = new[] { new LowLevelPointNode {
                    name = "ribbons", label = "PROP_PATH_RIBBONS",
                    array = new LowLevelArraySettings { min = 1, defaultCount = 1, editable = false },
                    children = new[] { new LowLevelPointNode {
                        name = "segments", label = "PROP_PATH_SEGMENTS",
                        array = new LowLevelArraySettings { min = 1, defaultCount = 1 }, children = new[] { section }
                    } }
                } }
            },
            parameters = new ParameterContainer { parameters = new[] {
                new Parameter { name = "useDistanceReference", label = "PROP_PATH_USE_REFERENCE", type = "bool" }
            } },
            ui = new CityElements.Types.Panel { label = "CREATE_PROP_RULE", menuWidth = 1.5f, tabs = new[] { new Tab {
                label = "", elements = new[] { "mainGroup", "propPathGroup", "useDistanceReference", "pointsGroup", "propsGroup" }
                    .Select(n => new TabElement { name = n, width = 1.5f }).ToArray()
            } } }
        };
        return new CityElements.Types.Runtime.LowLevelType(data, TypeId);
    }

    public string[] RibbonNames {
        get {
            var count = (points?.Get("ribbons") as PointList)?.items.Count ?? 1;
            var names = state.Array<string>("ribbonNames") ?? new string[0];
            return Enumerable.Range(0, count).Select(i => i < names.Length ? names[i] : i == 0 ? "main" : "ribbon" + i).ToArray();
        }
    }

    public List<GeometryPropLine> GetPropRibbons() {
        var res = new List<GeometryPropLine>();
        if (points?.Get("ribbons") is not PointList ribbons) return res;
        var names = RibbonNames;
        for (int i = 0; i < ribbons.items.Count; i++) {
            var segments = ((PointGroup)ribbons.items[i]).Get("segments") as PointList;
            for (int j = 0; j < segments.items.Count; j++) {
                var segment = (PointGroup)segments.items[j];
                var line = new GeometryPropLine { container = names[i] };
                var references = state.Array<string>("referenceSegments");
                var useReference = state.Bool("useDistanceReference") && (references == null || references.Length == 0 || references.Contains(i + "." + j));
                foreach (PointGroup s in ((PointList)segment.Get("sections")).items) {
                    line.startBound.Add(((PointLeaf)s.Get("start")).point.GetPoint());
                    line.endBound.Add(((PointLeaf)s.Get("end")).point.GetPoint());
                    if (useReference) line.distanceReference.Add(((PointLeaf)s.Get("reference")).point.GetPoint());
                }
                res.Add(line);
            }
        }
        return res;
    }

    public void SynchronizeRule() {
        var names = RibbonNames.ToList();
        var rule = state.State("propRule");
        if (rule == null) return;
        foreach (var item in rule.properties) {
            if (item.Value is not ObjectState || names.Contains(item.Key)) continue;
            if (string.IsNullOrWhiteSpace(item.Key) || item.Key.Contains('.')) continue;
            InsertArrayElement("ribbons", names.Count, false);
            names.Add(item.Key);
        }
        if (!names.SequenceEqual(RibbonNames)) state.SetArray("ribbonNames", names.ToArray());
    }

    public void RenameRibbon(int index, string name) {
        var names = RibbonNames;
        if (index < 0 || index >= names.Length || string.IsNullOrWhiteSpace(name) || name.Contains('.')
            || names.Where((n, i) => i != index).Contains(name)) throw new System.ArgumentException("invalid ribbon name");
        History.UndoManager.Touch(this);
        var rule = state.State("propRule");
        var value = rule?.State(names[index]);
        rule?.RemoveProperty(names[index]);
        if (value != null) rule.SetState(name, value);
        names[index] = name;
        state.SetArray("ribbonNames", names);
        state.FlagAsChanged();
    }

    public void RemoveRibbon(int index) {
        var names = RibbonNames.ToList();
        if (names.Count <= 1 || index < 0 || index >= names.Count) return;
        History.UndoManager.Touch(this);
        RemoveArrayElement("ribbons", index, false);
        state.State("propRule")?.RemoveProperty(names[index]);
        names.RemoveAt(index);
        state.SetArray("ribbonNames", names.ToArray());
        state.FlagAsChanged();
    }

    //Distance references belong to segments. Keep their indices aligned when either array is edited.
    void ShiftReferences(string arrayPath, int index, bool remove) {
        var references = state.Array<string>("referenceSegments");
        if (references == null || references.Length == 0) return;
        var path = arrayPath.Split('.');
        bool ribbons = arrayPath == "ribbons";
        if (!ribbons && (path.Length != 3 || path[0] != "ribbons" || path[2] != "segments")) return;
        var result = new List<string>();
        foreach (var reference in references) {
            var parts = reference.Split('.');
            if (parts.Length != 2 || !int.TryParse(parts[0], out var r) || !int.TryParse(parts[1], out var s)) continue;
            if (ribbons || r.ToString() == path[1]) {
                var value = ribbons ? r : s;
                if (remove && value == index) continue;
                if (value >= index) value += remove ? -1 : 1;
                if (ribbons) r = value;
                else s = value;
            }
            result.Add(r + "." + s);
        }
        state.SetArray("referenceSegments", result.ToArray());
        if (result.Count == 0) state.SetBool("useDistanceReference", false);
    }

    public override bool InsertArrayElement(string arrayPath, int index, bool userEdit = true) {
        if (!base.InsertArrayElement(arrayPath, index, userEdit)) return false;
        ShiftReferences(arrayPath, index, false);
        return true;
    }

    public override bool RemoveArrayElement(string arrayPath, int index, bool userEdit = true) {
        if (!base.RemoveArrayElement(arrayPath, index, userEdit)) return false;
        ShiftReferences(arrayPath, index, true);
        return true;
    }

    protected override void Rebuild() {
        SynchronizeRule();
        var geometry = new GeometryResult();
        geometry.propLines = GetPropRibbons();
        foreach (var ribbon in geometry.propLines) {
            geometry.lines.Add(new GeometryLine { points = ribbon.startBound, always = true, color = Colors.Cyan });
            geometry.lines.Add(new GeometryLine { points = ribbon.endBound, always = true, color = Colors.Cyan });
        }
        generator.Rebuild(geometry, state, RibbonNames);
    }
}
