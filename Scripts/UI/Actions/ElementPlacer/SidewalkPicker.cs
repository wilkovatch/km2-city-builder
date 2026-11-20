using System.Collections;
using System.Collections.Generic;
using Godot;
using Input = GameEngine.Input;
using KeyCommand = GameEngine.Input.KeyCommand;
using RaycastHit = GameEngine.Raycast.RaycastHit;

namespace ElementPlacer {
    //The click mode while an intersection is edited: a click on one of its sidewalks selects it in the panel (see
    //IntersectionGenerator.sidewalks); the clicks on the handle of its point and on the gizmo are left to the gizmo
    public class SidewalkPicker : TerrainAction {
        readonly System.Func<Intersection> getIntersection;
        readonly System.Action<int> onPick;

        public SidewalkPicker(System.Func<Intersection> getIntersection, System.Action<int> onPick) {
            this.getIntersection = getIntersection;
            this.onPick = onPick;
        }

        public override void ShowOnMap(List<RaycastHit?> hits) {
        }

        //the generated meshes, then the handles (the point of the intersection)
        public override List<int> GetLayerMasks() {
            return new List<int> { 1 << 7, 1 << 8 };
        }

        public override bool SnapsToGrid => false;

        public override void Apply(List<RaycastHit?> hits) {
            if (!Input.GetInputStart(KeyCommand.Select) || !hits[0].HasValue || hits[1].HasValue) return;
            var gizmo = CityBuilderMenuBar.staticGizmo;
            if (gizmo != null && (gizmo.isNearAxis || gizmo.isTransforming)) return;
            var intersection = getIntersection();
            if (intersection == null || intersection.IsDeleted() || intersection.generator == null) return;
            var hit = hits[0].Value;
            if (hit.collider != intersection.generator) return;
            var index = intersection.generator.FindSidewalk(hit.point);
            if (index < 0) return;
            GameEngine.Gizmo.ConsumeClick();
            onPick(index);
        }
    }
}
