using System.Collections.Generic;
using Godot;
using GO = GameEngine.GameObject;
using TP = CityElements.Types.Parsers.TypeParser;

namespace Api {
    //Creating and editing the elements, point by point as with the mouse tools of the editor
    public partial class CityApi {
        // Roads

        Operations.RoadDrawing GetDrawing(Road road) {
            if (!drawings.TryGetValue(road, out var drawing)) {
                //the drawings of the deleted roads are dropped
                var deleted = new List<Road>();
                foreach (var r in drawings.Keys) if (GO.IsNull(r) || r.deleted) deleted.Add(r);
                foreach (var r in deleted) drawings.Remove(r);
                drawing = new Operations.RoadDrawing(M);
                drawing.SetRoad(road);
                drawings[road] = drawing;
            }
            return drawing;
        }

        //a click of the road tool in add mode, then an update of the city as between two clicks
        void AddRoadPoint(Operations.RoadDrawing drawing, PointTarget t) {
            if (t == null) throw ApiException.InvalidParam("missing point");
            var road = drawing.GetRoad();
            if (road.endIntersection != null) throw ApiException.InvalidOperation("the road already ends at an intersection");
            if (t.FormCount() != 1) throw ApiException.InvalidParam("a road point needs exactly one of position, roadEnd, intersection, roadMiddle");
            if (t.roadEnd != null) {
                var other = Resolve<Road>(t.roadEnd.road, "road");
                if (other == road) throw ApiException.InvalidOperation("a road can't be connected to itself");
                if (t.roadEnd.end != "start" && t.roadEnd.end != "end") throw ApiException.InvalidValue("end must be 'start' or 'end'");
                drawing.ConnectToHandle(t.roadEnd.end == "start" ? other.points[0] : other.points[other.points.Count - 1]);
            } else if (t.intersection != null) {
                var intersection = Resolve<Intersection>(t.intersection, "intersection");
                drawing.ConnectToHandle(intersection.point);
            } else if (t.roadMiddle != null) {
                var other = Resolve<Road>(t.roadMiddle.road, "road");
                if (other == road) throw ApiException.InvalidOperation("a road can't be connected to its own middle");
                var res = other.splitLine.Resolve(t.roadMiddle.near);
                if (!res.HasValue) throw ApiException.InvalidOperation("the road has no free part to connect to");
                drawing.ConnectToMiddle(other, res.Value.point);
            } else if (t.position.HasValue) {
                drawing.AddPoint(t.onGround ? GeometryHelper.ProjectPoint(t.position.Value) : t.position.Value);
            } else {
                throw ApiException.InvalidParam("terrain points, anchors and links can't be road points");
            }
            M.worldChanged = true;
            M.ProcessUpdate();
        }

        //an element left incomplete (as only while being created in the editor) is deleted, like when its editor is closed
        bool DeleteIfIncomplete(object element) {
            if (!History.UndoManager.IsIncomplete(element)) return false;
            switch (element) {
                case Road r: Operations.Roads.Delete(r); break;
                case TerrainPatch p: Operations.TerrainPatches.Delete(p); break;
                case BuildingLine l: Operations.BuildingLines.Delete(l); break;
            }
            M.FlagAsChanged();
            return true;
        }

        object Result(object element, bool deleted) {
            return new { id = IdOf(element), deleted };
        }

        [ApiMethod("roads.create", "Creates a road through the points (in order, as clicked with the road tool). The first point can be a road end, an intersection or the middle of a road, and so can the last one")]
        public int CreateRoad(List<PointTarget> points, string preset = null, string type = null) {
            if (points == null || points.Count < 2) throw ApiException.InvalidParam("a road needs at least two points");
            return ApplyChange("create road", () => {
                var drawing = new Operations.RoadDrawing(M);
                var road = drawing.GetRoad();
                drawings[road] = drawing;
                if (preset != null) {
                    var p = Operations.Presets.Get("road", preset);
                    if (p == null) throw ApiException.NotFound("road preset '" + preset + "'");
                    road.SetState(p);
                }
                Operations.States.EnsureType(road.state, TP.GetRoadTypes());
                if (type != null) {
                    if (!TP.GetRoadTypes().ContainsKey(type)) throw ApiException.InvalidValue("unknown road type '" + type + "'");
                    Operations.States.SetType(road.state, type);
                }
                foreach (var point in points) AddRoadPoint(drawing, point);
                road = drawing.GetRoad(false);
                if (Operations.Roads.IsIncomplete(road)) throw ApiException.InvalidValue("the points give a road with less than two points");
                return IdOf(road);
            });
        }

        [ApiMethod("roads.extend", "Adds a point at the end of a road, as with the road tool in add mode (a road end, an intersection or the middle of a road connect the road there)")]
        public void ExtendRoad(ElementRef road, PointTarget point) {
            ApplyChange("add road point", () => {
                var r = Resolve<Road>(road, "road");
                History.UndoManager.Touch(r);
                AddRoadPoint(GetDrawing(r), point);
            });
        }

