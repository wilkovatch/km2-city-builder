using System.Collections.Generic;
using Godot;
using GO = GameEngine.GameObject;
using GS = GameEngine.Scriptable;
using Parameter = CityElements.Types.Parameter;
using TP = CityElements.Types.Parsers.TypeParser;

namespace Api {
    //The parts of the elements that are not elements themselves
    public class BorderMeshPart {
        public TerrainPatch patch;
        public int index;
        public TerrainBorderMesh mesh;
    }

    public class BlockPart {
        public BuildingLine line;
        public BuildingSideGenerator side;
        public int index;
    }

    public class PropContainerPart {
        public IObjectWithState road;
        public string container;
        public ObjectState state;
    }

    public class PropElementPart {
        public IObjectWithState road;
        public string container;
        public string list;
        public int index; //-1: the single element of the list
        public ObjectState state;
    }

    //an element of an array parameter, or a custom element parameter (index null)
    public class ParameterElementPart {
        public StateTarget parent;
        public Parameter param;
        public int? index;
        public ObjectState state;
    }

    //What the generic state editing works on: the states of an element or of a part of one, its core parameters,
    //and how presets and types apply to it
    public class StateTarget {
        public string kind;
        public object target; //the element or part
        public object owner; //the element recorded for undo
        public ObjectState state;
        public ObjectState instanceState; //null if none
        public Parameter[] parameters = new Parameter[0];
        public string presetCategory; //null if presets don't apply
        public System.Action<ObjectState> applyPreset;
        public List<string> presetAllowedTypes; //the presets of other types are not allowed
        public System.Func<IEnumerable<string>> types; //null if the type can't be chosen
        public CityElements.Types.Runtime.RoadType roadType;
        public System.Action afterChange; //after any change of the states

        public Parameter FindParameter(string name) {
            foreach (var p in parameters) {
                if (p.fullName() == name) return p;
            }
            return null;
        }
    }

    public partial class CityApi {
        // Elements

        public static string KindOf(object element) {
            switch (element) {
                case CityProperties: return "cityProperties";
                case MeshInstance mesh: return mesh.objectParameter == null ? "meshInstance" : "parametricObject";
                case Road: return "road";
                case Intersection: return "intersection";
                case TerrainPoint: return "terrainPoint";
                case TerrainPatch: return "terrainPatch";
                case BuildingLine: return "buildingLine";
                case PropPath: return "propPath";
                case LowLevelElement: return "lowLevel";
                case Building: return "building";
                case BuildingSideGenerator: return "buildingSide";
                case BlockPart: return "buildingBlock";
                case BorderMeshPart: return "borderMesh";
                case PropContainerPart: return "propContainer";
                case PropElementPart: return "propElement";
                case ParameterElementPart p: return p.index.HasValue ? "arrayElement" : "customElement";
                default: return element?.GetType().Name ?? "null";
            }
        }

        public static string GetName(object element) {
            switch (element) {
                case CityProperties: return "City properties";
                case Intersection i: return GO.GetName(i.geo);
                case Node3D n: return GO.GetName(n);
                default: return null;
            }
        }

        static IEnumerable<object> AllElements(ElementManager m) {
            foreach (var e in m.roads) yield return e;
            foreach (var e in m.intersections) yield return e;
            foreach (var e in m.patches) yield return e;
            foreach (var e in m.buildings) yield return e;
            foreach (var e in m.meshes) yield return e;
            foreach (var e in m.lowLevelElements) yield return e;
        }

