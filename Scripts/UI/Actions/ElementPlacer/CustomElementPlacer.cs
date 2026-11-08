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
            PrependPoint,
            PlacePoints //the placementPoints of the type, one per click (placeIndex: the next one)
        }

        public CustomElement curElement = null;
        public string createType;
        public ObjectState creationState;
        public string propertyContainerId;
        public PlacementMode placementMode = PlacementMode.None;
        public string arrayPath;
        public bool atStart;
        public int placeIndex;
        public System.Action onElementCreated;
        public System.Action onFrame; //every frame while the placer is the click modifier (e.g. to follow the selected point)
        public System.Action onModeEnded; //the placement mode ended by itself (e.g. a closed array was closed)
        readonly ElementManager manager;
        bool anchorsVisible;
        Operations.PointTargets.Slot? anchorsSlot; //the place of the next point the targets were shown for
        //the owner of the controls (see CustomElementSplineSettings.ownerReference) chosen in the menu, for the click applied again
        object chosenOwner;
        PopupMenu ownerMenu;
        System.Action<object> ownerMenuChosen;
        public List<object> ownerChoices; //the elements offered by the open menu (null if closed)

        public CustomElementPlacer(ElementManager manager) {
            this.manager = manager;
        }

        public void SetElement(CustomElement element) {
            if (element != curElement) EndPlacementRotation();
            if (!GO.IsNull(curElement) && !curElement.IsDeleted()) curElement.SetPlacementPoint(null);
            curElement = GO.IsNull(element) || (element != null && element.IsDeleted()) ? null : element;
            if (curElement != null) propertyContainerId = Operations.PropertyContainers.Scope(curElement.state);
        }

        public CustomElement GetElement() {
            if (curElement != null && (GO.IsNull(curElement) || curElement.IsDeleted())) curElement = null;
            return curElement;
        }

        bool Guards(List<RaycastHit?> hits) {
            if (!manager.builder.customElementEditorPanel.ActiveSelf()) return false;
            onFrame?.Invoke();
            UpdateAnchorVisibility();
            UpdatePlacementPoint();
            if (placementMode == PlacementMode.None) return false;
            if ((GetElement()?.Settings?.spline != null || placementMode == PlacementMode.PlacePoints) && placementMode != PlacementMode.Create &&
                (GameEngine.Gizmo.IsNearAxis() || (hits[0].HasValue &&
                GS.FindComponent<TerrainPoint>((Node3D)hits[0].Value.collider) is TerrainPoint p && curElement.HasPoint(p) && !AddsOnIntersection(p)))) return false;
            if (!hits[0].HasValue && !hits[1].HasValue && !hits[2].HasValue) return false;
            ShowOnMap(hits);
            if (placementMode == PlacementMode.PlacePoints && PlacementLeaf(placeIndex - 1)?.point is TerrainPoint last && !last.IsDeleted()) {
                var hit = GetHit(hits);
                if (!Refused(hit)) ActionHandlerManager.ShowGuideLine(last.GetPoint(), Actions.Helpers.GetAnchorInfo((Node3D)hit.collider, hit.point).pos);
            }
            if (!Input.GetInputStart(KeyCommand.Select)) return false;
            return true;
        }

        //a click adding a point on a point of the element on the center of an intersection: a click on the intersection (the
        //point isn't shared, see PointTargets.IntersectionAt), which gets another point (e.g. a route passing there again),
        //rather than the selection of the point
        bool AddsOnIntersection(TerrainPoint point) {
            if (placementMode != PlacementMode.AppendPoint && placementMode != PlacementMode.PrependPoint && placementMode != PlacementMode.InsertPoint) return false;
            return point.anchor is IntersectionAnchor centre && !centre.IsDeleted() && Operations.PointTargets.AcceptsIntersections(GetRuntimeType());
        }

        //the first hit (points, segments, then the ground) on a target the type accepts (see PointTargets), the first one if none is
        RaycastHit GetHit(List<RaycastHit?> hits) {
            if (FiltersTargets()) {
                var type = GetRuntimeType();
                var element = placementMode == PlacementMode.Create ? null : GetElement();
                var slot = NextSlot();
                foreach (var h in hits) {
                    if (h.HasValue && Operations.PointTargets.Allows(type, (Node3D)h.Value.collider, element, slot)) return h.Value;
                }
            }
            return hits[0].HasValue ? hits[0].Value : hits[1].HasValue ? hits[1].Value : hits[2].Value;
        }

        //the clicks place control points of a type choosing its targets (acceptTarget or ownerReference); not the first click
        //of a type with a placementObject (it places the object)
        bool FiltersTargets() {
            if (placementMode != PlacementMode.Create && placementMode != PlacementMode.AppendPoint && placementMode != PlacementMode.PrependPoint
                && placementMode != PlacementMode.InsertPoint && placementMode != PlacementMode.PlacePoints) return false;
            if (placementMode == PlacementMode.Create && GetSettings()?.placementObject != null) return false;
            var type = GetRuntimeType();
            return type != null && (type.acceptTargetFunction != null || Operations.PointTargets.OwnerParameter(type) != null);
        }

        //the type doesn't accept the target under the mouse (nothing is shown there)
        bool Refused(RaycastHit hit) {
            return FiltersTargets() && !Operations.PointTargets.Allows(GetRuntimeType(), (Node3D)hit.collider, placementMode == PlacementMode.Create ? null : GetElement(), NextSlot());
        }

        //the place of the point the next click adds to an array of points (see PointTargets.Slot), null if none
        Operations.PointTargets.Slot? NextSlot() {
            if (placementMode == PlacementMode.Create) {
                var settings = GetSettings();
                return settings?.spline != null && settings.placementObject == null ? new Operations.PointTargets.Slot(0, 1) : null;
            }
            var index = NextIndex(out var list);
            return index < 0 ? null : Operations.PointTargets.NewSlot(list, index);
        }

        //the index the next click adds a point at in its array (list), -1 if it doesn't add one
        int NextIndex(out CustomElements.PointList list) {
            list = null;
            var element = GetElement();
            if (element?.points == null) return -1;
            switch (placementMode) {
                case PlacementMode.AppendPoint:
                    list = arrayPath == null ? null : CustomElements.PointTree.Find(element.points, arrayPath) as CustomElements.PointList;
                    return list == null ? -1 : list.items.Count;
                case PlacementMode.PrependPoint:
                case PlacementMode.InsertPoint:
                    list = CustomElements.PointTree.Find(element.points, "spline") as CustomElements.PointList;
                    if (list == null) return -1;
                    if (placementMode == PlacementMode.PrependPoint) return 0;
                    var selectedPoint = manager.builder.gizmo.mainTargetRoot == null ? null : GS.FindRecursive<TerrainPoint>(manager.builder.gizmo.mainTargetRoot);
                    var index = list.items.FindIndex(n => ((CustomElements.PointLeaf)n).point == selectedPoint);
                    return index < 0 ? -1 : atStart ? index : index + 1;
            }
            return -1;
        }

        //the point added at the index would leave a point already there at a place where it isn't accepted (e.g. a point
        //that can be free only as the last one, see PointTargets.KeepsAccepted): the user is told, nothing is added
        bool RefusesNewPlaces(CustomElements.PointList list, int index) {
            if (list == null || index < 0 || Operations.PointTargets.KeepsAccepted(GetElement(), list, index)) return false;
            ShowRefusal("CE_POINT_PLACES_ERROR");
            return true;
        }

        void ShowRefusal(string genericKey) {
            manager.builder.CreateAlert(StringManager.Get("ERROR"), Operations.PointTargets.RefusalMessage(GetRuntimeType(), genericKey), StringManager.Get("OK"),
                null, 260, true, 520); //room for an explanation of the type (targetsHint)
        }

        //a click adding a point on a target the type refuses: nothing is added, the types explaining where their points go
        //(targetsHint) tell it (the others ignore the click, as they always did)
        bool RefusedClick(RaycastHit hit) {
            if (!Refused(hit)) return false;
            if (Operations.PointTargets.HasTargetsHint(GetRuntimeType())) ShowRefusal("CE_POINT_TARGET_ERROR");
            return true;
        }

        public override void Apply(List<RaycastHit?> hits) {
            if (!Guards(hits)) return;
            //not also the selection of the point clicked
            if (hits[0].HasValue && GS.FindComponent<TerrainPoint>((Node3D)hits[0].Value.collider) is TerrainPoint p && GetElement()?.HasPoint(p) == true) GameEngine.Gizmo.ConsumeClick();
            PlaceAt(GetHit(hits));
        }

        public void PlaceAt(RaycastHit hit) {
            if (!Operations.PropertyContainers.IsCurrent(propertyContainerId)) {
                placementMode = PlacementMode.None;
                return;
            }
            manager.worldChanged = true;
            var obj = (Node3D)hit.collider;
            if (chosenOwner == null && AsksOwner(obj, hit)) return;
            var ownerBefore = Operations.PointTargets.OwnerOf(GetElement());
            if (chosenOwner != null && placementMode != PlacementMode.Create) Operations.PointTargets.SetOwner(GetElement(), chosenOwner);
            PlaceAtTarget(hit, obj);
            //the targets of the owner only, from now on
            if (Operations.PointTargets.OwnerOf(GetElement()) != ownerBefore) UpdateAnchorVisibility(true);
        }

        void PlaceAtTarget(RaycastHit hit, Node3D obj) {
            switch (placementMode) {
                case PlacementMode.Create:
                    if (createType == null) return;
                    //the first point where the type accepts it (not the object of a placementObject)
                    if (FiltersTargets() && !Operations.PointTargets.Allows(GetRuntimeType(), obj, null, NextSlot())) return;
                    curElement = Operations.CustomElements.Create(manager, createType, hit.point);
                    if (creationState != null) Operations.CustomElementPresets.Apply(curElement, creationState);
                    if (chosenOwner != null) Operations.PointTargets.SetOwner(curElement, chosenOwner);
                    if (creationState != null) foreach (var parameter in curElement.GetCustomElementType().parameterContainer.parameters ?? System.Array.Empty<CityElements.Types.Parameter>()) {
                        if (parameter.type == "objectInstance" && creationState.State(parameter.fullName()) is ObjectState value) {
                            curElement.state.SetState(parameter.fullName(), (ObjectState)value.Clone());
                        }
                    }
                    Operations.PropertyContainers.Assign(curElement.state, propertyContainerId);
                    if (curElement.Settings?.point?.anchorable == true) {
                        Operations.TerrainPoints.Attach(((CustomElements.PointLeaf)curElement.points.Get("center")).point, obj, hit.point, false);
                    }
                    placementMode = PlacementMode.None;
                    if ((curElement.Settings?.placementPoints?.Length ?? 0) > 0) {
                        //the first point where clicked, the others wait on it for their own clicks
                        var placementPoints = curElement.Settings.placementPoints;
                        placeIndex = 0;
                        PlacePoint(placementPoints[0], obj, hit.point);
                        var first = PlacementLeaf(0)?.point;
                        for (int i = 1; i < placementPoints.Length; i++) {
                            var waiting = PlacementLeaf(i)?.point;
                            if (waiting != null && first != null) GO.SetPosition(waiting.GetObject(), first.GetPoint());
                        }
                        placeIndex = 1;
                        if (placeIndex < placementPoints.Length) placementMode = PlacementMode.PlacePoints;
                    }
                    if (curElement.Settings?.spline != null || curElement.Settings?.propPlacer?.type == "polygon") {
                        arrayPath = curElement.Settings?.propPlacer?.type == "polygon" ? "perimeter" : "spline";
                        var initialObject = curElement.Settings.placementObject;
                        if (initialObject != null) {
                            var start = curElement.state.State(initialObject) ?? new ObjectState();
                            start.SetVector3("localPosition", hit.point);
                            curElement.state.SetState(initialObject, start);
                        } else Operations.CustomElements.AppendPoint(manager, curElement, arrayPath, obj, hit.point);
                        placementMode = PlacementMode.AppendPoint;
                    }
                    curElement.SetActive(true, true);
                    curElement.Select(true);
                    onElementCreated?.Invoke();
                    var placed = curElement;
                    var objectKey = placed.Settings?.placementObject;
                    if (objectKey != null && placed.GetCustomElementType().objectInstanceParams.TryGetValue(objectKey, out var objectParam) &&
                        objectParam.objectInstanceSettings.allowRotation?[1] == true) {
                        BeginPlacementRotation(manager.GetViewport(), () => placed.FindParametricObject(objectKey, new List<string>()),
                            () => GetElement() == placed && manager.builder.customElementEditorPanel.ActiveSelf() &&
                                Operations.PropertyContainers.IsCurrent(propertyContainerId) && objectParam.objectInstanceSettings.allowRotation?[1] == true);
                    } else if (placed.Settings?.point?.RotationAxes()[1] == true) {
                        BeginPlacementRotation(manager.GetViewport(), () => (placed.points.Get("center") as CustomElements.PointLeaf)?.point,
                            () => GetElement() == placed && manager.builder.customElementEditorPanel.ActiveSelf() &&
                                Operations.PropertyContainers.IsCurrent(propertyContainerId) && placed.Settings.point.RotationAxes()[1]);
                    }
                    //a point element has its gizmo on the element, which the point deselection would remove
                    if (curElement.Settings?.point != null) {
                        UpdatePlacementPoint();
                        return;
                    }
                    break;
                case PlacementMode.AppendPoint:
                    if (GetElement() == null) return;
                    if (ClosesArray(obj)) {
                        if (curElement.Settings?.spline?.closable == true) curElement.state.SetBool("closed", true);
                        placementMode = PlacementMode.None;
                        onModeEnded?.Invoke();
                        return;
                    }
                    var appendAt = NextIndex(out var appendList);
                    if (RefusedClick(hit) || RefusesNewPlaces(appendList, appendAt)) return;
                    var point = Operations.CustomElements.AppendPoint(manager, curElement, arrayPath, obj, hit.point, Input.GetInputPressed(KeyCommand.Modifier2));
                    point?.Select(true);
                    break;
                case PlacementMode.PrependPoint:
                case PlacementMode.InsertPoint:
                    if (GetElement() == null) return;
                    var index = NextIndex(out var list);
                    if (index < 0 || RefusedClick(hit) || RefusesNewPlaces(list, index)) return;
                    Operations.CustomElements.InsertPointAt(manager, curElement, "spline", index, obj, hit.point);
                    break;
                case PlacementMode.AppendElement:
                    if (GetElement() == null) return;
                    Operations.CustomElements.AppendArrayElementAt(curElement, arrayPath, atStart, hit.point);
                    break;
                case PlacementMode.PlacePoints:
                    if (GetElement() == null || PlacementLeaf(placeIndex) == null) {
                        placementMode = PlacementMode.None;
                        return;
                    }
                    if (!PlacePoint(curElement.Settings.placementPoints[placeIndex], obj, hit.point)) return;
                    placeIndex++;
                    if (PlacementLeaf(placeIndex) == null) {
                        placementMode = PlacementMode.None;
                        UnselectPointDelayed();
                        onModeEnded?.Invoke();
                        return;
                    }
                    break;
            }
            UnselectPointDelayed();
            UpdatePlacementPoint();
        }

        //places the points of the placementPoints of the edited element again, from the first one
        public void StartPointPlacement() {
            placeIndex = 0;
            placementMode = PlacementLeaf(0) != null ? PlacementMode.PlacePoints : PlacementMode.None;
        }

        //the point of the edited element at the index of the placementPoints of its type, null past the last one
        public CustomElements.PointLeaf PlacementLeaf(int index) {
            var paths = GetElement()?.Settings?.placementPoints;
            if (paths == null || index < 0 || index >= paths.Length) return null;
            return CustomElements.PointTree.Find(curElement.points, paths[index]) as CustomElements.PointLeaf;
        }

        //the point at the path goes where clicked: on the clicked point (shared with its elements from now on), anchor
        //(e.g. a road end) or segment, or free on the ground. A point shared with other elements leaves them first (they
        //keep their point); the other points of the element can't be clicked. False if nothing was placed
        bool PlacePoint(string path, Node3D obj, Vector3 position) {
            var leaf = CustomElements.PointTree.Find(curElement.points, path) as CustomElements.PointLeaf;
            if (leaf?.point == null || leaf.point.IsDeleted()) return false;
            var clicked = GS.FindComponent<TerrainPoint>(obj);
            if (clicked != null && clicked != leaf.point && curElement.HasPoint(clicked)) return false;
            var point = leaf.point;
            if (point.GetLinks().Count > 1 && clicked != point) point = Operations.CustomElements.DetachPoint(manager, curElement, point);
            if (point == null) return false;
            if (leaf.def.anchorable && obj != null && Operations.CustomElements.AttachPoint(manager, curElement, point, obj, position) != null) return true;
            if (point.anchor != null) Operations.TerrainPoints.Detach(point); //free, not sliding along a segment it was on
            Operations.TerrainPoints.Move(point, position);
            return true;
        }

        public void UpdatePlacementPoint() {
            var element = GetElement();
            if ((element?.Settings?.placementPoints?.Length ?? 0) > 0) {
                //the last point placed, while the next one is placed
                var placed = placementMode == PlacementMode.PlacePoints && element.IsActive() ? PlacementLeaf(placeIndex - 1)?.point : null;
                element.SetPlacementPoint(placed);
                return;
            }
            if (element?.Settings?.spline == null && element?.Settings?.propPlacer == null) return;
            var list = element.points.Get(element.Settings.propPlacer?.type == "polygon" ? "perimeter" : "spline") as CustomElements.PointList;
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
            var closable = arrayPath == "spline" && curElement.Settings?.spline?.closable == true;
            if (list == null || !(list.def.array.closed || closable) || list.items.Count < 2) return false;
            //a loop of two points only follows the connections (the second span goes back the other way)
            if (closable && list.items.Count < 3 && !curElement.Settings.spline.FollowsConnectionsDynamically) return false;
            if (curElement.Settings?.propPlacer?.type == "polygon" && list.items.Count < 3) return false;
            var clicked = GS.FindComponent<TerrainPoint>(obj);
            if (list.items[0] is not CustomElements.PointLeaf first) return false;
            var anchor = GS.FindComponent<TerrainAnchor>(obj);
            return clicked != null && first.point == clicked || anchor != null && AddTerrainPointToSequence.AnchorEquals(first.point.anchor, anchor);
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
            if (Refused(hit)) return;
            (Vector3 pos, float size, Color color) = Actions.Helpers.GetAnchorInfo((Node3D)hit.collider, hit.point);
            ActionHandlerManager.ShowHandle(pos, size, color);
        }

        //An element whose sections are projected (e.g. the MM2 decals) is placed with the same surface rule as its sections
        //(GeometryHelper.AutoProjectOnSurface), so its points go where its sections go; anchors and point links keep their
        //own hit priority, and the other elements are placed on what is visible.
        public override bool Raycast(Node3D parent, Vector3 origin, Vector3 direction, out RaycastHit hit, float distance, int mask) {
            var settings = GetSettings();
            if (settings?.propPlacer != null && ProjectsPlacement(settings)) {
                if ((mask & ((1 << 3) | (1 << 7))) == 0) return base.Raycast(parent, origin, direction, out hit, distance, mask);
                return GeometryHelper.AutoRaycastSurface(parent, origin, direction, out hit, distance, mask, GetElement()?.generator);
            }
            if ((mask & ((1 << 3) | (1 << 7))) == 0 || (settings?.spline?.projectSections != true && settings?.point?.project != true)) {
                return base.Raycast(parent, origin, direction, out hit, distance, mask);
            }
            var exclude = placementMode == PlacementMode.Create ? null : GetElement()?.generator;
            return GeometryHelper.AutoRaycastSurface(parent, origin, direction, out hit, distance, mask, exclude);
        }

        // Owner of the controls (see CustomElementSplineSettings.ownerReference)

        //the first control of an element without owner, on a target of several elements which could own it: the user chooses
        //one in a menu, then the click is applied again with it (in its own undo step)
        bool AsksOwner(Node3D obj, RaycastHit hit) {
            if (placementMode != PlacementMode.Create && placementMode != PlacementMode.AppendPoint && placementMode != PlacementMode.PrependPoint
                && placementMode != PlacementMode.InsertPoint) return false;
            var type = GetRuntimeType();
            if (Operations.PointTargets.OwnerParameter(type) == null) return false;
            var element = placementMode == PlacementMode.Create ? null : GetElement();
            if (element != null && Operations.PointTargets.OwnerOf(element) != null) return false;
            var candidates = Operations.PointTargets.Candidates(type, obj, element);
            if (candidates.Count < 2) return false;
            var mode = placementMode;
            ShowOwnerMenu(candidates, owner => {
                if (placementMode != mode || (element != null && GetElement() != element)) return;
                History.UndoManager.Record("choose owner", () => {
                    chosenOwner = owner;
                    try { PlaceAt(hit); } finally { chosenOwner = null; }
                });
            });
            return true;
        }

        void ShowOwnerMenu(List<object> candidates, System.Action<object> chosen) {
            CloseOwnerMenu();
            ownerChoices = candidates;
            ownerMenuChosen = chosen;
            ownerMenu = new PopupMenu();
            UILayout.StylePopup(ownerMenu);
            ownerMenu.AddSeparator(StringManager.Get("CE_OWNER_CHOICE"));
            for (int i = 0; i < candidates.Count; i++) ownerMenu.AddItem(Api.CityApi.GetName(candidates[i]) ?? "?", i);
            ownerMenu.IdPressed += id => ChooseOwner((int)id);
            var menu = ownerMenu;
            //closed without a choice (after the choice, which closes it too)
            ownerMenu.PopupHide += () => manager.builder.DoDelayed(() => { if (ownerMenu == menu) CloseOwnerMenu(); });
            manager.builder.AddChild(ownerMenu);
            ownerMenu.Popup(new Rect2I((Vector2I)GameEngine.Input.MousePosition(manager.GetViewport()), Vector2I.Zero));
        }

        //the element at the index of ownerChoices owns the controls (from the menu, or the tests)
        public void ChooseOwner(int index) {
            var choices = ownerChoices;
            var chosen = ownerMenuChosen;
            CloseOwnerMenu();
            if (choices == null || index < 0 || index >= choices.Count) return;
            chosen?.Invoke(choices[index]);
        }

        public void CloseOwnerMenu() {
            ownerChoices = null;
            ownerMenuChosen = null;
            if (ownerMenu != null && GodotObject.IsInstanceValid(ownerMenu)) ownerMenu.QueueFree();
            ownerMenu = null;
        }

        //the type of the edited element (or of the one to create)
        CityElements.Types.Runtime.CustomElementType GetRuntimeType() {
            return GetElement()?.GetCustomElementType() ?? (createType != null && CityElements.Types.Parsers.TypeParser.GetCustomElementTypes()
                .TryGetValue(createType, out var type) ? type : null);
        }

        //the settings of the type of the edited element (or of the one to create)
        CityElements.Types.CustomElementSettings GetSettings() {
            return GetElement()?.Settings ?? (createType != null && CityElements.Types.Parsers.TypeParser.GetCustomElementTypes()
                .TryGetValue(createType, out var type) ? type.typeData.settings : null);
        }

        public bool AnchorablePlacement {
            get {
                var settings = GetSettings();
                if (settings?.point != null) return settings.point.anchorable;
                if (placementMode == PlacementMode.Create && settings?.placementObject != null) return false; //the object goes on the ground
                if (placementMode == PlacementMode.PlacePoints) return PlacementLeaf(placeIndex)?.def.anchorable == true;
                if (placementMode == PlacementMode.Create && (settings?.placementPoints?.Length ?? 0) > 0) {
                    return CityElements.Types.CustomElementType.FindPointNode(settings.controlPoints, settings.placementPoints[0])?.anchorable == true;
                }
                if (placementMode != PlacementMode.Create && GetElement() is CustomElement element && arrayPath != null) {
                    return CustomElements.PointTree.Find(element.points, arrayPath)?.def.anchorable == true;
                }
                return settings?.spline != null || settings?.propPlacer != null;
            }
        }

        //All placement entry points share the same target visibility, including the first click and resumed modes.
        public void UpdateAnchorVisibility(bool force = false) {
            var show = manager.builder.terrainClick.modifier == this && manager.builder.customElementEditorPanel.ActiveSelf()
                && placementMode != PlacementMode.None && AnchorablePlacement;
            var type = GetRuntimeType();
            //the targets of a function looking at the place of the point change with it (shown as for a point followed by
            //another one, see PointTargets.ShownSlot)
            var slot = show && FiltersTargets() && type?.acceptTargetFunction != null ? Operations.PointTargets.ShownSlot(NextSlot()) : null;
            if (!force && show == anchorsVisible && slot == anchorsSlot) return;
            anchorsVisible = show;
            anchorsSlot = slot;
            manager.ShowAnchors(show, true, true, GetSettings()?.roadEndAnchors == true, show && Operations.PointTargets.AcceptsIntersections(type));
            var placesObject = placementMode == PlacementMode.Create && GetSettings()?.placementObject != null;
            if (show && !placesObject) Operations.PointTargets.HideRefused(manager, type, placementMode == PlacementMode.Create ? null : GetElement(), slot);
            if (manager.builder.customElementEditorPanel.ActiveSelf()) manager.builder.customElementEditorPanel.ShowElement(GetElement());
        }

        bool ProjectsPlacement(CityElements.Types.CustomElementSettings settings) => GetElement()?.ProjectsPlacement
            ?? creationState?.Bool("projectToGround", settings.propPlacer.projectToGround) ?? settings.propPlacer.projectToGround;

        //terrain points and anchors (and the centers of the intersections, for the types with intersectionTargets), point
        //links, then the terrain and the generated meshes
        public override List<int> GetLayerMasks() {
            if (!AnchorablePlacement) return new List<int> { 0, 0, (1 << 3) | (1 << 7) };
            var points = GetSettings()?.propPlacer?.type == "polygon" ? (1 << 10) | (1 << 8) : 1 << 10;
            if (Operations.PointTargets.AcceptsIntersections(GetRuntimeType())) points |= 1 << 9;
            return new List<int> { points, 1 << 12, (1 << 3) | (1 << 7) };
        }

        public override int GroundHitIndex => 2;

        //not for the elements whose points or sections are always put on the surface
        public override bool SupportsPlacementHeight {
            get {
                if (placementMode == PlacementMode.None) return false;
                var settings = GetSettings();
                if (settings?.propPlacer != null) return !ProjectsPlacement(settings);
                if (settings?.point?.project == true) return false;
                var spline = settings?.spline;
                return spline == null || (!spline.projectPoints && !spline.projectSections);
            }
        }
    }
}
