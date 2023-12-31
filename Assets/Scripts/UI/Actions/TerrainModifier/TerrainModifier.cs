using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace TerrainModifier {
    public abstract class TerrainModifier : TerrainAction {
        protected CityGroundHelper helper;
        public float range = 3.0f;
        public float intensity = 1.0f;

        protected int xBase, yBase, size, scaledRange;
        protected Vector3 loopPos;

        public TerrainModifier(CityGroundHelper helper, float range, float intensity) {
            this.helper = helper;
            this.range = range;
            this.intensity = intensity;
        }

        public override void Apply(List<RaycastHit?> hits) {
            ShowOnMap(hits);
            if (!Input.GetMouseButton(0)) return;
            if (!hits[0].HasValue) return;
            var hit = hits[0].Value;
            Reset();

            var tData = helper.terrainObj.GetComponent<Terrain>().terrainData;
            var pixelSize = tData.size.x / tData.heightmapResolution;
            scaledRange = (int)Mathf.Ceil(range / pixelSize);
            size = 2 * scaledRange;
            var basePos = (hit.point + tData.size * 0.5f) / pixelSize - new Vector3(scaledRange, 0, scaledRange);
            xBase = (int)Mathf.Clamp(basePos.x, 0, tData.heightmapResolution - size);
            yBase = (int)Mathf.Clamp(basePos.z, 0, tData.heightmapResolution - size);
            int xBaseDiff = ((int)basePos.x) - xBase;
            int yBaseDiff = ((int)basePos.z) - yBase;
            loopPos = new Vector3(1, 0, 1) * scaledRange + new Vector3(xBaseDiff, 0, yBaseDiff);

            Scan(tData, hit.point);
            Deform(tData, hit.point, intensity * Time.deltaTime);
        }

        public virtual void Reset() {

        }

        public virtual void Scan(TerrainData data, Vector3 pos) {

        }

        public abstract void Deform(TerrainData data, Vector3 pos, float delta);
    }
}
