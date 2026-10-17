using System.Collections;
using System.Collections.Generic;
using Godot;
using Scriptable = Godot.Node3D;
using PhysicsScriptable = Godot.StaticBody3D;
using GameObject = Godot.Node3D;
using PhysicsGameObject = Godot.StaticBody3D;
using GO = GameEngine.GameObject;
using GS = GameEngine.Scriptable;
using Input = GameEngine.Input;
using KeyCommand = GameEngine.Input.KeyCommand;

namespace Actions {
    class Helpers {
        public static string PadInts(string input) {
            return System.Text.RegularExpressions.Regex.Replace(input, "[0-9]+", match => match.Value.PadLeft(10, '0'));
        }
        public static int GetLatestObjectNumber(GameObject container, string baseName) {
            int maxI = 1;
            for (int i = 0; i < GO.GetChildCount(container); i++) {
                var obj = GO.GetChildI(container, i);
                var objName = GO.GetName(obj);
                if (objName.Substring(0, Mathf.Min(objName.Length, baseName.Length)) == baseName) {
                    var suffix = objName.Split(' ');
                    if (suffix.Length >= 2 && int.TryParse(suffix[1], out int num)) {
                        if (num + 1 > maxI) maxI = num + 1;
                    }
                }
            }
            return maxI;
        }

        public static (Vector3 pos, float size, Color color) GetAnchorInfo(GameObject obj, Vector3 point) {
            var skipping = Input.GetInputPressed(KeyCommand.Modifier2);
            var anchor = obj == null ? null : GS.FindRecursive<TerrainAnchor>(obj);
            if (anchor == null) { //check if on anchor
                var p = obj == null ? null : GS.FindRecursive<TerrainPoint>(obj);
                if (p != null && p.anchor is TerrainAnchor ta && !GO.IsNull(ta)) {
                    anchor = ta;
                }
            }
            if (anchor != null) {
                return (anchor.GetPoint(), 1.2f, skipping ? (GameEngine.Color.yellow + GameEngine.Color.red) * 0.5f : GameEngine.Color.yellow);
            } else {
                var oldPoint = obj == null ? null : GS.FindRecursive<TerrainPoint>(obj);
                if (oldPoint == null) {
                    var pointLink = obj == null ? null : GS.Cast<PointLink>(obj);
                    if (pointLink != null) {
                        var pPrev = GO.GetParent(pointLink);
                        var pNext = pointLink.next;
                        return (GeometryHelper.ClosestPoint(point, GO.GetPosition(pPrev), GO.GetPosition(pNext)), 1.0f, GameEngine.Color.red + GameEngine.Color.blue);
                    }
                } else {
                    return (oldPoint.GetPoint(), 1.2f, GameEngine.Color.yellow);
                }
            }
            return (point, 1.0f, GameEngine.Color.green);
        }

        public static void SetLayerRecursively(GameObject obj, int layer) {
            if (obj == null) return;
            if (obj is PhysicsGameObject pObj) GO.SetLayer(pObj, layer);
            else if (obj is GeometryInstance3D gObj) GO.SetLayer(gObj, layer);
            for (int i = 0; i < GO.GetChildCount(obj); i++) {
                var child = GO.GetChildI(obj, i);
                if (child != null) SetLayerRecursively(child, layer);
            }
        }

        public static void OffsetChildren(GameObject obj, float yOffset) {
            for (int i = 0; i < GO.GetChildCount(obj); i++) {
                var child = GO.GetChildI(obj, i);
                var newPos = GO.GetPosition(child) + GameEngine.Vector3.up * yOffset * GO.GetGlobalScale(child).Y;
                GO.SetPosition(child, newPos);
            }
        }
    }
}
