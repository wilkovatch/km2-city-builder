using System.Collections.Generic;
using Godot;
using Newtonsoft.Json.Linq;
using GO = GameEngine.GameObject;
using GS = GameEngine.Scriptable;

namespace Api {
    //Reading the city
    public partial class CityApi {
        public static readonly string[] ElementKinds = { "road", "intersection", "terrainPatch", "buildingLine", "meshInstance", "terrainPoint", "cityProperties" };

        internal static int IdOf(object element) {
            return element is CityProperties ? History.ElementRegistry.CityPropertiesId : History.ElementRegistry.GetId(element);
        }

        static string TypeNameOf(object element) {
            switch (element) {
                case Road r: return r.state?.Str("type", null);
                case Intersection i: return i.state?.Str("type", null);
                case TerrainPatch p: return p.state?.Str("type", null);
                case BuildingLine l: return l.state?.Str("type", null);
                case MeshInstance m: return m.meshPath;
                default: return null;
            }
        }

        object Summary(object e) {
            return new { id = IdOf(e), kind = KindOf(e), name = GetName(e), type = TypeNameOf(e) };
        }

        [ApiMethod("city.info", "The city loaded: folder, core, terrain settings and element counts")]
        public object CityInfo() {
            if (!HasCity) return new { loaded = false };
            var core = PreferencesManager.Get("core", "");
            return new {
                loaded = true,
                folder = PreferencesManager.workingDirectory,
                core,
                coreVersion = PreferencesManager.Get("coreVersion", 0),
                terrainSize = CityGroundHelper.terrainSize,
                heightmapResolution = CityGroundHelper.heightmapResolution,
                maxHeight = CityGroundHelper.maxHeight,
                roads = CountAlive(M.roads),
                intersections = CountAlive(M.intersections),
                terrainPatches = CountAlive(M.patches),
                buildingLines = CountAlive(M.buildings),
                meshInstances = CountAlive(M.meshes),
                terrainPoints = M.GetTerrainPoints().Count
            };
        }

        static int CountAlive<T>(List<T> list) {
            var n = 0;
            foreach (var e in list) if (History.ElementSnapshot.IsAlive(e)) n++;
            return n;
        }

        [ApiMethod("elements.list", "The elements of the city (optionally of a kind, with a name or of a type): id, kind, name, type")]
        public List<object> ListElements(string kind = null, string name = null, string type = null) {
            RequireCity();
            if (kind != null && System.Array.IndexOf(ElementKinds, kind) < 0) throw ApiException.InvalidValue("unknown kind '" + kind + "' (" + string.Join(", ", ElementKinds) + ")");
            var res = new List<object>();
            var all = new List<object>(AllElements(M));
            if (kind == "terrainPoint") all = new List<object>(M.GetTerrainPoints());
            if (kind == "cityProperties" || kind == null) all.Insert(0, M.GetDummy<CityProperties>());
            foreach (var e in all) {
                if (!History.ElementSnapshot.IsAlive(e)) continue;
                if (kind != null && KindOf(e) != kind) continue;
                if (name != null && GetName(e) != name) continue;
                if (type != null && TypeNameOf(e) != type) continue;
                res.Add(Summary(e));
            }
            return res;
        }

        //an anchor as referenced by the commands: {of, index}
        static object AnchorRef(TerrainAnchor anchor) {
            var (owner, index) = Operations.Anchors.GetOwner(anchor);
            if (owner == null || index < 0) return null;
            return new { of = IdOf(owner), index };
        }

        static object AnchorInfo(TerrainAnchor anchor, int index, Dictionary<TerrainAnchor, List<Operations.Anchors.User>> users) {
            var usedBy = new List<object>();
            if (users.TryGetValue(anchor, out var list)) {
                foreach (var u in list) usedBy.Add(new { element = IdOf(u.element), point = History.ElementRegistry.GetId(u.point), segment = u.segment });
            }
            var (owner, _) = Operations.Anchors.GetOwner(anchor);
            return new {
                of = owner != null ? IdOf(owner) : (int?)null,
                index,
                position = anchor.GetPoint(),
                insideDirection = anchor.insideDirection,
                prev = GO.IsNull(anchor.prev) ? null : AnchorRef(anchor.prev),
                next = GO.IsNull(anchor.next) ? null : AnchorRef(anchor.next),
                usedBy
            };
        }

