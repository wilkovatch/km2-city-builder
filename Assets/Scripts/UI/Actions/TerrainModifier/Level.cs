using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace TerrainModifier {
    public class Level : TerrainModifier {
        float height;
        float closestDistance;
        bool manualHeight;

        public Level(CityGroundHelper helper, float range, float intensity, bool singleStep, bool terrainHeightActive, float terrainHeight) : base(helper, range, intensity, singleStep) {
            manualHeight = terrainHeightActive;
            height = 0.5f + terrainHeight / (CityGroundHelper.maxHeight * 2); //the value to be inserted is the heightmap value (0-1, with default being 0.5), not an absolute value
        }

        public override void ShowOnMap(List<RaycastHit?> hits) {
            if (!hits[0].HasValue) {
                helper.SetCirclePosition(Vector3.zero, 0);
                return;
            }
            var hit = hits[0].Value;
            helper.SetCirclePosition(hit.point, range);
        }

        public override void Reset() {
            closestDistance = float.MaxValue;
        }

        public override void Scan(TerrainData data, Vector3 pos) {
            if (manualHeight) return;
            var heights = data.GetHeights(xBase, yBase, size, size);
            for (int x = 0; x < size; x++) {
                for (int y = 0; y < size; y++) {
                    var d = Vector3.Distance(new Vector3(x, 0, y), loopPos);
                    if (d < closestDistance) {
                        closestDistance = d;
                        height = heights[y, x];
                    }
                }
            }
        }

        public override void Deform(TerrainData data, Vector3 pos, float delta) {
            helper.elementManager.worldChanged = true;
            var heights = data.GetHeights(xBase, yBase, size, size);
            for (int x = 0; x < size; x++) {
                for (int y = 0; y < size; y++) {
                    var d = Vector3.Distance(new Vector3(x, 0, y), loopPos);
                    if (d < scaledRange) {
                        heights[y, x] = Mathf.Lerp(heights[y, x], height, Mathf.Clamp01(delta));
                    }
                }
            }
            data.SetHeightsDelayLOD(xBase, yBase, heights);
        }
    }
}