        [ApiMethod("roads.insertPoint", "Inserts a control point at the index, as with the road tool in insert mode")]
        public void InsertRoadPoint(ElementRef road, int index, Vector3 position, bool onGround = false) {
            ApplyChange("insert road point", () => {
                var r = Resolve<Road>(road, "road");
                if (index < 0 || index > r.points.Count) throw ApiException.InvalidValue("index out of range (0-" + r.points.Count + ")");
                History.UndoManager.Touch(r);
                GetDrawing(r).AddPoint(onGround ? GeometryHelper.ProjectPoint(position) : position, index);
            });
        }

        [ApiMethod("roads.movePoint", "Moves a control point of a road")]
        public void MoveRoadPoint(ElementRef road, int index, Vector3 position, bool onGround = false) {
            ApplyChange("move road point", () => {
                var r = Resolve<Road>(road, "road");
                if (index < 0 || index >= r.points.Count) throw ApiException.NotFound("point " + index);
                History.UndoManager.TouchOwner(r.points[index]);
                GO.SetPosition(r.points[index], onGround ? GeometryHelper.ProjectPoint(position) : position);
            });
        }

        [ApiMethod("roads.removePoint", "Removes a control point (the road must keep two), or the last one if no index is given (detaching the road from its end intersection, the road is deleted if it is left with one point)")]
        public object RemoveRoadPoint(ElementRef road, int? index = null) {
            return ApplyChange("remove road point", () => {
                var r = Resolve<Road>(road, "road");
                History.UndoManager.Touch(r);
                if (index.HasValue) {
                    if (index.Value < 0 || index.Value >= r.points.Count) throw ApiException.NotFound("point " + index.Value);
                    var point = r.points[index.Value];
                    if (!Operations.Roads.CanRemoveControlPoint(r, point)) throw ApiException.InvalidOperation("a road must keep at least two points");
                    Operations.Roads.RemoveControlPoint(r, point);
                } else {
                    Operations.Roads.RemoveLastPoint(r);
                }
                return Result(r, DeleteIfIncomplete(r));
            });
        }

        [ApiMethod("roads.delete", "Deletes a road (and the intersections left with no road)")]
        public void DeleteRoad(ElementRef road) {
            ApplyChange("delete road", () => {
                var r = Resolve<Road>(road, "road");
                History.UndoManager.Touch(r);
                Operations.Roads.Delete(r);
            });
        }

        [ApiMethod("roads.props.enable", "Enables or disables a props container of the prop rule of a road")]
        public void EnableRoadProps(ElementRef road, string container, bool enabled) {
            ApplyChange("enable props", () => {
                var r = Resolve<Road>(road, "road");
                if (System.Array.IndexOf(Operations.Roads.GetPropContainerNames(r), container) < 0) {
                    throw ApiException.NotFound("props container '" + container + "' (" + string.Join(", ", Operations.Roads.GetPropContainerNames(r)) + ")");
                }
                History.UndoManager.Touch(r);
                Operations.Roads.EnablePropContainer(r, container, enabled);
                //the container gets the first type, as when its editor is opened
                if (enabled) Operations.Props.EnsureContainerType(Operations.Roads.GetPropRule(r, false).State(container));
            });
        }

        [ApiMethod("roads.props.applyRulePreset", "Gives the prop rule of a road the values of a prop rule preset (of its road type)")]
        public void ApplyRoadPropRulePreset(ElementRef road, string preset) {
            ApplyChange("load preset", () => {
                var r = Resolve<Road>(road, "road");
                var p = Operations.Presets.Get("roadPropRule", preset);
                if (p == null) throw ApiException.NotFound("prop rule preset '" + preset + "'");
                if (!Operations.Presets.Filter("roadPropRule", new List<string> { r.state.Str("type") }).Exists(x => x.Name == preset)) {
                    throw ApiException.InvalidValue("the preset '" + preset + "' is for another road type");
                }
                History.UndoManager.Touch(r);
                Operations.Roads.SetPropRule(r, p);
            });
        }

        [ApiMethod("roads.props.saveRulePreset", "Saves the prop rule of a road as a preset")]
        public void SaveRoadPropRulePreset(ElementRef road, string name) {
            if (string.IsNullOrWhiteSpace(name)) throw ApiException.InvalidValue("the preset name can't be empty");
            ApplyChange("save preset", () => {
                var r = Resolve<Road>(road, "road");
                History.UndoManager.Touch(r);
                var rule = Operations.Roads.GetPropRule(r, true);
                rule.Name = name;
                Operations.Roads.SetPropRule(r, rule);
                PresetManager.SavePreset((ObjectState)rule.Clone(), "roadPropRule");
            });
        }

        // Intersections (created by connecting roads)

        [ApiMethod("intersections.move", "Moves an intersection (the roads follow it)")]
        public void MoveIntersection(ElementRef intersection, Vector3 position, bool onGround = false) {
            ApplyChange("move intersection", () => {
                var i = Resolve<Intersection>(intersection, "intersection");
                Operations.Intersections.Move(i, onGround ? GeometryHelper.ProjectPoint(position) : position);
            });
        }

        [ApiMethod("intersections.delete", "Deletes an intersection (the roads are detached from it)")]
        public void DeleteIntersection(ElementRef intersection) {
            ApplyChange("delete intersection", () => {
                var i = Resolve<Intersection>(intersection, "intersection");
                if (!Operations.Intersections.CanDelete(i)) throw ApiException.InvalidOperation("the intersection can't be deleted");
                History.UndoManager.Touch(i);
                Operations.Intersections.Delete(i);
            });
        }

