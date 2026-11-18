using System.Collections.Generic;
using Godot;
using GS = GameEngine.Scriptable;

namespace Operations {
    //Editing of building lines, of their buildings and of the sides of the buildings
    public static class BuildingLines {
        public static readonly string[] SideNames = { "front", "left", "right", "back" };

        public static BuildingLine Create(ElementManager manager) {
            var name = "Building " + Actions.Helpers.GetLatestObjectNumber(manager.GetBuildingContainer(), "Building ");
            var line = GS.Create<BuildingLine>(name, manager.GetBuildingContainer());
            line.Initialize(manager.GetTerrainPointContainer());
            manager.AddBuildingLine(line);
            return line;
        }

        //a line with less than two points is incomplete: it is deleted when its editor is closed
        public static bool IsIncomplete(BuildingLine line) {
            return line != null && line.GetPointCount() < 2;
        }

        //obj is what was clicked: an anchor, an existing terrain point, a point link or nothing (the terrain).
        //pointOnly: false follows the anchors from the previous anchor to the clicked one
        public static TerrainPoint AddPoint(BuildingLine line, Node3D obj, Vector3 point, bool pointOnly, bool dividing) {
            var newPoint = line.AddPoint(obj, point, pointOnly);
            if (newPoint != null) newPoint.dividing = dividing;
            return newPoint;
        }

        public static void RemoveLastPoint(BuildingLine line) {
            line.RemoveLastLinePoint();
        }

        //removes a point from the line (the point is deleted if no other element uses it)
        public static bool RemovePoint(BuildingLine line, TerrainPoint point) {
            return point.DeleteManual(line);
        }

        //switches a point of the line between dividing two buildings or not
        public static bool ToggleDividing(BuildingLine line, TerrainPoint point) {
            if (line == null || point == null || !line.ContainsPoint(point)) return false;
            History.UndoManager.Touch(point);
            point.dividing = !point.dividing;
            return true;
        }

        //closes the line following the anchors, splitting it into buildings of random length with the given presets
        public static AutoCloseResult AutoClose(BuildingLine line, float minLength, float maxLength, List<ObjectState> presets, bool subdivide) {
            return AutoClose(line, minLength, maxLength, presets, subdivide, out _);
        }

        //anchors: the anchors of the loop the line was closed with
        public static AutoCloseResult AutoClose(BuildingLine line, float minLength, float maxLength, List<ObjectState> presets, bool subdivide, out List<TerrainAnchor> anchors) {
            anchors = new List<TerrainAnchor>();
            if (presets == null || presets.Count == 0) return AutoCloseResult.NoPresets;
            var res = line.AutoClose(minLength, maxLength, presets, subdivide);
            if (res.points != null) anchors = res.points;
            return (AutoCloseResult)res.resultCode;
        }

        public static void Delete(BuildingLine line) {
            line.Delete();
        }

        //inserts a free point in the line, in the segment closest to the position (or before the point at the index);
        //null if the line has less than two points (or the index is out of range)
        public static TerrainPoint InsertPoint(BuildingLine line, Vector3 position, bool dividing = false, int? index = null) {
            var at = index ?? line.ClosestInsertIndex(position);
            return at < 0 ? null : line.InsertPoint(at, position, dividing);
        }

        //a click on a segment of the line being edited (obj: what was clicked) inserts a free point there, giving the
        //segment a new corner; null if the click is not on one (it is then a point to add, see AddPoint)
        public static TerrainPoint InsertOnClickedSegment(BuildingLine line, Node3D obj, Vector3 point, bool dividing) {
            var index = AddTerrainPointToSequence.ClickedSegmentInsertIndex(line.GetPointsComponents(), obj, ref point);
            return index < 0 ? null : line.InsertPoint(index, point, dividing);
        }

        //null if the line can be split at the point, otherwise why not
        public static string GetSplitError(BuildingLine line, TerrainPoint point) {
            if (line == null || line.IsDeleted()) return "the building line was deleted";
            if (line.IsLoop()) return "a looping building line can't be split";
            var points = line.GetPointsComponents();
            var index = points.IndexOf(point);
            if (point == null || index < 0) return "the point is not part of the building line";
            if (index == 0 || index == points.Count - 1) return "the point must not be an end of the building line";
            return null;
        }

        //splits the line at one of its points: the line keeps the points up to it, a new line with the same properties
        //gets the ones from it. The point is shared by both lines (they stay joined when it moves), and the buildings
        //keep their properties (the two parts of a building cut by the split have the ones it had). Returns the new
        //line (null if it can't be split, see GetSplitError)
        public static BuildingLine Split(ElementManager manager, BuildingLine line, TerrainPoint point) {
            if (GetSplitError(line, point) != null) return null;
            var points = line.GetPointsComponents();
            var index = points.IndexOf(point);
            var kept = points.GetRange(0, index + 1);
            var moved = points.GetRange(index, points.Count - index);
            var keptStates = line.BuildingStatesFor(kept);
            var movedStates = line.BuildingStatesFor(moved);
            History.UndoManager.Touch(line);
            var newLine = Create(manager);
            newLine.state = (ObjectState)line.state.Clone();
            newLine.instanceState = (ObjectState)line.instanceState.Clone();
            newLine.stateForNewBuildings = (ObjectState)line.stateForNewBuildings.Clone();
            //the new line first: the points it gets always belong to a line
            newLine.SetPoints(moved, movedStates);
            line.SetPoints(kept, keptStates);
            manager.FlagAsChanged();
            return newLine;
        }