        List<object> AnchorsOf(TerrainAnchorLineManager anchorManager) {
            var users = Operations.Anchors.GetUsers(M);
            var res = new List<object>();
            var anchors = anchorManager.GetTerrainAnchors();
            for (int i = 0; i < anchors.Count; i++) {
                if (GO.IsNull(anchors[i]) || anchors[i].IsDeleted()) continue;
                res.Add(AnchorInfo(anchors[i], i, users));
            }
            return res;
        }

        [ApiMethod("anchors.list", "Every anchor of the city (in the order of the city-wide generators): owner, index, position, previous and next anchor, the points attached to it; unusedBy (terrainPatch or buildingLine): only the anchors no point of that kind is on")]
        public List<object> ListAnchors(string unusedBy = null) {
            RequireCity();
            if (unusedBy != null && unusedBy != "terrainPatch" && unusedBy != "buildingLine") throw ApiException.InvalidValue("unusedBy must be terrainPatch or buildingLine");
            var users = Operations.Anchors.GetUsers(M);
            var res = new List<object>();
            foreach (var anchor in Operations.Anchors.GetAll(M)) {
                if (anchor.IsDeleted()) continue;
                var (owner, index) = Operations.Anchors.GetOwner(anchor);
                if (owner == null || index < 0) continue;
                users.TryGetValue(anchor, out var list);
                if (unusedBy == "terrainPatch" && Operations.Anchors.IsUsedBy<TerrainPatch>(list)) continue;
                if (unusedBy == "buildingLine" && Operations.Anchors.IsUsedBy<BuildingLine>(list)) continue;
                res.Add(AnchorInfo(anchor, index, users));
            }
            return res;
        }

        [ApiMethod("anchors.loop", "The anchors from an anchor to another one (to: none for the loop around the block, ending with the anchor itself), along the shorter way (invert: the longer one), as followed when adding points between two anchors")]
        public List<object> AnchorLoop(PointTarget.AnchorTarget anchor, PointTarget.AnchorTarget to = null, bool invert = false) {
            RequireCity();
            var from = ResolveAnchor(anchor);
            var res = new List<object>();
            foreach (var a in Operations.Anchors.GetLoop(from, to != null ? ResolveAnchor(to) : null, invert)) {
                if (a != null) res.Add(AnchorRef(a));
            }
            return res;
        }

        [ApiMethod("anchors.segment", "The segment from an anchor to the next one along the same edge (where a point can be placed with anchorLink), null at the end of the edge")]
        public object AnchorSegment(PointTarget.AnchorTarget anchor) {
            RequireCity();
            var a = ResolveAnchor(anchor);
            if (Operations.Anchors.GetLink(a) == null) return null;
            var start = a.GetPoint();
            var end = a.next.GetPoint();
            return new { start, end, length = start.DistanceTo(end), next = AnchorRef(a.next) };
        }

        //anchor: the anchor the point is on; segment: the segment it is on (from an anchor to the next one, or between two
        //terrain points), with its position along it (0-1)
        static object PointInfo(TerrainPoint p) {
            object anchor = null, segment = null;
            if (!GO.IsNull(p.anchor) && !p.anchor.IsDeleted()) {
                if (p.anchor is TerrainAnchor ta) {
                    anchor = AnchorRef(ta);
                } else if (p.anchor is LineAnchor la) {
                    var startAnchor = GS.FindRecursive<TerrainAnchor>(la.start);
                    if (startAnchor != null) {
                        segment = new { anchor = AnchorRef(startAnchor), percent = la.percent };
                    } else {
                        var a = GS.FindRecursive<TerrainPoint>(la.start);
                        var b = GS.FindRecursive<TerrainPoint>(la.end);
                        if (a != null && b != null) segment = new { points = new[] { History.ElementRegistry.GetId(a), History.ElementRegistry.GetId(b) }, percent = la.percent };
                    }
                }
            }
            return new { id = History.ElementRegistry.GetId(p), position = p.GetPoint(), dividing = p.dividing, anchor, segment };
        }

        static List<object> PointsInfo(List<TerrainPoint> points) {
            var res = new List<object>();
            foreach (var p in points) res.Add(PointInfo(p));
            return res;
        }

