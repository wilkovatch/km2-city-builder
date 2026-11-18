using System.Collections.Generic;
using Godot;
using GS = GameEngine.Scriptable;
using TP = CityElements.Types.Parsers.TypeParser;

namespace Operations {
    //Editing of polygonal buildings (a building on the closed outline through its points) and of the sides of their walls
    public static class PolygonalBuildings {
        public const string PresetCategory = "polygonalBuilding";

        public static PolygonalBuilding Create(ElementManager manager, string name = null) {
            name ??= "Polygonal building " + Actions.Helpers.GetLatestObjectNumber(manager.GetBuildingContainer(), "Polygonal building ");
            var building = GS.Create<PolygonalBuilding>(name, manager.GetBuildingContainer());
            building.Initialize(manager.GetTerrainPointContainer());
            manager.AddPolygonalBuilding(building);
            return building;
        }

        //whether any building type can be used for polygonal buildings
        public static bool IsAvailable() {
            return TP.GetPolygonalBuildingTypes().Count > 0;
        }

        //a building with less than three points is incomplete: it is deleted when its editor is closed
        public static bool IsIncomplete(PolygonalBuilding building) {
            return building != null && building.GetPointCount() < 3;
        }

        //obj is what was clicked: an anchor, an existing terrain point, a point link or nothing (the terrain).
        //pointOnly: false follows the anchors from the previous anchor to the clicked one
        public static TerrainPoint AddPoint(PolygonalBuilding building, Node3D obj, Vector3 point, bool pointOnly = true) {
            return building.AddPoint(obj, point, pointOnly);
        }

        //inserts a free point in the building, in the edge closest to the position (or before the point at the index);
        //null if the building has less than two points (or the index is out of range)
        public static TerrainPoint InsertPoint(PolygonalBuilding building, Vector3 position, int? index = null) {
            var at = index ?? building.ClosestInsertIndex(position);
            return at < 0 ? null : building.InsertPoint(at, position);
        }

        //a click on an edge of the building being edited (obj: what was clicked) inserts a free point there; null if the
        //click is not on one (it is then a point to add, see AddPoint)
        public static TerrainPoint InsertOnClickedSegment(PolygonalBuilding building, Node3D obj, Vector3 point) {
            var index = AddTerrainPointToSequence.ClickedSegmentInsertIndex(building.GetPointsComponents(), obj, ref point);
            return index < 0 ? null : building.InsertPoint(index, point);
        }

        public static void RemoveLastPoint(PolygonalBuilding building) {
            building.RemoveLastPoint();
        }

        //removes a point from the building (the point is deleted if no other element uses it)
        public static bool RemovePoint(PolygonalBuilding building, TerrainPoint point) {
            return point.DeleteManual(building);
        }

        public static void Delete(PolygonalBuilding building) {
            building.Delete();
        }

        public static void SetState(PolygonalBuilding building, ObjectState state) {
            History.UndoManager.Touch(building);
            building.state = (ObjectState)state.Clone();
            building.state.FlagAsChanged();
        }

        //one side state for every wall, or one each (each wall starts with a copy of the shared one)
        public static void SetIndividualSides(PolygonalBuilding building, bool individual) {
            if (building.IsIndividual() == individual) return;
            History.UndoManager.Touch(building);
            if (individual) building.FillWallStates();
            building.state.SetBool("individualSides", individual);
        }

        //the side of the wall starting at the point with the index (any wall, or the first one without an index, when
        //the walls share one side); null if there is none
        public static BuildingSideGenerator GetWallSide(PolygonalBuilding building, int? pointIndex) {
            if (building == null) return null;
            if (pointIndex.HasValue) {
                var points = building.GetPointsComponents();
                if (pointIndex.Value < 0 || pointIndex.Value >= points.Count) return null;
                return building.GetWallSide(points[pointIndex.Value]);
            }
            if (building.IsIndividual()) return null;
            return building.walls.Count > 0 ? building.walls[0].front : null;
        }

        //the index of the point the wall of the side starts at (-1 if it is not a wall of the building)
        public static int WallIndex(PolygonalBuilding building, BuildingSideGenerator side) {
            if (building == null || side == null || side.building == null || side.building.owner != building) return -1;
            return building.GetPointsComponents().IndexOf(side.building.firstPoint);
        }

        // Presets

        //the default preset: the building type of the default building line preset (or the first one with polygonal
        //buildings), with the default building side and the roof texture of the default building
        public static ObjectState DefaultPreset() {
            var res = new ObjectState() { Name = Presets.DefaultName };
            var types = TP.GetPolygonalBuildingTypes();
            string type = null;
            var lineType = PresetManager.GetPresets().GetList("buildingLine")[0].Str("type", null);
            if (lineType != null && types.ContainsKey(lineType)) type = lineType;
            if (type == null) {
                foreach (var key in types.Keys) {
                    type = key;
                    break;
                }
            }
            if (type != null) res.SetStr("type", type);
            res.SetFloat("height", 10.0f);
            res.SetStr("roofTex", PresetManager.GetPresets().GetList("building")[0].Str("topTexture"));
            res.SetBool("individualSides", false);
            var side = (ObjectState)PresetManager.GetPresets().GetList("buildingSide")[0].Clone();
            side.Name = "";
            if (type != null) side.SetStr("type", type);
            res.SetState("sideState", side);
            return res;
        }

        //the parts of the polygonal buildings linked to the preset (the buildings themselves, or the sides of their walls
        //and their blocks) are replaced with the result of change
        public static void ChangeLinked(ElementManager manager, string category, string name, System.Func<ObjectState, ObjectState> change,
            System.Func<ObjectState, string, string, System.Func<ObjectState, ObjectState>, ObjectState> changeSide) {

            foreach (var building in manager.polygonalBuildings) {
                if (building.IsDeleted()) continue;
                if (category == PresetCategory) {
                    if (building.state.Name == name) {
                        History.UndoManager.Touch(building);
                        building.state = change((ObjectState)building.state.Clone());
                        building.state.FlagAsChanged();
                    }
                    continue;
                }
                if (category != "buildingSide" && category != "buildingBlock") continue;
                var sharedSide = building.state.State("sideState", false);
                var newShared = sharedSide == null ? null : changeSide((ObjectState)sharedSide.Clone(), category, name, change);
                var points = building.GetPointsComponents();
                var wallStates = building.GetWallStatesArray();
                var changedWalls = false;
                for (int i = 0; i < wallStates.Length; i++) {
                    if (wallStates[i] == null) continue;
                    var newSide = changeSide((ObjectState)wallStates[i].Clone(), category, name, change);
                    if (newSide == null) continue;
                    wallStates[i] = newSide;
                    changedWalls = true;
                }
                if (newShared == null && !changedWalls) continue;
                History.UndoManager.Touch(building);
                var newState = (ObjectState)building.state.Clone();
                if (newShared != null) newState.SetState("sideState", newShared);
                building.SetStates(newState, wallStates);
            }
        }
    }
}