        // Terrain patches

        //the element edited by a command, recorded for undo like the element of an editor panel
        TerrainPatch EditPatch(ElementRef r) {
            var p = Resolve<TerrainPatch>(r, "terrainPatch");
            History.UndoManager.Touch(p);
            return p;
        }

        BuildingLine EditLine(ElementRef r) {
            var l = Resolve<BuildingLine>(r, "buildingLine");
            History.UndoManager.Touch(l);
            return l;
        }

        MeshInstance EditMesh(ElementRef r) {
            var m = Resolve<MeshInstance>(r, "meshInstance");
            History.UndoManager.Touch(m);
            return m;
        }

        //returns the point added (the last one if several, null if none)
        TerrainPoint AddPatchPoint(TerrainPatch patch, PointTarget point, bool perimeter, bool followAnchors) {
            var (obj, pos) = ResolvePoint(point);
            TerrainPoint res = null;
            if (perimeter) {
                res = Operations.TerrainPatches.AddPerimeterPoint(patch, obj, pos, !followAnchors);
            } else {
                var before = patch.GetInternalPointsComponents().Count;
                Operations.TerrainPatches.AddInternalPoint(patch, obj, pos);
                var internals = patch.GetInternalPointsComponents();
                if (internals.Count > before) res = internals[internals.Count - 1];
            }
            M.worldChanged = true;
            M.ProcessUpdate();
            return res;
        }

        static int? PointId(TerrainPoint p) {
            return p == null ? (int?)null : History.ElementRegistry.GetId(p);
        }

        //the result of the creation of a patch or line with details: the point each target gave (the last one if it gave
        //several, null if none) and the anchors of the loop it was closed with
        object CreationDetails(object element, List<int?> points, List<TerrainAnchor> anchors) {
            List<object> anchorRefs = null;
            if (anchors != null) {
                anchorRefs = new List<object>();
                foreach (var a in anchors) anchorRefs.Add(AnchorRef(a));
            }
            return new { id = IdOf(element), points, anchors = anchorRefs };
        }

        [ApiMethod("patches.create", "Creates a terrain patch with the perimeter points (as clicked, followAnchors: follow the anchors between two anchors, like with the modifier key) and the internal points; autoClose: close the perimeter from its single anchor point. Returns the id, or with details {id, points: the point id given by each perimeter target, anchors: the anchors of the loop}")]
        public object CreatePatch(List<PointTarget> perimeter, List<PointTarget> internalPoints = null, string type = null, bool followAnchors = false, bool autoClose = false, bool details = false) {
            if (perimeter == null || perimeter.Count == 0) throw ApiException.InvalidParam("missing perimeter points");
            return ApplyChange("create terrain patch", () => {
                var patch = Operations.TerrainPatches.Create(M);
                if (type != null) {
                    if (!TP.GetTerrainPatchTypes().ContainsKey(type)) throw ApiException.InvalidValue("unknown terrain patch type '" + type + "'");
                    Operations.States.SetType(patch.state, type);
                }
                var points = new List<int?>();
                foreach (var p in perimeter) points.Add(PointId(AddPatchPoint(patch, p, true, followAnchors)));
                List<TerrainAnchor> anchors = null;
                if (autoClose) {
                    CheckAutoClose(Operations.TerrainPatches.AutoClose(patch, out anchors));
                    M.worldChanged = true;
                    M.ProcessUpdate();
                }
                if (internalPoints != null) foreach (var p in internalPoints) AddPatchPoint(patch, p, false, false);
                if (Operations.TerrainPatches.IsIncomplete(patch)) throw ApiException.InvalidValue("a terrain patch needs at least three perimeter points");
                return details ? CreationDetails(patch, points, anchors) : IdOf(patch);
            });
        }

        static void CheckAutoClose(Operations.AutoCloseResult res) {
            switch (res) {
                case Operations.AutoCloseResult.Closed: return;
                case Operations.AutoCloseResult.NoLoopFound: throw new ApiException("no_loop_found", "no loop found");
                case Operations.AutoCloseResult.NoPresets: throw new ApiException("autoclose_failed", "no building presets given");
                default: throw new ApiException("autoclose_failed", "the element must have exactly one point, on an anchor");
            }
        }

        [ApiMethod("patches.addPerimeterPoint", "Adds a point at the end of the perimeter of a terrain patch, returns the terrain point id (null if not added, e.g. already in it)")]
        public int? AddPatchPerimeterPoint(ElementRef patch, PointTarget point, bool followAnchors = false) {
            return ApplyChange("add perimeter point", () => {
                var p = EditPatch(patch);
                var (obj, pos) = ResolvePoint(point);
                var res = Operations.TerrainPatches.AddPerimeterPoint(p, obj, pos, !followAnchors);
                return res != null ? History.ElementRegistry.GetId(res) : (int?)null;
            });
        }

