using System.Collections.Generic;
using Godot;
using Input = GameEngine.Input;
using KeyCommand = GameEngine.Input.KeyCommand;
using RaycastHit = GameEngine.Raycast.RaycastHit;
using GS = GameEngine.Scriptable;

namespace ElementPlacer {
    //A temporary click mode of the point transform panel. It keeps the selected point and the previous click mode.
    public class TerrainPointAttachmentPicker : TerrainAction {
        public enum Target {
            Anchor, //the clicked anchor or segment (or point, without generatedOnly)
            Line, //the line between the next two terrain points clicked
            Column //the vertical line of the terrain point clicked
        }

        readonly CityBuilderMenuBar builder;
        TerrainPoint point;
        TerrainAction previous;
        System.Action onEnd;
        Target target;
        bool generatedOnly;
        public System.Action onFrame; //every frame while picking (e.g. to show which end of a line to click)

        public TerrainPointAttachmentPicker(CityBuilderMenuBar builder) {
            this.builder = builder;
        }

        public bool IsActive => point != null && builder.terrainClick.modifier == this;
        public Target CurrentTarget => target;
        public TerrainPoint LineStart { get; private set; } //Line: the first end clicked

        //generatedOnly: only the anchors of the roads and intersections for everyone (see Operations.TerrainPoints.AttachmentFromClick)
        public void Start(TerrainPoint point, System.Action onEnd, Target target = Target.Anchor, bool generatedOnly = true) {
            Stop();
            this.point = point;
            this.onEnd = onEnd;
            this.target = target;
            this.generatedOnly = generatedOnly;
            previous = builder.terrainClick.modifier;
            builder.terrainClick.modifier = this;
        }

        public void Stop() {
            if (point == null) return;
            var end = onEnd;
            point = null;
            onEnd = null;
            onFrame = null;
            LineStart = null;
            if (builder.terrainClick.modifier == this) builder.terrainClick.modifier = previous;
            previous = null;
            end?.Invoke();
        }

        TerrainPoint ClickedPoint(Node3D obj) {
            var clicked = obj == null ? null : GS.FindRecursive<TerrainPoint>(obj);
            if (clicked == null || clicked.IsDeleted() || clicked == LineStart || Operations.TerrainPoints.DependsOn(clicked, point)) return null;
            return clicked;
        }

        bool Accepts(Node3D obj, Vector3 position) {
            if (target == Target.Anchor) return Operations.TerrainPoints.AttachmentFromClick(obj, position, generatedOnly, point) != null;
            return ClickedPoint(obj) != null;
        }

        //also used by the self-test, inside the same undo record as a click; true if the click was used (for a line, the
        //first end clicked too)
        public bool Pick(Node3D obj, Vector3 position) {
            if (!IsActive || point.IsDeleted()) return false;
            if (target == Target.Anchor) {
                if (!Operations.TerrainPoints.Attach(point, obj, position, generatedOnly)) return false;
            } else {
                var clicked = ClickedPoint(obj);
                if (clicked == null) return false;
                if (target == Target.Line && LineStart == null) {
                    LineStart = clicked;
                    builder.gizmo.ConsumeClick();
                    return true;
                }
                var ok = target == Target.Line ? Operations.CustomElements.AttachToLine(point, LineStart, clicked) : Operations.CustomElements.AttachToColumn(point, clicked);
                if (!ok) return false;
            }
            builder.gizmo.ConsumeClick();
            builder.NotifyChange();
            Stop();
            return true;
        }

        public override void Apply(List<RaycastHit?> hits) {
            if (!IsActive) return;
            onFrame?.Invoke();
            if (!IsActive) return;
            if (point.IsDeleted() || (LineStart != null && LineStart.IsDeleted())) {
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
                if (!hit.HasValue || !Accepts(hit.Value.collider as Node3D, hit.Value.point)) continue;
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