        //null if the two lines can be joined, otherwise why not
        public static string GetMergeError(BuildingLine line, BuildingLine other) {
            if (line == null || other == null || line.IsDeleted() || other.IsDeleted()) return "a building line was deleted";
            if (line == other) return "a building line can't be joined with itself";
            if (line.IsLoop() || other.IsLoop()) return "a looping building line can't be joined";
            if (line.GetPointCount() < 2 || other.GetPointCount() < 2) return "a building line has less than two points";
            if (line.state.Str("type", null) != other.state.Str("type", null)) return "the building lines are of different types";
            return null;
        }

        //joins the other line to the line, which keeps its properties: the ends of the two lines closest to each other
        //are joined (the points of the other line are reversed if needed), into one point if they are at the same
        //position (or are the same point), otherwise with a new building between them. The buildings keep their
        //properties, and the other line is deleted. False if they can't be joined (see GetMergeError)
        public static bool Merge(ElementManager manager, BuildingLine line, BuildingLine other) {
            if (GetMergeError(line, other) != null) return false;
            var a = line.GetPointsComponents();
            var b = other.GetPointsComponents();
            //the closest pair of ends: the other line goes after the line, or before it
            float Distance(TerrainPoint p, TerrainPoint q) => p.GetPoint().DistanceSquaredTo(q.GetPoint());
            var after = Mathf.Min(Distance(a[a.Count - 1], b[0]), Distance(a[a.Count - 1], b[b.Count - 1]));
            var before = Mathf.Min(Distance(a[0], b[0]), Distance(a[0], b[b.Count - 1]));
            var append = after <= before;
            var joint = append ? a[a.Count - 1] : a[0];
            //the other line starts from the joint when appended, and ends on it otherwise
            var startsAtJoint = Distance(joint, b[0]) <= Distance(joint, b[b.Count - 1]);
            if (startsAtJoint != append) b.Reverse();
            var aStates = line.BuildingStatesFor(a);
            var bStates = other.BuildingStatesFor(b);
            var bJoint = append ? b[0] : b[b.Count - 1];
            var samePoint = bJoint == joint || GeometryHelper.AreVectorsEqual(bJoint.GetPoint(), joint.GetPoint());
            History.UndoManager.Touch(line);
            History.UndoManager.Touch(other);
            History.UndoManager.Touch(joint);
            History.UndoManager.Touch(bJoint);
            joint.dividing = true; //the buildings of the two lines stay apart
            bJoint.dividing = true;
            if (samePoint) b.Remove(bJoint);
            var gap = new List<ObjectState>();
            if (!samePoint && aStates.Count > 0) gap.Add((ObjectState)aStates[append ? aStates.Count - 1 : 0].Clone());
            var points = new List<TerrainPoint>();
            var states = new List<ObjectState>();
            points.AddRange(append ? a : b);
            points.AddRange(append ? b : a);
            states.AddRange(append ? aStates : bStates);
            states.AddRange(gap);
            states.AddRange(append ? bStates : aStates);
            //before the other line is deleted: the points it gives already belong to the line
            line.SetPoints(points, states);
            other.Delete();
            manager.FlagAsChanged();
            return true;
        }

        public static void SetLineState(BuildingLine line, ObjectState state) {
            line.state = (ObjectState)state.Clone();
        }

        //gives every building of the line the state (e.g. a building preset)
        public static void SetAllBuildingsState(BuildingLine line, ObjectState state) {
            foreach (var b in line.buildings) {
                b.SetState(state);
            }
        }

        public static void SetBuildingState(Building building, ObjectState state) {
            building.State = (ObjectState)state.Clone();
        }

        //side: "front", "left", "right" or "back"
        public static BuildingSideGenerator GetSide(Building building, string side) {
            if (building == null) return null;
            switch (side) {
                case "front": return building.front;
                case "left": return building.left;
                case "right": return building.right;
                case "back": return building.back;
                default: return null;
            }
        }

        public static string GetSideName(BuildingSideGenerator side) {
            var building = side?.building;
            if (building == null) return null;
            if (building.front == side) return "front";
            if (building.left == side) return "left";
            if (building.right == side) return "right";
            if (building.back == side) return "back";
            return null;
        }

