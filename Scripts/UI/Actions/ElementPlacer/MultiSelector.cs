using System.Collections;
using System.Collections.Generic;
using Godot;
using Input = GameEngine.Input;
using KeyCommand = GameEngine.Input.KeyCommand;
using RaycastHit = GameEngine.Raycast.RaycastHit;
using GO = GameEngine.GameObject;
using GS = GameEngine.Scriptable;
using SelectionOutline = RuntimeGizmos.SelectionOutline;

namespace ElementPlacer {
    //The multiple selection (Edit->Select multiple elements): a click adds an element of any kind to the selection, or
    //removes it; dragging selects every element inside a rectangle (with the modifier key: removes them). The selected
    //elements are outlined, and a handle at their center moves, rotates and scales them together with the gizmo (see
    //Operations.GroupTransform).
    public class MultiSelector : TerrainAction {
        const uint OutlineLayer = 1u << 15;
        const float DragThreshold = 6; //pixels: under it a press and release is a click
        readonly CityBuilderMenuBar builder;
        readonly ElementManager manager;
        readonly List<object> selection = new List<object>();
        public System.Action onSelectionChanged;

        Handle pivot;
        Operations.GroupTransform.Session session;
        Vector3 pivotStart;
        Quaternion pivotStartRotation;
        Vector3 pivotStartScale;

        bool pressed = false;
        bool dragging = false;
        Vector2 pressPosition;
        object pressElement;
        Panel rectangle;

        SelectionOutline outline;
        List<GeometryInstance3D> outlined = new List<GeometryInstance3D>();
        bool hooked = false;

        public MultiSelector(CityBuilderMenuBar builder) {
            this.builder = builder;
            manager = builder.helper.elementManager;
        }

        public IReadOnlyList<object> Selection => selection;

        public bool IsActive() {
            return builder.terrainClick.modifier == this;
        }

        // Selection

        void Prune() {
            if (selection.RemoveAll(e => !Operations.GroupTransform.Accepts(e) || !History.ElementSnapshot.IsAlive(e)) > 0) SelectionChanged();
        }

        public void Clear() {
            if (selection.Count == 0) return;
            selection.Clear();
            SelectionChanged();
        }

        public void SelectAll() {
            var added = false;
            void Add(object element) {
                if (!Operations.GroupTransform.Accepts(element) || selection.Contains(element)) return;
                selection.Add(element);
                added = true;
            }
            foreach (var e in manager.roads) Add(e);
            foreach (var e in manager.intersections) Add(e);
            foreach (var e in manager.patches) Add(e);
            foreach (var e in manager.buildings) Add(e);
            foreach (var e in manager.meshes) Add(e);
            foreach (var e in manager.customElements) Add(e);
            if (added) SelectionChanged();
        }

        void Toggle(object element) {
            if (!selection.Remove(element)) selection.Add(element);
            SelectionChanged();
        }

        void SelectionChanged() {
            UpdatePivot();
            onSelectionChanged?.Invoke();
        }

        //the node whose meshes are outlined, and whose bounds give where the element is
        static Node3D NodeOf(object element) {
            return element is Intersection intersection ? intersection.geo : element as Node3D;
        }

        void UpdateOutline() {
            foreach (var render in outlined) SelectionOutline.RemoveFromMask(render, OutlineLayer);
            outlined = new List<GeometryInstance3D>();
            foreach (var element in selection) {
                var node = NodeOf(element);
                if (!GO.IsNull(node)) outlined.AddRange(SelectionOutline.GetRenderers(node));
            }
            if (outline == null) {
                if (outlined.Count == 0) return;
                outline = new SelectionOutline { Name = "MultiSelectionOutline", maskLayer = OutlineLayer, outlineColor = new Color(1.0f, 0.85f, 0.1f) };
                builder.gizmo.AddChild(outline);
            }
            outline.UpdateOutline(outlined);
        }

        // The handle at the center of the selection, moved with the gizmo

