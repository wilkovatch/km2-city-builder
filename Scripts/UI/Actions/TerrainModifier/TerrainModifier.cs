using System.Collections;
using System.Collections.Generic;
using Godot;
using Input = GameEngine.Input;
using KeyCommand = GameEngine.Input.KeyCommand;
using RaycastHit = GameEngine.Raycast.RaycastHit;

namespace TerrainModifier {
    public abstract class TerrainModifier : TerrainAction {
        protected CityGroundHelper helper;
        public float range = 3.0f;
        public float intensity = 1.0f;
        public bool singleStep = false;

        protected int xBase, yBase, size, scaledRange;
        protected Vector3 loopPos;

        public TerrainModifier(CityGroundHelper helper, float range, float intensity, bool singleStep) {
            this.helper = helper;
            this.range = range;
            this.intensity = intensity;
            this.singleStep = singleStep;
        }

        public override void Apply(List<RaycastHit?> hits) {
            ShowOnMap(hits);
            if (singleStep) {
                if (!Input.GetInputStart(KeyCommand.Select)) return;
            } else {
                if (!Input.GetInputPressed(KeyCommand.Select)) return;
            }
            if (!hits[0].HasValue) return;
            var hit = hits[0].Value;
            Reset();

            var terrain = helper.terrainObj;
            var resolution = GameEngine.Terrain.GetHeightmapResolution(terrain);
            if (resolution == 0) return;
            var pixelSize = GameEngine.Terrain.GetPixelSize(terrain);
            var terrainSize = GameEngine.Terrain.GetSize(terrain);
            scaledRange = (int)Mathf.Ceil(range / pixelSize);
            size = 2 * scaledRange;
            var basePos = (hit.point + new Vector3(terrainSize, 0, terrainSize) * 0.5f) / pixelSize - new Vector3(scaledRange, 0, scaledRange);
            xBase = (int)Mathf.Clamp(basePos.X, 0, resolution - size);
            yBase = (int)Mathf.Clamp(basePos.Z, 0, resolution - size);
            int xBaseDiff = ((int)basePos.X) - xBase;
            int yBaseDiff = ((int)basePos.Z) - yBase;
            loopPos = new Vector3(1, 0, 1) * scaledRange + new Vector3(xBaseDiff, 0, yBaseDiff);

            Scan(terrain, hit.point);
            History.UndoManager.TouchHeights(xBase, yBase, size, size);
            Deform(terrain, hit.point, singleStep ? intensity : (intensity * (float)helper.GetProcessDeltaTime()));
        }

        public virtual void Reset() {

        }

        public virtual void Scan(Node3D terrain, Vector3 pos) {

        }

        public abstract void Deform(Node3D terrain, Vector3 pos, float delta);
    }
}
