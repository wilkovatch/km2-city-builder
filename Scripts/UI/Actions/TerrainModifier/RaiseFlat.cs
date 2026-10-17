using System.Collections.Generic;
using Godot;
using RaycastHit = GameEngine.Raycast.RaycastHit;

namespace TerrainModifier {
    public class RaiseFlat : TerrainModifier {

        public RaiseFlat(CityGroundHelper helper, float range, float intensity, bool singleStep) : base(helper, range, intensity, singleStep) {

        }

        public override void ShowOnMap(List<RaycastHit?> hits) {
            if (!hits[0].HasValue) {
                helper.SetCirclePosition(GameEngine.Vector3.zero, 0);
                return;
            }
            var hit = hits[0].Value;
            helper.SetCirclePosition(hit.point, range);
        }

        public override void Deform(Node3D terrain, Vector3 pos, float delta) {
            helper.elementManager.worldChanged = true;
            var heights = GameEngine.Terrain.GetHeights(terrain, xBase, yBase, size, size);
            for (int x = 0; x < size; x++) {
                for (int y = 0; y < size; y++) {
                    var d = GameEngine.Vector3.Distance(new Vector3(x, 0, y), loopPos);
                    if (d < scaledRange) {
                        heights[y, x] += delta / (CityGroundHelper.maxHeight * 2); //the value to be inserted is the heightmap value (0-1), not an absolute value
                    }
                }
            }
            GameEngine.Terrain.SetHeightsDelayLOD(terrain, xBase, yBase, heights);
        }
    }
}