        //an element of the city (with a registry id)
        object ResolveElement(ElementRef r) {
            if (r == null) throw ApiException.InvalidParam("missing reference");
            RequireCity();
            if (r.id.HasValue) {
                if (r.id.Value == History.ElementRegistry.CityPropertiesId) return M.GetDummy<CityProperties>();
                var e = History.UndoManager.GetAliveElement(r.id.Value);
                if (e == null) throw ApiException.NotFound("element " + r.id.Value);
                if (r.kind != null && KindOf(e) != r.kind) throw ApiException.WrongKind(r.kind, KindOf(e));
                return e;
            }
            if (r.name != null) {
                object found = null;
                foreach (var e in AllElements(M)) {
                    if (!History.ElementSnapshot.IsAlive(e) || GetName(e) != r.name) continue;
                    if (r.kind != null && KindOf(e) != r.kind) continue;
                    if (found != null) throw ApiException.InvalidParam("more than one element is named '" + r.name + "', use the id or the kind");
                    found = e;
                }
                if (found == null) throw ApiException.NotFound("element '" + r.name + "'");
                return found;
            }
            throw ApiException.InvalidParam("invalid element reference " + r);
        }

        //any element or part of one
        public object Resolve(ElementRef r) {
            if (r == null) throw ApiException.InvalidParam("missing reference");
            if (r.line != null) {
                var line = Resolve<BuildingLine>(r.line, "buildingLine");
                if (!r.building.HasValue) throw ApiException.InvalidParam("missing building index");
                var i = r.building.Value;
                if (i < 0 || i >= line.buildings.Count) throw ApiException.NotFound("building " + i);
                var building = line.buildings[i];
                if (r.side == null) return building;
                if (System.Array.IndexOf(Operations.BuildingLines.SideNames, r.side) < 0) throw ApiException.InvalidValue("invalid side '" + r.side + "'");
                var side = Operations.BuildingLines.GetSide(building, r.side);
                if (side == null) throw ApiException.NotFound("side " + r.side + " of building " + i);
                if (!r.block.HasValue) return side;
                if (Operations.BuildingLines.GetBlock(side, r.block.Value) == null) throw ApiException.NotFound("block " + r.block.Value);
                return new BlockPart { line = line, side = side, index = r.block.Value };
            }
            if (r.patch != null) {
                var patch = Resolve<TerrainPatch>(r.patch, "terrainPatch");
                if (!r.borderMesh.HasValue) throw ApiException.InvalidParam("missing border mesh index");
                var bm = patch.GetBorderMesh(r.borderMesh.Value);
                if (bm == null) throw ApiException.NotFound("border mesh " + r.borderMesh.Value);
                return new BorderMeshPart { patch = patch, index = r.borderMesh.Value, mesh = bm };
            }
            if (r.road != null) {
                var road = Resolve<IObjectWithState>(r.road, null);
                if (r.container == null) throw ApiException.InvalidParam("missing props container");
                var containerState = Operations.Props.GetPropRule(road.GetState(), false)?.State(r.container);
                if (containerState == null) throw ApiException.NotFound("props container '" + r.container + "' (enable it first)");
                if (r.list == null) return new PropContainerPart { road = road, container = r.container, state = containerState };
                if (Operations.Props.GetListParameter(containerState, r.list) == null) throw ApiException.NotFound("props list '" + r.list + "'");
                ObjectState elem;
                if (r.index.HasValue) {
                    var arr = containerState.Array<ObjectState>(r.list);
                    if (arr == null || r.index.Value < 0 || r.index.Value >= arr.Length) throw ApiException.NotFound("props element " + r.index.Value);
                    elem = arr[r.index.Value];
                } else {
                    elem = containerState.State(r.list);
                    if (elem == null) throw ApiException.NotFound("props element of '" + r.list + "' (add it first)");
                }
                return new PropElementPart { road = road, container = r.container, list = r.list, index = r.index ?? -1, state = elem };
            }
            if (r.parent != null) {
                var parent = GetStateTarget(r.parent);
                if (r.param == null) throw ApiException.InvalidParam("missing param");
                var param = parent.FindParameter(r.param);
                if (param == null) throw ApiException.NotFound("parameter '" + r.param + "'");
                var owner = parent.state; //the editor keeps the arrays and custom elements in the state
                if (param.type == "array") {
                    if (!r.index.HasValue) throw ApiException.InvalidParam("missing index of the array element");
                    var arr = owner?.Array<ObjectState>(param.fullName());
                    if (arr == null || r.index.Value < 0 || r.index.Value >= arr.Length) throw ApiException.NotFound("element " + r.index.Value + " of '" + r.param + "'");
                    return new ParameterElementPart { parent = parent, param = param, index = r.index.Value, state = arr[r.index.Value] };
                } else if (param.type == "customElement") {
                    var sub = owner?.State(param.fullName());
                    if (sub == null) throw ApiException.NotFound("custom element '" + r.param + "' (create it with state.customElement.ensure)");
                    return new ParameterElementPart { parent = parent, param = param, state = sub };
                }
                throw ApiException.InvalidParam("'" + r.param + "' is not an array or custom element parameter");
            }
            if (r.owner != null) {
                var owner = Resolve(r.owner);
                if (!(owner is IParametricObjectContainer container)) throw ApiException.WrongKind("road or cityProperties", KindOf(owner));
                if (r.objectKey == null) throw ApiException.InvalidParam("missing object");
                var obj = Operations.Meshes.FindParametricObject(container, r.objectKey, r.path);
                if (obj == null) throw ApiException.NotFound("parametric object '" + r.objectKey + "'");
                return obj;
            }
            return ResolveElement(r);
        }

