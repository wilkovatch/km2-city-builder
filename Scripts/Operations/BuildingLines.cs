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
