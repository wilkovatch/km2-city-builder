using System.Collections.Generic;
using Godot;
using GS = GameEngine.Scriptable;

namespace Operations {
    //result of the automatic closing of terrain patches and building lines
    public enum AutoCloseResult {
        NoPresets = -2, //building lines only: no building presets to split the line with
        CannotClose = -1, //the element must have only its first point, on an anchor
        NoLoopFound = 0,
        Closed = 1
    }

    public enum AddBorderMeshResult {
        Added,
        LimitReached
    }

    public static class TerrainPatches {
        public static TerrainPatch Create(ElementManager manager) {
            var name = "Terrain " + Actions.Helpers.GetLatestObjectNumber(manager.GetTerrainContainer(), "Terrain ");
            var patch = GS.Create<TerrainPatch>(name);
            patch.Initialize(manager.GetTerrainPointContainer(), manager.GetTerrainContainer());
            manager.AddPatch(patch);
            return patch;
        }

        //a patch with less than three perimeter points is incomplete: it is deleted when its editor is closed
        public static bool IsIncomplete(TerrainPatch patch) {
            return patch != null && patch.GetPerimeterPointCount() < 3;
        }

        //obj is what was clicked: an anchor, an existing terrain point, a point link or nothing (the terrain).
        //pointOnly: false follows the anchors from the previous anchor to the clicked one
        public static TerrainPoint AddPerimeterPoint(TerrainPatch patch, Node3D obj, Vector3 point, bool pointOnly) {
            return patch.AddPerimeterPoint(obj, point, pointOnly);
        }

        public static void AddInternalPoint(TerrainPatch patch, Node3D obj, Vector3 point) {
            patch.AddInternalPoint(obj, point);
        }

        public static void AddPointToBorderMesh(TerrainPatch patch, int borderMesh, TerrainPoint point) {
            patch.AddPointToBorderMesh(borderMesh, point);
        }

        public static void RemoveLastPerimeterPoint(TerrainPatch patch) {
            patch.RemovePerimeterPoint();
        }

        //removes a perimeter or internal point from the patch (the point is deleted if no other element uses it)
        public static bool RemovePoint(TerrainPatch patch, TerrainPoint point) {
            return point.DeleteManual(patch);
        }

        public static AutoCloseResult AutoClose(TerrainPatch patch) {
            return AutoClose(patch, out _);
        }

        //anchors: the anchors of the loop the perimeter was closed with
        public static AutoCloseResult AutoClose(TerrainPatch patch, out List<TerrainAnchor> anchors) {
            var res = patch.AutoClose();
            anchors = res.points ?? new List<TerrainAnchor>();
            return (AutoCloseResult)res.resultCode;
        }

        //fills the patch with internal points at the distance from each other and from the perimeter (only if it has
        //none yet); returns the new points, null if the patch already has internal points
        public static List<TerrainPoint> GenerateInternalPoints(TerrainPatch patch, float distance) {
            if (patch.GetInternalPointsComponents().Count > 0) return null;
            History.UndoManager.Touch(patch);
            patch.AutoGenerateInternalPoints(distance);
            return patch.GetInternalPointsComponents();
        }

        public static bool CanAddBorderMesh(TerrainPatch patch) {
            var limit = patch.GetTerrainPatchType().typeData.settings.maxBorderMeshes;
            return limit <= 0 || patch.GetBorderMeshCount() < limit;
        }

        //the new border mesh gets the default preset
        public static AddBorderMeshResult AddBorderMesh(TerrainPatch patch) {
            if (!CanAddBorderMesh(patch)) return AddBorderMeshResult.LimitReached;
            var state = PresetManager.GetPreset("terrainPatchBorderMesh", 0);
            patch.AddBorderMesh(state, new ObjectState());
            return AddBorderMeshResult.Added;
        }

        public static void RemoveBorderMesh(TerrainPatch patch, int index) {
            patch.RemoveBorderMesh(index);
        }

        public static void Delete(TerrainPatch patch) {
            patch.Delete();
        }
    }
}