        object Derived(object e) {
            switch (e) {
                case Road road: {
                    var points = new List<Vector3>();
                    foreach (var p in road.points) points.Add(GO.GetPosition(p));
                    float? width = null;
                    try { width = road.GetStandardFloat("totalWidth"); } catch (System.Exception) { }
                    return new {
                        points,
                        pointRotations = road.GetPointRotations(), //[yaw, roll] as set
                        effectivePointRotations = road.GetEffectiveRotations(), //as used by the geometry
                        curve = road.curvePoints,
                        startIntersection = road.startIntersection != null ? IdOf(road.startIntersection) : (int?)null,
                        endIntersection = road.endIntersection != null ? IdOf(road.endIntersection) : (int?)null,
                        //of the ends on the intersections relative to their points (the automatic ones included)
                        startConnectionHeight = road.startIntersection?.GetConnectionHeight(road.startIntersection.roads.IndexOf(road)),
                        endConnectionHeight = road.endIntersection?.GetConnectionHeight(road.endIntersection.roads.IndexOf(road)),
                        totalWidth = width,
                        anchors = AnchorsOf(road.anchorManager),
                        props = Operations.Roads.GetPropContainerNames(road),
                        parametricObjects = new List<string>(road.objectInstances.Keys)
                    };
                }
                case Intersection intersection: {
                    var roads = new List<int>();
                    foreach (var r in intersection.GetRoadsSortedClockwise()) roads.Add(IdOf(r));
                    return new {
                        center = GO.GetPosition(intersection.point),
                        roadsClockwise = roads,
                        sizes = intersection.sizes,
                        seamless = Operations.Intersections.IsSeamless(intersection),
                        seamlessActive = intersection.IsSeamless(), //only with two roads
                        anchors = AnchorsOf(intersection.anchorManager)
                    };
                }
                case TerrainPatch patch: {
                    var borderMeshes = new List<object>();
                    foreach (var bm in patch.GetTerrainBorderMeshes()) {
                        var seg = new List<int>();
                        foreach (var p in bm.segment) seg.Add(History.ElementRegistry.GetId(p));
                        borderMeshes.Add(new { type = bm.state?.Str("type", null), points = seg });
                    }
                    return new {
                        perimeter = PointsInfo(patch.GetPerimeterPointsComponents()),
                        internalPoints = PointsInfo(patch.GetInternalPointsComponents()),
                        borderMeshes
                    };
                }
                case BuildingLine line: {
                    var buildings = new List<object>();
                    foreach (var b in line.buildings) {
                        var sides = new List<string>();
                        foreach (var s in Operations.BuildingLines.SideNames) if (Operations.BuildingLines.GetSide(b, s) != null) sides.Add(s);
                        buildings.Add(new {
                            firstPoint = History.ElementRegistry.GetId(b.firstPoint),
                            lastPoint = History.ElementRegistry.GetId(b.lastPoint),
                            enabled = b.enabled,
                            sides
                        });
                    }
                    return new { points = PointsInfo(line.GetPointsComponents()), buildings };
                }
                case MeshInstance mesh: {
                    return new {
                        position = GO.GetPosition(mesh),
                        rotation = GO.GetRotation(mesh),
                        quaternion = GO.GetRotationQuaternion(mesh),
                        scale = GO.GetLocalScale(mesh),
                        mesh = mesh.meshPath,
                        layer = GO.GetLayer(mesh)
                    };
                }
                case TerrainPoint point: {
                    var used = new List<int>();
                    foreach (var p in M.patches) if (p.GetPerimeterPointsComponents().Contains(point) || p.GetInternalPointsComponents().Contains(point)) used.Add(IdOf(p));
                    foreach (var l in M.buildings) if (l.ContainsPoint(point)) used.Add(IdOf(l));
                    return new { position = point.GetPoint(), dividing = point.dividing, usedBy = used };
                }
                case CityProperties cp: {
                    return new { parametricObjects = new List<string>(cp.objectInstances.Keys) };
                }
                default:
                    return null;
            }
        }

        [ApiMethod("elements.get", "An element as stored in the saved city (ids as references); derived: also the data computed from it (curve, anchors, buildings...)")]
        public object GetElement(ElementRef @ref, bool derived = false) {
            var e = Resolve(@ref);
            var kind = History.ElementSnapshot.GetKind(e);
            var res = new JObject {
                ["id"] = IdOf(e),
                ["kind"] = KindOf(e)
            };
            if (kind.HasValue) {
                res["data"] = JToken.Parse(History.ElementSnapshot.Capture(e, kind.Value, IdOf(e)));
            } else {
                //parts of elements
                var t = GetStateTarget(e);
                res["data"] = Json.ToToken(new { state = t.state, instanceState = t.instanceState });
            }
            if (derived) {
                var d = Derived(e);
                if (d != null) res["derived"] = Json.ToToken(d);
            }
            return res;
        }