        void UpdatePivot() {
            var gizmo = builder.gizmo;
            var center = selection.Count > 0 ? Operations.GroupTransform.CenterOf(manager, selection) : null;
            if (!center.HasValue) {
                if (pivot != null && gizmo.HasTarget(pivot)) gizmo.ClearTargets();
                if (pivot != null) GO.SetEnabled(pivot, false);
                return;
            }
            if (GO.IsNull(pivot)) {
                pivot = GameEngine.Resources.CreatePointHandle(manager, true);
                GO.SetName(pivot, "MultiSelectionPivot");
                pivot.SetColor(new Color(1.0f, 0.85f, 0.1f));
                pivot.SetScale(GameEngine.Vector3.one * 2.0f);
                GO.SetLayer(pivot, 8);
                pivot.SetMeta(CityBuilderMenuBar.ToolHandleMeta, true);
            }
            GO.SetEnabled(pivot, true);
            ResetPivot(center.Value);
            if (!gizmo.HasTarget(pivot)) {
                var type = gizmo.transformType == RuntimeGizmos.TransformType.None ? RuntimeGizmos.TransformType.Move : gizmo.transformType;
                gizmo.ClearTargets();
                gizmo.AddTarget(pivot);
                gizmo.transformType = type;
            }
            gizmo.SetPivotPoint();
        }

        void ResetPivot(Vector3 position) {
            GO.SetPosition(pivot, position);
            GO.SetRotation(pivot, Quaternion.Identity);
            GO.SetLocalScale(pivot, GameEngine.Vector3.one);
        }

        void OnTransformStarted(IReadOnlyList<Node3D> targets) {
            session = null;
            if (!IsActive() || GO.IsNull(pivot) || targets.Count != 1 || targets[0] != pivot) return;
            Prune();
            pivotStart = GO.GetPosition(pivot);
            pivotStartRotation = GO.GetRotationQuaternion(pivot);
            pivotStartScale = GO.GetLocalScale(pivot);
            //inside the undo step of the gizmo: the elements are recorded as they were
            session = Operations.GroupTransform.Begin(manager, selection, pivotStart);
        }

        void OnTransformChanged() {
            if (session == null || GO.IsNull(pivot)) return;
            var scale = GO.GetLocalScale(pivot);
            float Factor(float now, float start) => Mathf.Abs(start) < 0.001f || Mathf.Abs(now) < 0.001f ? 1 : now / start;
            Operations.GroupTransform.Apply(session, GO.GetPosition(pivot) - pivotStart,
                GO.GetRotationQuaternion(pivot) * pivotStartRotation.Inverse(),
                new Vector3(Factor(scale.X, pivotStartScale.X), Factor(scale.Y, pivotStartScale.Y), Factor(scale.Z, pivotStartScale.Z)));
            manager.FlagAsChanged();
        }

        void OnTransformFinished(IReadOnlyList<Node3D> targets) {
            if (session == null) return;
            session = null;
            //the next transform starts from a handle without rotation and scale, at the center of what moves now
            if (!GO.IsNull(pivot)) {
                ResetPivot(Operations.GroupTransform.CenterOf(manager, selection) ?? GO.GetPosition(pivot));
                builder.gizmo.SetPivotPoint();
            }
        }

        public void SetTransformType(RuntimeGizmos.TransformType type) {
            builder.gizmo.transformType = type;
        }

        // Starting and ending the mode

        public void Start() {
            builder.terrainClick.modifier = this;
            builder.gizmo.ClearTargets();
            builder.gizmo.allowDeselect = false; //the clicks select elements, the gizmo keeps the handle
            if (!hooked) {
                builder.gizmo.onTransformStarted += OnTransformStarted;
                builder.gizmo.onAnyChange += OnTransformChanged;
                builder.gizmo.onTransformFinished += OnTransformFinished;
                hooked = true;
            }
            pressed = dragging = false;
            UpdatePivot();
        }

        public void Stop() {
            selection.Clear();
            session = null;
            pressed = dragging = false;
            ShowRectangle(false);
            UpdateOutline();
            if (!GO.IsNull(pivot)) {
                if (builder.gizmo.HasTarget(pivot)) builder.gizmo.ClearTargets();
                GO.Destroy(pivot);
            }
            pivot = null;
            builder.gizmo.allowDeselect = true;
        }