        public T Resolve<T>(ElementRef r, string kind) where T : class {
            var res = Resolve(r);
            if (res is T t && (kind == null || KindOf(res) == kind)) return t;
            throw ApiException.WrongKind(kind ?? typeof(T).Name, KindOf(res));
        }

        TerrainPoint ResolveTerrainPoint(int id) {
            var p = History.UndoManager.GetAliveElement(id) as TerrainPoint;
            if (p == null) throw ApiException.NotFound("terrain point " + id);
            return p;
        }

        // Point targets

        //the node the click of the editor would have hit and the point, for the terrain patches and the building lines
        (Node3D obj, Vector3 point) ResolvePoint(PointTarget t) {
            if (t == null) throw ApiException.InvalidParam("missing point");
            if (t.FormCount() != 1) throw ApiException.InvalidParam("a point needs exactly one of position, terrainPoint, anchor, link, anchorLink");
            if (t.anchorLink != null) {
                var anchor = ResolveAnchor(t.anchorLink);
                var link = Operations.Anchors.GetLink(anchor);
                if (link == null) throw ApiException.NotFound("segment after the anchor " + t.anchorLink.index + " (it is the last one of its edge)");
                var start = anchor.GetPoint();
                var end = anchor.next.GetPoint();
                //the point is placed on the segment, like a click on it
                var pos = t.position.HasValue ? start + (end - start) * GeometryHelper.ClosestPointFactor(t.position.Value, start, end) : (start + end) * 0.5f;
                return (link, pos);
            }
            if (t.terrainPoint.HasValue) {
                var p = ResolveTerrainPoint(t.terrainPoint.Value);
                return (p.GetObject(), p.GetPoint());
            }
            if (t.anchor != null) {
                var anchor = ResolveAnchor(t.anchor);
                return (anchor.GetObject(), anchor.GetPoint());
            }
            if (t.link != null) {
                if (t.link.Length != 2 || !t.position.HasValue) throw ApiException.InvalidParam("a link needs two terrain points and a position");
                var a = ResolveTerrainPoint(t.link[0]);
                var b = ResolveTerrainPoint(t.link[1]);
                PointLink found = null;
                foreach (var pair in new[] { (a, b), (b, a) }) {
                    foreach (var link in GS.FindMultipleRecursive<PointLink>(pair.Item1.GetObject())) {
                        if (link.next == pair.Item2.GetObject()) found = link;
                    }
                    if (found != null) break;
                }
                if (found == null) throw ApiException.NotFound("link between the terrain points " + t.link[0] + " and " + t.link[1]);
                return (found, t.position.Value);
            }
            if (t.position.HasValue) {
                var pos = t.onGround ? GeometryHelper.ProjectPoint(t.position.Value) : t.position.Value;
                return (null, pos);
            }
            throw ApiException.InvalidParam("this kind of point can only be used to connect roads");
        }