        [ApiMethod("patches.addInternalPoint", "Adds an internal point to a terrain patch (to add vertices inside it)")]
        public void AddPatchInternalPoint(ElementRef patch, PointTarget point) {
            ApplyChange("add internal point", () => {
                var p = EditPatch(patch);
                var (obj, pos) = ResolvePoint(point);
                Operations.TerrainPatches.AddInternalPoint(p, obj, pos);
            });
        }

        [ApiMethod("patches.removePoint", "Removes a perimeter or internal point from a terrain patch (the patch is deleted if left with less than three perimeter points)")]
        public object RemovePatchPoint(ElementRef patch, int point) {
            return ApplyChange("remove point", () => {
                var p = EditPatch(patch);
                if (!Operations.TerrainPatches.RemovePoint(p, ResolveTerrainPoint(point))) throw ApiException.InvalidOperation("the point is not part of the terrain patch");
                return Result(p, DeleteIfIncomplete(p));
            });
        }

        [ApiMethod("patches.removeLastPerimeterPoint", "Removes the last perimeter point (the patch is deleted if left with less than three)")]
        public object RemovePatchLastPoint(ElementRef patch) {
            return ApplyChange("remove point", () => {
                var p = EditPatch(patch);
                Operations.TerrainPatches.RemoveLastPerimeterPoint(p);
                return Result(p, DeleteIfIncomplete(p));
            });
        }

        static object AutoCloseAnchors(List<TerrainAnchor> anchors) {
            var res = new List<object>();
            foreach (var a in anchors) res.Add(AnchorRef(a));
            return new { anchors = res };
        }

        [ApiMethod("patches.autoClose", "Closes the perimeter following the anchors from its single point (on an anchor), returns the anchors of the loop")]
        public object AutoClosePatch(ElementRef patch) {
            return ApplyChange("auto close", () => {
                CheckAutoClose(Operations.TerrainPatches.AutoClose(EditPatch(patch), out var anchors));
                return AutoCloseAnchors(anchors);
            });
        }

        [ApiMethod("patches.generateInternalPoints", "Fills a terrain patch without internal points with internal points at the distance from each other and from the perimeter, returns their ids")]
        public List<int> GeneratePatchInternalPoints(ElementRef patch, float distance) {
            if (distance <= 0) throw ApiException.InvalidValue("distance must be positive");
            return ApplyChange("generate internal points", () => {
                var points = Operations.TerrainPatches.GenerateInternalPoints(EditPatch(patch), distance);
                if (points == null) throw ApiException.InvalidOperation("the terrain patch already has internal points");
                var res = new List<int>();
                foreach (var p in points) res.Add(History.ElementRegistry.GetId(p));
                return res;
            });
        }

        [ApiMethod("patches.borderMesh.add", "Adds a border mesh (with the default preset) to a terrain patch, returns its index")]
        public int AddBorderMesh(ElementRef patch) {
            return ApplyChange("add border mesh", () => {
                var p = EditPatch(patch);
                if (Operations.TerrainPatches.AddBorderMesh(p) == Operations.AddBorderMeshResult.LimitReached) {
                    throw new ApiException("limit_reached", "the terrain patch can't have more border meshes");
                }
                return p.GetBorderMeshCount() - 1;
            });
        }

        [ApiMethod("patches.borderMesh.remove", "Removes a border mesh from a terrain patch")]
        public void RemoveBorderMesh(ElementRef patch, int borderMesh) {
            ApplyChange("remove border mesh", () => {
                var p = EditPatch(patch);
                if (p.GetBorderMesh(borderMesh) == null) throw ApiException.NotFound("border mesh " + borderMesh);
                Operations.TerrainPatches.RemoveBorderMesh(p, borderMesh);
            });
        }

        [ApiMethod("patches.borderMesh.addPoint", "Adds a perimeter point to a border mesh (with the perimeter points between it and the segment of the border mesh)")]
        public void AddBorderMeshPoint(ElementRef patch, int borderMesh, int point) {
            ApplyChange("border mesh point", () => {
                var p = EditPatch(patch);
                if (p.GetBorderMesh(borderMesh) == null) throw ApiException.NotFound("border mesh " + borderMesh);
                var tp = ResolveTerrainPoint(point);
                if (!p.GetPerimeterPointsComponents().Contains(tp)) throw ApiException.InvalidOperation("the point is not on the perimeter of the terrain patch");
                Operations.TerrainPatches.AddPointToBorderMesh(p, borderMesh, tp);
            });
        }

        [ApiMethod("terrainPoints.move", "Moves a point of terrain patches and building lines, as with the gizmo: a point on a segment slides along it, a point on an anchor can't be moved. Returns the new position")]
        public Vector3 MoveTerrainPoint(int point, Vector3 position, bool onGround = false) {
            ApplyChange("move point", () => {
                var p = ResolveTerrainPoint(point);
                var pos = onGround ? GeometryHelper.ProjectPoint(position) : position;
                if (Operations.TerrainPoints.Move(p, pos) == Operations.TerrainPoints.MoveResult.OnAnchor) {
                    throw ApiException.InvalidOperation("the point is on an anchor, it follows it");
                }
                M.worldChanged = true;
            });
            //after the update (the points of elements projected on the ground follow it)
            return ResolveTerrainPoint(point).GetPoint();
        }

