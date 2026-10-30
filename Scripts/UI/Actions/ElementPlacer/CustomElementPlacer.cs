using System.Collections.Generic;
using Godot;
using Input = GameEngine.Input;
using KeyCommand = GameEngine.Input.KeyCommand;
using GS = GameEngine.Scriptable;
using GO = GameEngine.GameObject;
using RaycastHit = GameEngine.Raycast.RaycastHit;

namespace ElementPlacer {
    //The clicks while a custom element is edited (or created)
    public class CustomElementPlacer : TerrainAction {
        public enum PlacementMode {
            None,
            Create, //a new element of createType where clicked
            AppendPoint, //a point at the end of the array at arrayPath (an array of points)
            AppendElement, //a new element at the start (atStart) or end of the array at arrayPath, centered where clicked
            InsertPoint,
            PrependPoint
        }

        public CustomElement curElement = null;
        public string createType;
        public ObjectState creationState;
        public PlacementMode placementMode = PlacementMode.None;
        public string arrayPath;
        public bool atStart;
        public System.Action onElementCreated;
        public System.Action onFrame; //every frame while the placer is the click modifier (e.g. to follow the selected point)
        public System.Action onModeEnded; //the placement mode ended by itself (e.g. a closed array was closed)
        readonly ElementManager manager;

        public CustomElementPlacer(ElementManager manager) {
            this.manager = manager;
        }

        public void SetElement(CustomElement element) {
            if (!GO.IsNull(curElement) && !curElement.IsDeleted()) curElement.SetPlacementPoint(null);
            curElement = GO.IsNull(element) || (element != null && element.IsDeleted()) ? null : element;
        }

        public CustomElement GetElement() {
            if (curElement != null && (GO.IsNull(curElement) || curElement.IsDeleted())) curElement = null;
            return curElement;
        }

        bool Guards(List<RaycastHit?> hits) {
            onFrame?.Invoke();
            UpdatePlacementPoint();
            if (placementMode == PlacementMode.None) return false;
            if (GetElement()?.Settings?.spline != null && placementMode != PlacementMode.Create &&
                (GameEngine.Gizmo.IsNearAxis() || (hits[0].HasValue &&
                GS.FindRecursive<TerrainPoint>((Node3D)hits[0].Value.collider) is TerrainPoint p && curElement.HasPoint(p)))) return false;
            if (!hits[0].HasValue && !hits[1].HasValue && !hits[2].HasValue) return false;
            ShowOnMap(hits);
            if (!Input.GetInputStart(KeyCommand.Select)) return false;
            return true;
        }

        RaycastHit GetHit(List<RaycastHit?> hits) {
            return hits[0].HasValue ? hits[0].Value : hits[1].HasValue ? hits[1].Value : hits[2].Value;
        }

        public override void Apply(List<RaycastHit?> hits) {
            if (!Guards(hits)) return;
            PlaceAt(GetHit(hits));
        }

        public void PlaceAt(RaycastHit hit) {
            manager.worldChanged = true;
            var obj = (Node3D)hit.collider;
            switch (placementMode) {
                case PlacementMode.Create:
                    if (createType == null) return;
                    curElement = Operations.CustomElements.Create(manager, createType, hit.point);
                    if (creationState != null) Operations.CustomElementPresets.Apply(curElement.state, creationState, createType);
                    placementMode = PlacementMode.None;
                    if (curElement.Settings?.spline != null) {
                        arrayPath = "spline";
                        Operations.CustomElements.AppendPoint(manager, curElement, arrayPath, obj, hit.point);
                        placementMode = PlacementMode.AppendPoint;
                    }
                    curElement.SetActive(true, true);
                    curElement.Select(true);
                    onElementCreated?.Invoke();
                    break;
                case PlacementMode.AppendPoint:
                    if (GetElement() == null) return;
                    if (ClosesArray(obj)) {
                        placementMode = PlacementMode.None;
                        onModeEnded?.Invoke();
                        return;
                    }
                    var point = Operations.CustomElements.AppendPoint(manager, curElement, arrayPath, obj, hit.point);
                    point?.Select(true);
                    break;
                case PlacementMode.PrependPoint:
                case PlacementMode.InsertPoint:
                    if (GetElement() == null) return;
                    var list = CustomElements.PointTree.Find(curElement.points, "spline") as CustomElements.PointList;
                    var selectedPoint = manager.builder.gizmo.mainTargetRoot == null ? null : GS.FindRecursive<TerrainPoint>(manager.builder.gizmo.mainTargetRoot);
                    var index = placementMode == PlacementMode.PrependPoint ? 0 : list.items.FindIndex(n => ((CustomElements.PointLeaf)n).point == selectedPoint);
                    if (index < 0) return;
                    if (placementMode == PlacementMode.InsertPoint && !atStart) index++;
                    Operations.CustomElements.InsertPointAt(manager, curElement, "spline", index, obj, hit.point);
                    break;
                case PlacementMode.AppendElement:
                    if (GetElement() == null) return;
                    Operations.CustomElements.AppendArrayElementAt(curElement, arrayPath, atStart, hit.point);
                    break;
            }
            UnselectPointDelayed();
            UpdatePlacementPoint();
        }

        public void UpdatePlacementPoint() {
            var element = GetElement();
            if (element?.Settings?.spline == null) return;
            var list = element.points.Get("spline") as CustomElements.PointList;
            TerrainPoint point = null;
            if (element.IsActive() && list?.items.Count > 0) {
                if (placementMode == PlacementMode.AppendPoint) point = ((CustomElements.PointLeaf)list.items[^1]).point;
                else if (placementMode == PlacementMode.PrependPoint) point = ((CustomElements.PointLeaf)list.items[0]).point;
                else if (placementMode == PlacementMode.InsertPoint) {
                    var target = manager.builder.gizmo.mainTargetRoot;
                    if (!GO.IsNull(target)) {
                        var selected = GS.FindRecursive<TerrainPoint>(target);
                        if (selected != null && !selected.IsDeleted() && element.HasPoint(selected)) point = selected;
                    }
                }
            }
            element.SetPlacementPoint(point);
        }

        //the click is on the first point of the closed array points are added to
        bool ClosesArray(Node3D obj) {
            var list = CustomElements.PointTree.Find(curElement.points, arrayPath) as CustomElements.PointList;
            if (list == null || !list.def.array.closed || list.items.Count < 2) return false;
            var clicked = obj == null ? null : GS.FindRecursive<TerrainPoint>(obj);
            return clicked != null && list.items[0] is CustomElements.PointLeaf first && first.point == clicked;
        }

        void UnselectPointDelayed() {
            manager.builder.DoDelayed(UnselectPoint); //otherwise the immediate deselect gets overridden by the UI select
        }

        void UnselectPoint() {
            var oldDeselect = manager.builder.gizmo.allowDeselect;
            manager.builder.gizmo.allowDeselect = true;
            manager.builder.gizmo.ClearTargets();
            manager.builder.gizmo.allowDeselect = oldDeselect;
            UpdatePlacementPoint();
        }

        public override void ShowOnMap(List<RaycastHit?> hits) {
            var hit = GetHit(hits);
            (Vector3 pos, float size, Color color) = Actions.Helpers.GetAnchorInfo((Node3D)hit.collider, hit.point);
            ActionHandlerManager.ShowHandle(pos, size, color);
        }

        //terrain points and anchors, point links, then the terrain and the generated meshes
        public override List<int> GetLayerMasks() {
            return new List<int> { 1 << 10, 1 << 12, (1 << 3) | (1 << 7) };
        }
    }
}
