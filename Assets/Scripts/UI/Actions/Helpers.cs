using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Actions {
    class Helpers {
        public static string PadInts(string input) {
            return System.Text.RegularExpressions.Regex.Replace(input, "[0-9]+", match => match.Value.PadLeft(10, '0'));
        }
        public static int GetLatestObjectNumber(GameObject container, string baseName) {
            int maxI = 1;
            for (int i = 0; i < container.transform.childCount; i++) {
                var obj = container.transform.GetChild(i).gameObject;
                if (obj.name.Substring(0, Mathf.Min(obj.name.Length, baseName.Length)) == baseName) {
                    var suffix = obj.name.Split(' ');
                    if (suffix.Length >= 2 && int.TryParse(suffix[1], out int num)) {
                        if (num + 1 > maxI) maxI = num + 1;
                    }
                }
            }
            return maxI;
        }

        public static (Vector3 pos, float size, Color color) GetAnchorInfo(GameObject obj, Vector3 point) {
            var skipping = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            var anchor = obj == null ? null : obj.GetComponent<TerrainAnchor>();
            if (anchor == null) { //check if on anchor
                var p = obj == null ? null : obj.GetComponent<TerrainPoint>();
                if (p != null && p.anchor is TerrainAnchor ta) {
                    anchor = ta;
                }
            }
            if (anchor != null) {
                return (anchor.gameObject.transform.position, 1.2f, skipping ? (Color.yellow + Color.red) * 0.5f : Color.yellow);
            } else {
                var oldPoint = obj == null ? null : obj.GetComponent<TerrainPoint>();
                if (oldPoint == null) {
                    var pointLink = obj == null ? null : obj.GetComponent<PointLink>();
                    if (pointLink != null) {
                        var pPrev = pointLink.transform.parent.gameObject;
                        var pNext = pointLink.next;
                        return (GeometryHelper.ClosestPoint(point, pPrev.transform.position, pNext.transform.position), 1.0f, Color.red + Color.blue);
                    }
                } else {
                    return (oldPoint.gameObject.transform.position, 1.2f, Color.yellow);
                }
            }
            return (point, 1.0f, Color.green);
        }

        public static void SetLayerRecursively(GameObject obj, int layer) {
            if (obj == null) return;
            obj.layer = layer;
            foreach (Transform child in obj.transform) {
                if (child != null) SetLayerRecursively(child.gameObject, layer);
            }
        }

        public static void OffsetChildren(GameObject obj, float yOffset) {
            foreach (Transform child in obj.transform) {
                child.gameObject.transform.position += Vector3.up * yOffset * child.gameObject.transform.lossyScale.y;
            }
        }
    }
}