        [ApiMethod("patches.delete", "Deletes a terrain patch")]
        public void DeletePatch(ElementRef patch) {
            ApplyChange("delete terrain patch", () => Operations.TerrainPatches.Delete(EditPatch(patch)));
        }

        // Building lines

        List<ObjectState> GetBuildingPresets(List<string> names) {
            var res = new List<ObjectState>();
            if (names == null) return res;
            foreach (var name in names) {
                var p = Operations.Presets.Get("building", name);
                if (p == null) throw ApiException.NotFound("building preset '" + name + "'");
                res.Add(p);
            }
            return res;
        }

        public class AutoCloseSettings {
            public List<string> presets; //building presets, picked at random for the buildings
            public float minLength = 15;
            public float maxLength = 15;
            public bool subdivide = true;
        }

        [ApiMethod("lines.create", "Creates a building line through the points (dividing: the indices of the points splitting two buildings); autoClose: close it from its single point on a segment between two anchors (see anchorLink), splitting it into buildings with the presets. Returns the id, or with details {id, points: the point id given by each target, anchors: the anchors of the loop}")]
        public object CreateLine(List<PointTarget> points, string preset = null, string type = null, List<int> dividing = null, bool followAnchors = false, AutoCloseSettings autoClose = null, bool details = false) {
            if (points == null || points.Count == 0) throw ApiException.InvalidParam("missing points");
            return ApplyChange("create building line", () => {
                var line = Operations.BuildingLines.Create(M);
                if (preset != null) {
                    var p = Operations.Presets.Get("buildingLine", preset);
                    if (p == null) throw ApiException.NotFound("building line preset '" + preset + "'");
                    Operations.BuildingLines.SetLineState(line, p);
                }
                Operations.States.EnsureType(line.state, TP.GetBuildingLineTypes());
                if (type != null) {
                    if (!TP.GetBuildingLineTypes().ContainsKey(type)) throw ApiException.InvalidValue("unknown building type '" + type + "'");
                    Operations.States.SetType(line.state, type);
                }
                var ids = new List<int?>();
                for (int i = 0; i < points.Count; i++) {
                    var (obj, pos) = ResolvePoint(points[i]);
                    ids.Add(PointId(Operations.BuildingLines.AddPoint(line, obj, pos, !followAnchors, dividing != null && dividing.Contains(i))));
                    M.worldChanged = true;
                    M.ProcessUpdate();
                }
                List<TerrainAnchor> anchors = null;
                if (autoClose != null) {
                    CheckAutoClose(Operations.BuildingLines.AutoClose(line, autoClose.minLength, autoClose.maxLength, GetBuildingPresets(autoClose.presets), autoClose.subdivide, out anchors));
                }
                if (Operations.BuildingLines.IsIncomplete(line)) throw ApiException.InvalidValue("a building line needs at least two points");
                return details ? CreationDetails(line, ids, anchors) : IdOf(line);
            });
        }

        [ApiMethod("lines.addPoint", "Adds a point at the end of a building line, returns the terrain point id (null if not added)")]
        public int? AddLinePoint(ElementRef line, PointTarget point, bool dividing = false, bool followAnchors = false) {
            return ApplyChange("add building line point", () => {
                var l = EditLine(line);
                var (obj, pos) = ResolvePoint(point);
                var res = Operations.BuildingLines.AddPoint(l, obj, pos, !followAnchors, dividing);
                return res != null ? History.ElementRegistry.GetId(res) : (int?)null;
            });
        }

        [ApiMethod("lines.removePoint", "Removes a point from a building line (the line is deleted if left with one point)")]
        public object RemoveLinePoint(ElementRef line, int point) {
            return ApplyChange("remove point", () => {
                var l = EditLine(line);
                if (!Operations.BuildingLines.RemovePoint(l, ResolveTerrainPoint(point))) throw ApiException.InvalidOperation("the point is not part of the building line");
                return Result(l, DeleteIfIncomplete(l));
            });
        }

        [ApiMethod("lines.removeLastPoint", "Removes the last point of a building line (the line is deleted if left with one point)")]
        public object RemoveLineLastPoint(ElementRef line) {
            return ApplyChange("remove point", () => {
                var l = EditLine(line);
                Operations.BuildingLines.RemoveLastPoint(l);
                return Result(l, DeleteIfIncomplete(l));
            });
        }

        [ApiMethod("lines.toggleDividing", "Switches a point of a building line between dividing two buildings or not")]
        public bool ToggleDividing(ElementRef line, int point) {
            return ApplyChange("dividing point", () => {
                var l = EditLine(line);
                var p = ResolveTerrainPoint(point);
                if (!Operations.BuildingLines.ToggleDividing(l, p)) throw ApiException.InvalidOperation("the point is not part of the building line");
                return p.dividing;
            });
        }

        [ApiMethod("lines.autoClose", "Closes a building line following the anchors from its single point (on a segment between two anchors, see anchorLink), splitting it into buildings with the presets; returns the anchors of the loop")]
        public object AutoCloseLine(ElementRef line, List<string> presets, float minLength = 15, float maxLength = 15, bool subdivide = true) {
            return ApplyChange("auto close", () => {
                var l = EditLine(line);
                CheckAutoClose(Operations.BuildingLines.AutoClose(l, minLength, maxLength, GetBuildingPresets(presets), subdivide, out var anchors));
                return AutoCloseAnchors(anchors);
            });
        }