        static void CollectIds(JToken token, string key, HashSet<int> res) {
            if (token is JObject o) {
                foreach (var p in o.Properties()) {
                    if (p.Name == key && p.Value.Type == JTokenType.Integer) res.Add(p.Value.Value<int>());
                    else CollectIds(p.Value, key, res);
                }
            } else if (token is JArray a) {
                foreach (var t in a) CollectIds(t, key, res);
            }
        }

        [ApiMethod("elements.geometry", "The generated geometry of a road, intersection, terrain patch, building line (with its buildings), mesh instance or parametric object, as in the export JSON (base64 arrays), with the materials and meshes it references")]
        public object GetGeometry(ElementRef @ref) {
            var e = Resolve(@ref);
            using (var geometry = new CityEncoder.ExportGeometry(x => IdOf(x))) {
                object element;
                switch (e) {
                    case Road r: element = geometry.GetRoad(r, IdOf(r)); break;
                    case Intersection i: element = geometry.GetIntersection(i, IdOf(i)); break;
                    case TerrainPatch p: element = geometry.GetTerrainPatch(p, IdOf(p)); break;
                    case BuildingLine l: element = geometry.GetBuildingLine(l, IdOf(l)); break;
                    case MeshInstance m: element = geometry.GetMeshInstance(m, m.objectParameter == null ? IdOf(m) : -1); break;
                    default: throw ApiException.WrongKind("road, intersection, terrainPatch, buildingLine, meshInstance or parametricObject", KindOf(e));
                }
                //serialized as in the export file
                var token = JToken.Parse(Newtonsoft.Json.JsonConvert.SerializeObject(element, Newtonsoft.Json.Formatting.None));
                var materialIds = new HashSet<int>();
                var meshIds = new HashSet<int>();
                CollectIds(token, "materialId", materialIds);
                CollectIds(token, "meshId", meshIds);
                var materials = new JObject();
                foreach (var id in materialIds) {
                    if (id >= 0 && id < geometry.materials.Count) materials[id.ToString()] = JToken.Parse(Newtonsoft.Json.JsonConvert.SerializeObject(geometry.materials[id]));
                }
                var meshes = new JObject();
                foreach (var id in meshIds) {
                    if (id >= 0 && id < geometry.meshes.Count) meshes[id.ToString()] = JToken.Parse(Newtonsoft.Json.JsonConvert.SerializeObject(geometry.meshes[id]));
                }
                return new JObject { ["kind"] = KindOf(e), ["element"] = token, ["materials"] = materials, ["meshes"] = meshes };
            }
        }

        [ApiMethod("elements.runtimeValue", "The value of a definition of the type of a road (as computed for its last update)")]
        public object RuntimeValue(ElementRef @ref, string variable, string valueType = "float") {
            var e = Resolve(@ref);
            if (!(e is IObjectWithStateAndRuntimeType rt)) throw ApiException.WrongKind("road", KindOf(e));
            try {
                switch (valueType) {
                    case "float": return rt.GetRuntimeFloat(variable);
                    case "bool": return rt.GetRuntimeBool(variable);
                    case "vec2": return rt.GetRuntimeVec2(variable);
                    case "vec3": return rt.GetRuntimeVec3(variable);
                    default: throw ApiException.InvalidValue("valueType must be float, bool, vec2 or vec3");
                }
            } catch (KeyNotFoundException) {
                throw ApiException.NotFound("definition '" + variable + "'");
            }
        }

        [ApiMethod("terrain.height", "The height of the ground (terrain and generated meshes) under a point")]
        public float TerrainHeight(float x, float z) {
            RequireCity();
            return Operations.Terrain.GetHeight(new Vector3(x, 0, z));
        }

        [ApiMethod("terrain.info", "The terrain settings, and the heightmap grid (gridSize samples per side, pixelSize apart; the sample x, z is at x * pixelSize + origin, z * pixelSize + origin)")]
        public object TerrainInfo() {
            RequireCity();
            return new {
                size = CityGroundHelper.terrainSize,
                heightmapResolution = CityGroundHelper.heightmapResolution,
                maxHeight = CityGroundHelper.maxHeight,
                gridSize = Operations.Terrain.GetGridSize(builder.helper),
                pixelSize = Operations.Terrain.GetPixelSize(builder.helper),
                origin = -CityGroundHelper.terrainSize * 0.5f
            };
        }
    }
}