        //with all sides equal, the other sides use the front one
        public static void SetAllSidesEqual(Building building, bool equal) {
            var state = (ObjectState)building.State.Clone();
            state.SetBool("allSidesEqual", equal);
            if (equal) {
                state.State("frontState", false).FlagAsChanged();
                state.SetState("rightState", new ObjectState());
                state.SetState("leftState", new ObjectState());
                state.SetState("backState", new ObjectState());
            } else {
                state.SetState("rightState", (ObjectState)state.State("frontState").Clone());
                state.SetState("leftState", (ObjectState)state.State("frontState").Clone());
                state.SetState("backState", (ObjectState)state.State("frontState").Clone());
            }
            building.State = state;
        }

        public static void SetSideState(BuildingSideGenerator side, ObjectState state) {
            side.State = (ObjectState)state.Clone();
        }

        // Floors (the upper ones, the ground floor always exists)

        public static void AddUpperFloor(BuildingSideGenerator side) {
            var f = new ObjectState();
            f.SetArray("Slices", new ObjectState[0]);
            f.SetFloat("height", 1);
            var newFloors = new List<ObjectState>();
            var upperFloors = side.state.Array<ObjectState>("upperFloors");
            if (upperFloors != null) newFloors.AddRange(upperFloors);
            newFloors.Add(f);
            side.state.SetArray("upperFloors", newFloors.ToArray());
            side.SyncWithBuilding();
        }

        public static bool RemoveUpperFloor(BuildingSideGenerator side, int floor) {
            var upperFloors = side.state.Array<ObjectState>("upperFloors");
            if (upperFloors == null || floor < 0 || floor > upperFloors.Length - 1) return false;
            var newFloors = new List<ObjectState>(upperFloors);
            newFloors.RemoveAt(floor);
            side.state.SetArray("upperFloors", newFloors.ToArray());
            side.SyncWithBuilding();
            return true;
        }

        // Slices (floor -1 is the ground floor)

        static ObjectState GetFloorState(BuildingSideGenerator side, int floor) {
            if (floor < 0) return side.state.State("groundFloor");
            var upperFloors = side.state.Array<ObjectState>("upperFloors");
            if (upperFloors == null || floor >= upperFloors.Length) return null;
            return upperFloors[floor];
        }

        public static bool AddSlice(BuildingSideGenerator side, int floor) {
            var floorState = GetFloorState(side, floor);
            if (floorState == null) return false;
            var slices = floorState.Array<ObjectState>("Slices", false);
            var f = new ObjectState();
            f.SetInt("Repetitions", 1);
            f.SetFloat("Width", 1);
            if (floor < 0) {
                f.SetInt("top", 0);
                f.SetInt("middle", 0);
                f.SetInt("bottom", 0);
            } else {
                f.SetInt("block", 0);
            }
            var newSlices = new List<ObjectState>(slices);
            newSlices.Add(f);
            floorState.SetArray("Slices", newSlices.ToArray());
            side.SyncWithBuilding();
            return true;
        }

        public static bool RemoveSlice(BuildingSideGenerator side, int floor, int slice) {
            var floorState = GetFloorState(side, floor);
            if (floorState == null || slice < 0) return false;
            var slices = floorState.Array<ObjectState>("Slices", false);
            if (slice > slices.Length - 1) return false;
            var newSlices = new List<ObjectState>(slices);
            newSlices.RemoveAt(slice);
            floorState.SetArray("Slices", newSlices.ToArray());
            side.SyncWithBuilding();
            return true;
        }

        // Blocks (the dictionary of blocks used by the slices)

        //the new block gets the first block type
        public static void AddBlock(BuildingSideGenerator side) {
            var bTypes = CityElements.Types.Parsers.TypeParser.GetBuildingBlockTypes(false);
            var defType = "";
            foreach (var elem in bTypes) {
                defType = elem.Key;
                break;
            }
            var b = new ObjectState();
            b.SetStr("type", defType);
            var newBlocks = new List<ObjectState>();
            var blockDict = side.state.Array<ObjectState>("blockDict");
            if (blockDict != null) newBlocks.AddRange(blockDict);
            newBlocks.Add(b);
            side.state.SetArray("blockDict", newBlocks.ToArray());
            side.SyncWithBuilding();
        }

        public static bool RemoveBlock(BuildingSideGenerator side, int block) {
            var blockDict = side.state.Array<ObjectState>("blockDict");
            if (blockDict == null || block < 0 || block > blockDict.Length - 1) return false;
            var newBlocks = new List<ObjectState>(blockDict);
            newBlocks.RemoveAt(block);
            side.state.SetArray("blockDict", newBlocks.ToArray());
            side.SyncWithBuilding();
            return true;
        }

        public static ObjectState GetBlock(BuildingSideGenerator side, int block) {
            var blockDict = side.State.Array<ObjectState>("blockDict");
            if (blockDict == null || block < 0 || block >= blockDict.Length) return null;
            return blockDict[block];
        }

        public static void SetBlockState(BuildingSideGenerator side, int block, ObjectState state) {
            side.State.Array<ObjectState>("blockDict")[block].ReplaceWith(state);
            side.State.FlagAsChanged();
        }
    }
}