        [ApiMethod("lines.applyBuildingPreset", "Gives every building of a line the values of a building preset")]
        public void ApplyBuildingPresetToLine(ElementRef line, string preset) {
            ApplyChange("load preset", () => {
                var l = EditLine(line);
                var p = Operations.Presets.Get("building", preset);
                if (p == null) throw ApiException.NotFound("building preset '" + preset + "'");
                History.UndoManager.Touch(l);
                Operations.BuildingLines.SetAllBuildingsState(l, p);
            });
        }

        [ApiMethod("lines.delete", "Deletes a building line")]
        public void DeleteLine(ElementRef line) {
            ApplyChange("delete building line", () => Operations.BuildingLines.Delete(EditLine(line)));
        }

        // Buildings and their sides

        [ApiMethod("buildings.setEnabled", "Enables or disables a building of a line")]
        public void SetBuildingEnabled(ElementRef building, bool enabled) {
            ApplyChange("building enabled", () => {
                var b = Resolve<Building>(building, "building");
                History.UndoManager.Touch(b.line);
                b.enabled = enabled;
                b.state.FlagAsChanged();
                b.RefreshOutline();
            });
        }

        [ApiMethod("buildings.setAllSidesEqual", "With all sides equal, the other sides of a building use the front one")]
        public void SetAllSidesEqual(ElementRef building, bool equal) {
            ApplyChange("all sides equal", () => {
                var b = Resolve<Building>(building, "building");
                History.UndoManager.Touch(b.line);
                Operations.BuildingLines.SetAllSidesEqual(b, equal);
            });
        }

        BuildingSideGenerator ResolveSide(ElementRef side) {
            var s = Resolve<BuildingSideGenerator>(side, "buildingSide");
            History.UndoManager.Touch(s.building.line);
            return s;
        }

        [ApiMethod("sides.addFloor", "Adds an upper floor to a building side, returns its index")]
        public int AddFloor(ElementRef side) {
            return ApplyChange("add floor", () => {
                var s = ResolveSide(side);
                Operations.BuildingLines.AddUpperFloor(s);
                return s.state.Array<ObjectState>("upperFloors").Length - 1;
            });
        }

        [ApiMethod("sides.removeFloor", "Removes an upper floor of a building side")]
        public void RemoveFloor(ElementRef side, int floor) {
            ApplyChange("delete floor", () => {
                if (!Operations.BuildingLines.RemoveUpperFloor(ResolveSide(side), floor)) throw ApiException.NotFound("floor " + floor);
            });
        }

        [ApiMethod("sides.addSlice", "Adds a slice to a floor of a building side (floor -1: the ground floor), returns its index")]
        public int AddSlice(ElementRef side, int floor = -1) {
            return ApplyChange("add slice", () => {
                var s = ResolveSide(side);
                if (!Operations.BuildingLines.AddSlice(s, floor)) throw ApiException.NotFound("floor " + floor);
                var floorState = floor < 0 ? s.state.State("groundFloor") : s.state.Array<ObjectState>("upperFloors")[floor];
                return floorState.Array<ObjectState>("Slices").Length - 1;
            });
        }

        [ApiMethod("sides.removeSlice", "Removes a slice of a floor of a building side (floor -1: the ground floor)")]
        public void RemoveSlice(ElementRef side, int floor, int slice) {
            ApplyChange("delete slice", () => {
                if (!Operations.BuildingLines.RemoveSlice(ResolveSide(side), floor, slice)) throw ApiException.NotFound("slice " + slice + " of floor " + floor);
            });
        }

        [ApiMethod("sides.addBlock", "Adds a block (of the first block type) to the dictionary of a building side, returns its index")]
        public int AddBlock(ElementRef side) {
            return ApplyChange("add block", () => {
                var s = ResolveSide(side);
                Operations.BuildingLines.AddBlock(s);
                return s.state.Array<ObjectState>("blockDict").Length - 1;
            });
        }

        [ApiMethod("sides.removeBlock", "Removes a block from the dictionary of a building side")]
        public void RemoveBlock(ElementRef side, int block) {
            ApplyChange("delete block", () => {
                if (!Operations.BuildingLines.RemoveBlock(ResolveSide(side), block)) throw ApiException.NotFound("block " + block);
            });
        }

        // Mesh instances

        static ObjectState SettingsOrDefault(ObjectState settings) {
            return settings ?? new ObjectState();
        }

        [ApiMethod("meshes.create", "Places a mesh instance (onGround: on the layers the mesh instances with these settings are placed on)")]
        public int CreateMesh(string mesh, Vector3 position, ObjectState settings = null, Vector3? rotation = null, Vector3? scale = null, bool randomRotation = false, bool onGround = false) {
            if (string.IsNullOrEmpty(mesh)) throw ApiException.InvalidParam("missing mesh");
            return ApplyChange("create mesh", () => {
                settings = SettingsOrDefault(settings);
                var pos = onGround ? GeometryHelper.ProjectPoint(position, Operations.Meshes.GetPlacerLayerMask(settings)) : position;
                var m = Operations.Meshes.Create(M, mesh, pos, settings, randomRotation);
                if (rotation.HasValue) GO.SetRotation(m, rotation.Value);
                if (scale.HasValue) GO.SetLocalScale(m, scale.Value);
                m.Start();
                return IdOf(m);
            });
        }

