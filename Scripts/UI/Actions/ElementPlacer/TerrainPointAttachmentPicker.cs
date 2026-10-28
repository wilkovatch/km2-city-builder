using System.Collections.Generic;
using Godot;
using Input = GameEngine.Input;
using KeyCommand = GameEngine.Input.KeyCommand;
using RaycastHit = GameEngine.Raycast.RaycastHit;

namespace ElementPlacer {
    //A temporary click mode of the point transform panel. It keeps the selected point and the previous click mode.
    public class TerrainPointAttachmentPicker : TerrainAction {
        readonly CityBuilderMenuBar builder;
        TerrainPoint point;
        TerrainAction previous;
        System.Action onEnd;

        public TerrainPointAttachmentPicker(CityBuilderMenuBar builder) {
            this.builder = builder;
        }

        public bool IsActive => point != null && builder.terrainClick.modifier == this;

        public void Start(TerrainPoint point, System.Action onEnd) {
            Stop();
            this.point = point;
            this.onEnd = onEnd;
            previous = builder.terrainClick.modifier;
            builder.terrainClick.modifier = this;
        }

        public void Stop() {
            if (point == null) return;
            var end = onEnd;
            point = null;
            onEnd = null;
            if (builder.terrainClick.modifier == this) builder.terrainClick.modifier = previous;
            previous = null;
            end?.Invoke();
        }

        //also used by the self-test, inside the same undo record as a click.
        public bool Pick(Node3D obj, Vector3 position) {
            if (!IsActive || point.IsDeleted() || !Operations.TerrainPoints.Attach(point, obj, position)) return false;
            builder.gizmo.ConsumeClick();
            builder.NotifyChange();
            Stop();
            return true;
        }

        public override void Apply(List<RaycastHit?> hits) {
            if (!IsActive) return;
            if (point.IsDeleted()) {
                Stop();
                return;
            }
            ShowOnMap(hits);
            if (!Input.GetInputStart(KeyCommand.Select)) return;
            builder.gizmo.ConsumeClick();
            foreach (var hit in hits) {
                if (hit.HasValue && Pick(hit.Value.collider as Node3D, hit.Value.point)) return;
            }
        }

        public override void ShowOnMap(List<RaycastHit?> hits) {
            foreach (var hit in hits) {
                if (!hit.HasValue) continue;
                var anchor = Operations.TerrainPoints.AttachmentFromClick(hit.Value.collider as Node3D, hit.Value.point, point: point);
                if (anchor == null) continue;
                var (position, size, color) = Actions.Helpers.GetAnchorInfo(hit.Value.collider as Node3D, hit.Value.point);
                ActionHandlerManager.ShowHandle(position, size, color);
                return;
            }
        }

        public override List<int> GetLayerMasks() {
            return new List<int> { 1 << 10, 1 << 12 };
        }
    }
}