        // Clicks and rectangles

        public override void Apply(List<RaycastHit?> hits) {
            Prune();
            UpdateOutline();
            var gizmo = builder.gizmo;
            var mouse = GameEngine.Input.MousePosition(builder.GetViewport());
            if (Input.GetInputStart(KeyCommand.Select)) {
                //the clicks on the gizmo and on the panels are not selections
                pressed = !gizmo.isNearAxis && !gizmo.isTransforming && !UIDetection.OnUI();
                dragging = false;
                pressPosition = mouse;
                pressElement = pressed && hits.Count > 0 && hits[0].HasValue ? Operations.GroupTransform.ElementOf(hits[0].Value.collider) : null;
                return;
            }
            if (!pressed) return;
            if (Input.GetInputPressed(KeyCommand.Select)) {
                if (!dragging && mouse.DistanceTo(pressPosition) >= DragThreshold) dragging = true;
                if (dragging) ShowRectangle(true, pressPosition, mouse);
                return;
            }
            //released
            pressed = false;
            ShowRectangle(false);
            if (dragging) {
                dragging = false;
                SelectInRectangle(new Rect2(pressPosition, Vector2.Zero).Expand(mouse), Input.GetInputPressed(KeyCommand.Modifier2));
            } else if (pressElement != null && Operations.GroupTransform.Accepts(pressElement)) {
                Toggle(pressElement);
            }
            pressElement = null;
        }

        void SelectInRectangle(Rect2 rect, bool remove) {
            var camera = builder.GetViewport().GetCamera3D();
            if (camera == null) return;
            var changed = false;
            void Check(object element) {
                if (!Operations.GroupTransform.Accepts(element) || selection.Contains(element) != remove) return;
                var bounds = Api.CityApi.BoundsOf(element); //in the coordinates of the engine, as the camera
                if (bounds == null) return;
                var center = bounds.Value.GetCenter();
                if (camera.IsPositionBehind(center) || !rect.HasPoint(camera.UnprojectPosition(center))) return;
                if (remove) selection.Remove(element);
                else selection.Add(element);
                changed = true;
            }
            foreach (var e in manager.roads) Check(e);
            foreach (var e in manager.intersections) Check(e);
            foreach (var e in manager.patches) Check(e);
            foreach (var e in manager.buildings) Check(e);
            foreach (var e in manager.meshes.ToArray()) Check(e);
            foreach (var e in manager.customElements) Check(e);
            if (changed) SelectionChanged();
        }

        void ShowRectangle(bool show, Vector2 from = default, Vector2 to = default) {
            if (rectangle == null || !GodotObject.IsInstanceValid(rectangle)) {
                if (!show) return;
                rectangle = new Panel { Name = "SelectionRectangle", MouseFilter = Control.MouseFilterEnum.Ignore };
                var box = new StyleBoxFlat { BgColor = new Color(1.0f, 0.85f, 0.1f, 0.12f), BorderColor = new Color(1.0f, 0.85f, 0.1f, 0.9f) };
                box.SetBorderWidthAll(1);
                rectangle.AddThemeStyleboxOverride("panel", box);
                builder.terrainClick.AddChild(rectangle);
            }
            rectangle.Visible = show;
            if (!show) return;
            var rect = new Rect2(from, Vector2.Zero).Expand(to);
            rectangle.GlobalPosition = rect.Position;
            rectangle.Size = rect.Size;
        }

        public override void ShowOnMap(List<RaycastHit?> hits) {
        }

        public override List<int> GetLayerMasks() {
            return new List<int> { ElementPicker.PickMask };
        }

        public override bool SnapsToGrid => false;

        public override bool Raycast(Node3D parent, Vector3 origin, Vector3 direction, out RaycastHit hit, float distance, int mask) {
            return GameEngine.Raycast.DoPickRaycast(parent, origin, direction, out hit, distance, mask);
        }
    }
}