        [ApiMethod("meshes.scatter", "Places count mesh instances at random points within the radius, on the ground (like the multiple placement of the mesh tool)")]
        public List<int> ScatterMeshes(string mesh, Vector3 center, float radius, int count, ObjectState settings = null, bool randomRotation = false) {
            if (string.IsNullOrEmpty(mesh)) throw ApiException.InvalidParam("missing mesh");
            if (count < 1 || radius < 0) throw ApiException.InvalidValue("count must be positive and radius not negative");
            return ApplyChange("create meshes", () => {
                var res = new List<int>();
                foreach (var m in Operations.Meshes.Scatter(M, mesh, center, radius, count, SettingsOrDefault(settings), randomRotation)) {
                    m.Start();
                    res.Add(IdOf(m));
                }
                return res;
            });
        }

        [ApiMethod("meshes.eraseInRadius", "Deletes the mesh instances on the layer of the settings within the radius (like the delete mode of the mesh tool), returns how many")]
        public int EraseMeshes(Vector3 center, float radius, ObjectState settings = null) {
            return ApplyChange("delete meshes", () => Operations.Meshes.EraseInRadius(M, center, radius, SettingsOrDefault(settings)));
        }

        [ApiMethod("meshes.setTransform", "Sets the position, rotation (euler angles in degrees, or quaternion) and scale of a mesh instance")]
        public void SetMeshTransform(ElementRef mesh, Vector3? position = null, Vector3? rotation = null, Quaternion? quaternion = null, Vector3? scale = null) {
            if (rotation.HasValue && quaternion.HasValue) throw ApiException.InvalidParam("give either rotation or quaternion");
            ApplyChange("transform", () => {
                var m = Resolve<MeshInstance>(mesh, "meshInstance");
                History.UndoManager.Touch(m);
                Operations.Meshes.SetTransform(m, position, quaternion, scale);
                if (rotation.HasValue) GO.SetRotation(m, rotation.Value);
            });
        }

        [ApiMethod("meshes.replaceMesh", "Replaces the mesh of a mesh instance, keeping its transform (and id)")]
        public int ReplaceMesh(ElementRef mesh, string newMesh) {
            if (string.IsNullOrEmpty(newMesh)) throw ApiException.InvalidParam("missing mesh");
            return ApplyChange("change mesh", () => {
                var m = EditMesh(mesh);
                var res = Operations.Meshes.ReplaceMesh(M, m, newMesh);
                res.Start();
                return IdOf(res);
            });
        }

        [ApiMethod("meshes.projectToGround", "Puts a mesh instance on the ground under it")]
        public void ProjectMeshToGround(ElementRef mesh) {
            ApplyChange("project to ground", () => {
                var m = Resolve<MeshInstance>(mesh, "meshInstance");
                History.UndoManager.Touch(m);
                Operations.Meshes.ProjectToGround(m);
            });
        }

        [ApiMethod("meshes.delete", "Deletes a mesh instance")]
        public void DeleteMesh(ElementRef mesh) {
            ApplyChange("delete mesh", () => Operations.Meshes.Delete(EditMesh(mesh)));
        }

        // Parametric objects

        MeshInstance ResolveObject(ElementRef obj) {
            var m = Resolve<MeshInstance>(obj, "parametricObject");
            History.UndoManager.TouchOwner(m);
            return m;
        }

        [ApiMethod("objects.get", "A parametric object: its mesh and its transform relative to its base")]
        public object GetObject(ElementRef @object) {
            var m = Resolve<MeshInstance>(@object, "parametricObject");
            var s = m.objectParameter.objectInstanceSettings;
            return new {
                mesh = m.meshPath,
                localPosition = GO.GetLocalPosition(m),
                localRotation = GO.GetLocalRotation(m),
                localScale = GO.GetLocalScale(m),
                position = GO.GetPosition(m),
                allowCustomModel = s.allowCustomModel,
                allowPosition = s.allowPosition,
                allowRotation = s.allowRotation,
                allowScale = s.allowScale,
                deletable = Operations.Meshes.CanDelete(m)
            };
        }

        static void CheckAxes(string what, bool[] allowed, Vector3 current, Vector3? value) {
            if (!value.HasValue || allowed == null) return;
            for (int i = 0; i < 3; i++) {
                if (i < allowed.Length && !allowed[i] && Mathf.Abs(current[i] - value.Value[i]) > 1e-4f) {
                    throw ApiException.InvalidValue("the " + what + " of this object can't change along the axis " + "xyz"[i]);
                }
            }
        }

        [ApiMethod("objects.setTransform", "Sets the transform of a parametric object relative to its base (rotation: euler angles in degrees), on the axes it allows")]
        public void SetObjectTransform(ElementRef @object, Vector3? localPosition = null, Vector3? localRotation = null, Vector3? localScale = null) {
            ApplyChange("transform", () => {
                var m = ResolveObject(@object);
                var s = m.objectParameter.objectInstanceSettings;
                CheckAxes("position", s.allowPosition, GO.GetLocalPosition(m), localPosition);
                CheckAxes("rotation", s.allowRotation, GO.GetLocalRotation(m), localRotation);
                CheckAxes("scale", s.allowScale, GO.GetLocalScale(m), localScale);
                Operations.Meshes.SetParametricTransform(m, localPosition, localRotation, localScale);
            });
        }

