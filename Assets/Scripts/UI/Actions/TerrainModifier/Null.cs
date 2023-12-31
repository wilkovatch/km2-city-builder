using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace TerrainModifier {
    // Does nothing, used to hide circle on map, also allows to select objects for further editing
    public class Null : TerrainModifier {
        System.Action<RaycastHit> action;
        public bool groundOnly;

        public Null(CityGroundHelper helper, System.Action<RaycastHit> action) : base(helper, 0, 0) {
            this.action = action;
        }

        public override void Apply(List<RaycastHit?> hits) {
            ShowOnMap(hits);
            if (!Input.GetMouseButton(0)) return;
            if (!hits[0].HasValue) return;
            var hit = hits[0].Value;
            Select(hit);
        }

        public override void ShowOnMap(List<RaycastHit?> hits) {
            helper.SetCirclePosition(Vector3.zero, 0);
        }

        public override void Deform(TerrainData data, Vector3 pos, float delta) {
        }

        public override List<int> GetLayerMasks() {
            if (groundOnly) {
                return new List<int> { 1 << 7 };
            } else {
                if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)) {
                    return new List<int> { ~((1 << 3) | (1 << 11)) };
                } else {
                    return new List<int> { ~(1 << 3) };
                }
            }
        }

        public void Select(RaycastHit hit) {
            if (action != null) action.Invoke(hit);
        }
    }
}