        TerrainAnchor ResolveAnchor(PointTarget.AnchorTarget a) {
            var owner = Resolve(a.owner);
            List<TerrainAnchor> anchors;
            if (owner is Road road) anchors = road.anchorManager.GetTerrainAnchors();
            else if (owner is Intersection intersection) anchors = intersection.anchorManager.GetTerrainAnchors();
            else throw ApiException.WrongKind("road or intersection", KindOf(owner));
            if (a.index < 0 || a.index >= anchors.Count) throw ApiException.NotFound("anchor " + a.index + " (the element has " + anchors.Count + ")");
            return anchors[a.index];
        }

        // State targets

        static Parameter[] Params(CityElements.Types.ParameterContainer pc) {
            return pc?.parameters ?? new Parameter[0];
        }

        static T TypeOf<T>(Dictionary<string, T> types, string type) where T : class {
            return type != null && types.TryGetValue(type, out var res) ? res : null;
        }

        public StateTarget GetStateTarget(ElementRef r) {
            return GetStateTarget(Resolve(r));
        }

        StateTarget GetStateTarget(object e) {
            var t = new StateTarget { kind = KindOf(e), target = e, owner = e };
            switch (e) {
                case Road road:
                    t.state = road.state;
                    t.instanceState = road.instanceState;
                    t.roadType = TypeOf(TP.GetRoadTypes(), road.state.Str("type", null));
                    t.parameters = Params(t.roadType?.parameterContainer);
                    t.presetCategory = "road";
                    t.applyPreset = x => road.SetState(x);
                    t.types = () => TP.GetRoadTypes().Keys;
                    break;
                case Intersection intersection:
                    t.state = intersection.state;
                    t.instanceState = intersection.instanceState;
                    t.parameters = Params(TypeOf(TP.GetIntersectionTypes(), intersection.state.Str("type", null))?.parameterContainer);
                    t.presetCategory = "intersection";
                    t.applyPreset = x => intersection.state = x;
                    t.types = () => TP.GetIntersectionTypes().Keys;
                    break;
                case LowLevelElement lowLevel:
                    t.state = lowLevel.state;
                    t.parameters = Params(lowLevel.GetLowLevelType()?.parameterContainer);
                    t.presetCategory = Operations.LowLevelPresets.Category(lowLevel.TypeName);
                    if (t.presetCategory != null) {
                        t.presetAllowedTypes = new List<string> { lowLevel.TypeName };
                        t.applyPreset = x => Operations.LowLevelPresets.Apply(lowLevel.state, x, lowLevel.TypeName);
                    }
                    break;
                case TerrainPatch patch:
                    t.state = patch.state;
                    t.parameters = Params(TypeOf(TP.GetTerrainPatchTypes(), patch.state.Str("type", null))?.parameterContainer);
                    t.types = () => TP.GetTerrainPatchTypes().Keys;
                    break;
                case BorderMeshPart bm:
                    t.owner = bm.patch;
                    t.state = bm.mesh.state;
                    t.instanceState = bm.mesh.instanceState;
                    t.parameters = Params(TypeOf(TP.GetTerrainPatchBorderMeshTypes(), bm.mesh.state.Str("type", null))?.parameterContainer);
                    t.presetCategory = "terrainPatchBorderMesh";
                    t.applyPreset = x => bm.mesh.state = x;
                    t.types = () => TP.GetTerrainPatchBorderMeshTypes().Keys;
                    break;
                case BuildingLine line:
                    t.state = line.state;
                    t.instanceState = line.instanceState;
                    t.parameters = Params(TypeOf(TP.GetBuildingLineTypes(), line.state.Str("type", null))?.parameterContainer);
                    t.presetCategory = "buildingLine";
                    t.applyPreset = x => Operations.BuildingLines.SetLineState(line, x);
                    t.types = () => TP.GetBuildingLineTypes().Keys;
                    break;
                case Building building:
                    t.owner = building.line;
                    t.state = building.State;
                    t.parameters = Params(TypeOf(TP.GetBuildingBuildingTypes(), building.line.state.Str("type", null))?.parameterContainer);
                    t.presetCategory = "building";
                    t.applyPreset = x => Operations.BuildingLines.SetBuildingState(building, x);
                    break;
                case BuildingSideGenerator side:
                    t.owner = side.building.line;
                    t.state = side.State;
                    t.parameters = Params(TypeOf(TP.GetBuildingSideTypes(), side.building.line.state.Str("type", null))?.parameterContainer);
                    t.presetCategory = "buildingSide";
                    t.applyPreset = x => Operations.BuildingLines.SetSideState(side, x);
                    break;
                case BlockPart block:
                    t.owner = block.line;
                    t.state = Operations.BuildingLines.GetBlock(block.side, block.index);
                    t.parameters = Params(TypeOf(TP.GetBuildingBlockTypes(), t.state.Str("type", null))?.parameterContainer);
                    t.presetCategory = "buildingBlock";
                    t.applyPreset = x => Operations.BuildingLines.SetBlockState(block.side, block.index, x);
                    t.types = () => TP.GetBuildingBlockTypes().Keys;
                    t.afterChange = () => block.side.State.FlagAsChanged();
                    break;
                case MeshInstance mesh:
                    t.owner = mesh.objectParameter == null ? mesh : History.UndoManager.ResolveOwner(mesh);
                    t.state = mesh.settings;
                    if (mesh.objectParameter == null) {
                        t.parameters = Params(TP.GetMeshInstanceSettings()?.typeData.parametersInfo);
                    } else {
                        var settingsType = TypeOf(TP.GetParametricMeshInstanceSettings(), mesh.objectParameter.objectInstanceSettings.type);
                        t.parameters = Params(settingsType?.typeData.parametersInfo);
                    }
                    break;
                case CityProperties cp:
                    t.state = cp.state;
                    t.parameters = Params(TypeOf(TP.GetCitySettings(), "city")?.parameterContainer);
                    break;
                case PropContainerPart pc:
                    t.owner = pc.road;
                    t.state = pc.state;
                    t.presetCategory = "propContainer";
                    t.applyPreset = x => Operations.Props.GetPropRule(pc.road.GetState(), true).SetState(pc.container, x);
                    t.types = () => TP.GetPropsContainersTypes().Keys;
                    t.afterChange = () => pc.road.GetState().FlagAsChanged();
                    break;
                case PropElementPart pe:
                    t.owner = pe.road;
                    t.state = pe.state;
                    t.parameters = Params(TypeOf(TP.GetPropsElementTypes(), pe.state.Str("type", null))?.typeData.parametersInfo);
                    var containerState = Operations.Props.GetPropRule(pe.road.GetState(), false).State(pe.container);
                    var allowed = Operations.Props.GetAllowedTypes(containerState, pe.list);
                    t.presetCategory = "propElem";
                    t.presetAllowedTypes = allowed;
                    t.applyPreset = x => pe.state.properties = x.properties;
                    t.types = () => allowed.Count > 0 ? allowed : new List<string>(TP.GetPropsElementTypes().Keys);
                    t.afterChange = () => pe.road.GetState().FlagAsChanged();
                    break;
                case ParameterElementPart pp:
                    t.owner = pp.parent.owner;
                    t.state = pp.state;
                    t.parameters = Operations.States.GetSubparams(pp.param) ?? new Parameter[0];
                    var parentAfter = pp.parent.afterChange;
                    var parentState = pp.parent.state;
                    t.afterChange = () => { parentState.FlagAsChanged(); parentAfter?.Invoke(); };
                    break;
                default:
                    throw ApiException.WrongKind("an element with a state", KindOf(e));
            }
            return t;
        }
    }
}