        [ApiMethod("objects.setMesh", "Replaces the mesh of a parametric object (if it allows a custom one)")]
        public void SetObjectMesh(ElementRef @object, string mesh) {
            ApplyChange("change mesh", () => {
                var m = ResolveObject(@object);
                if (!m.objectParameter.objectInstanceSettings.allowCustomModel) throw ApiException.InvalidOperation("this object has no custom mesh");
                Operations.Meshes.ReplaceMesh(M, m, mesh);
            });
        }

        [ApiMethod("objects.projectToGround", "Puts a parametric object on the ground under it")]
        public void ProjectObjectToGround(ElementRef @object) {
            ApplyChange("project to ground", () => {
                var m = ResolveObject(@object);
                Operations.Meshes.ProjectToGround(m);
                m.SyncParametricObjectState();
            });
        }

        [ApiMethod("objects.delete", "Deletes the array element of a parametric object (the other parametric objects are generated by their element and can't be deleted)")]
        public void DeleteObject(ElementRef @object) {
            ApplyChange("delete", () => {
                var m = ResolveObject(@object);
                if (!Operations.Meshes.Delete(m)) throw ApiException.InvalidOperation("this object can't be deleted");
                builder.NotifyVisibilityChange();
            });
        }

        // Props

        [ApiMethod("props.elements.add", "Adds a props element (the first preset of an allowed type) to a list of a props container, returns its index (-1 for the lists with a single element)")]
        public int AddPropsElement(ElementRef container, string list) {
            return ApplyChange("add prop", () => {
                var c = Resolve<PropContainerPart>(container, "propContainer");
                var param = Operations.Props.GetListParameter(c.state, list);
                if (param == null) throw ApiException.NotFound("props list '" + list + "'");
                History.UndoManager.Touch(c.road);
                int res;
                if (param.maxNumber == 1) {
                    Operations.Props.EnsureSingleElement(c.state, list);
                    res = -1;
                } else {
                    if (Operations.Props.IsListFull(c.state, list)) throw new ApiException("limit_reached", "'" + list + "' can have at most " + param.maxNumber + " elements");
                    res = Operations.Props.AddElement(c.state, list);
                }
                c.road.state.FlagAsChanged();
                return res;
            });
        }

        [ApiMethod("props.elements.remove", "Removes a props element from a list of a props container")]
        public void RemovePropsElement(ElementRef container, string list, int index) {
            ApplyChange("delete prop", () => {
                var c = Resolve<PropContainerPart>(container, "propContainer");
                History.UndoManager.Touch(c.road);
                if (!Operations.Props.RemoveElement(c.state, list, index)) throw ApiException.NotFound("props element " + index + " of '" + list + "'");
                c.road.state.FlagAsChanged();
            });
        }

        PropElementPart ResolvePropElement(ElementRef element) {
            var e = Resolve<PropElementPart>(element, "propElement");
            History.UndoManager.Touch(e.road);
            return e;
        }

        [ApiMethod("props.element.meshes.add", "Adds a mesh to the meshes of a props element")]
        public void AddPropsElementMesh(ElementRef element, string mesh) {
            ApplyChange("add mesh", () => {
                var e = ResolvePropElement(element);
                var type = TypeOf(TP.GetPropsElementTypes(), e.state.Str("type", null));
                var max = type?.typeData.maxMeshes ?? 0;
                if (max > 0 && (e.state.Array<string>("meshes")?.Length ?? 0) >= max) throw new ApiException("limit_reached", "the props element can have at most " + max + " meshes");
                Operations.Props.AddMesh(e.state, mesh);
                e.road.state.FlagAsChanged();
            });
        }

        [ApiMethod("props.element.meshes.set", "Replaces a mesh of a props element")]
        public void SetPropsElementMesh(ElementRef element, int index, string mesh) {
            ApplyChange("change mesh", () => {
                var e = ResolvePropElement(element);
                if (!Operations.Props.SetMesh(e.state, index, mesh)) throw ApiException.NotFound("mesh " + index);
                e.road.state.FlagAsChanged();
            });
        }

        [ApiMethod("props.element.meshes.remove", "Removes a mesh from a props element")]
        public void RemovePropsElementMesh(ElementRef element, int index) {
            ApplyChange("delete mesh", () => {
                var e = ResolvePropElement(element);
                if (!Operations.Props.RemoveMesh(e.state, index)) throw ApiException.NotFound("mesh " + index);
                e.road.state.FlagAsChanged();
            });
        }

        [ApiMethod("props.element.setMesh", "Sets the mesh of a props element with a single one")]
        public void SetPropsElementSingleMesh(ElementRef element, string mesh) {
            ApplyChange("change mesh", () => {
                var e = ResolvePropElement(element);
                Operations.Props.SetSingleMesh(e.state, mesh);
                e.road.state.FlagAsChanged();
            });
        }
    }
}
