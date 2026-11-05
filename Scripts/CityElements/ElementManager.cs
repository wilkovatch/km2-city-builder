using System.Collections;
using System.Collections.Generic;
using Godot;
using Scriptable = Godot.Node3D;
using GameObject = Godot.Node3D;
using SM = StringManager;
using GO = GameEngine.GameObject;
using GS = GameEngine.Scriptable;
using System;

public partial class ElementManager : Scriptable {
    public List<Road> roads = new List<Road>();
    public List<Intersection> intersections = new List<Intersection>();
    public List<TerrainPatch> patches = new List<TerrainPatch>();
    public List<MeshInstance> meshes = new List<MeshInstance>();
    public List<BuildingLine> buildings = new List<BuildingLine>();
    public List<CustomElement> customElements = new List<CustomElement>();
    public bool worldChanged = false;
    ulong splineTerrainVersion = ulong.MaxValue;
    bool splineProjectionPending, splinePhysicsReady;
    readonly record struct ProjectionMeshState(Vector3 position, Vector3 rotation, Vector3 scale, string mesh, string type, bool enabled);
    Dictionary<MeshInstance, ProjectionMeshState> projectionMeshes = new();
    Dictionary<MeshInstance, ProjectionMeshState> nextProjectionMeshes = new();
    public bool propsChanged = false;
    public bool visibilityChanged = false;
    GameObject terrainPointContainer = null;
    GameObject terrainContainer = null;
    GameObject roadContainer = null;
    GameObject meshContainer = null;
    GameObject buildingContainer = null;
    GameObject customElementContainer = null;
    GameObject dummyContainer = null;
    GameObject activeDummyContainer = null;
    Dictionary<System.Type, GameObject> dummies = new Dictionary<System.Type, GameObject>();
    public CityBuilderMenuBar builder;
    //the parts of the roads split by the last action, finished at the next update (see RoadSplitter.FinishPart)
    readonly Dictionary<Road, RoadSplitter.Part> splitParts = new Dictionary<Road, RoadSplitter.Part>();
    readonly List<CustomElement> updatedCustomElements = new List<CustomElement>();
    int customElementAppliedVersion = -1;
    internal (int roads, int intersections, int customElements) lastRebuildCounts;
    internal (int roads, int intersections) lastRoadUpdateVisits;
    internal int cityUpdateCount;
    readonly List<Road> interactiveRoads = new();
    readonly List<Intersection> interactiveIntersections = new();
    readonly List<TerrainPatch> interactivePatches = new();
    readonly List<BuildingLine> interactiveBuildings = new();
    readonly List<CustomElement> interactiveCustomElements = new();
    readonly List<TerrainPoint> interactivePoints = new();
    internal (int patches, int buildings, int customElements) lastInteractiveDependencyVisits;
    (int roads, int intersections, int patches, int buildings, int customElements) interactiveMembership;
    bool interactiveRoadEdit, deferredRoadSurfacesChanged;

    public GameObject GetDummyContainer<T>() {
        if (typeof(T) == typeof(CityProperties)) {
            var obj = GetObject("ActiveDummies", ref activeDummyContainer);
            return obj;
        }
        else {
            var obj = GetObject("Dummies", ref dummyContainer);
            GO.SetEnabled(obj, false);
            return obj;
        }
    }

    public GameObject GetTerrainPointContainer() {
        return GetObject("TerrainPoints", ref terrainPointContainer);
    }

    public GameObject GetTerrainContainer() {
        return GetObject("TerrainPatches", ref terrainContainer);
    }

    public GameObject GetRoadContainer() {
        return GetObject("Roads", ref roadContainer);
    }

    public GameObject GetMeshContainer() {
        return GetObject("Meshes", ref meshContainer);
    }

    public GameObject GetBuildingContainer() {
        return GetObject("Buildings", ref buildingContainer);
    }

    public GameObject GetCustomElementContainer() {
        return GetObject("CustomElements", ref customElementContainer);
    }

    bool IsDummyPossible<T>() {
        var t = typeof(T);
        if (t == typeof(Road) || t == typeof(CityProperties)) {
            return true;
        }
        else if (t == typeof(BuildingLine)) {
            var preset = PresetManager.GetPreset("buildingLine", 0);
            return preset.Str("type", null) != null;
        }
        return false;
    }

    public T GetDummy<T>() where T : Scriptable, new() {
        if (!IsDummyPossible<T>()) return null;
        GameObject dummy;
        if (dummies.ContainsKey(typeof(T)) && !IsDestroyed(dummies[typeof(T)])) {
            dummy = dummies[typeof(T)];
        }
        else {
            dummy = GS.Create<T>("dummy (" + typeof(T).ToString() + ")", GetDummyContainer<T>());
            var c = GS.Cast<T>(dummy);
            dummies[typeof(T)] = dummy;
            if (c is Road r) {
                r.Initialize();
            }
            else if (c is BuildingLine bl) {
                bl.Initialize(dummy);
                bl.AddPoint(null, new Vector3(0, 0, 0), true);
                bl.AddPoint(null, new Vector3(1, 0, 0), true);
                bl.UpdateLine();
            }
            else if (c is CityProperties cp) {
                cp.Initialize();
                cp.state.FlagAsChanged();
                History.ElementRegistry.Register(cp, History.ElementRegistry.CityPropertiesId);
            }
        }
        var res = GS.Cast<T>(dummy);
        return res;
    }

    public Intersection GetDummyIntersection() {
        Intersection intersection;
        if (dummies.ContainsKey(typeof(Intersection)) && !IsDestroyed(dummies[typeof(Intersection)])) {
            var dummy = dummies[typeof(Intersection)];
            intersection = GS.FindRecursive<Intersection.IntersectionComponent>(dummy).intersection;
        }
        else {
            var dummy = GO.CreateStaticBody("dummy (Intersection)");
            intersection = new Intersection(dummy, null);
            dummies[typeof(Intersection)] = dummy;
            GO.SetParent(dummy, GetDummyContainer<Intersection>());
            // The generator object would be an orphan node that is never freed
            GO.SetParent(intersection.geo, GetDummyContainer<Intersection>());
        }
        return intersection;
    }

    //the elements are added through these methods, which give them the stable id used by undo/redo
    //(the id is passed when an element is recreated by undo)
    public void AddRoad(Road road, int id = -1) {
        roads.Add(road);
        History.ElementRegistry.Register(road, id);
    }

    public void AddIntersection(Intersection intersection, int id = -1) {
        intersections.Add(intersection);
        History.ElementRegistry.Register(intersection, id);
    }

    public RoadSplitter.Part GetSplitPart(Road road) {
        return splitParts.TryGetValue(road, out var part) ? part : null;
    }

    public void SetSplitPart(Road road, RoadSplitter.Part part) {
        splitParts[road] = part;
    }

    //the control points of the split roads are finished when the sizes of the intersections at their ends are known
    void FinishSplitParts() {
        var parts = new List<RoadSplitter.Part>(splitParts.Values);
        splitParts.Clear();
        //the sizes depend on the curves near the intersections: the parts with points ending up inside are finished again
        for (int pass = 0; pass < 3 && parts.Count > 0; pass++) {
            var changed = new List<RoadSplitter.Part>();
            foreach (var part in parts) {
                if (!RoadSplitter.FinishPart(part)) continue;
                part.road.UpdateLine();
                changed.Add(part);
            }
            if (changed.Count == 0) return;
            foreach (var intersection in intersections) {
                intersection.RecalculateSize();
            }
            parts = changed.FindAll(RoadSplitter.HasPointsInsideCuts);
        }
    }

    //a new road with the default preset and no points
    public Road CreateRoad() {
        var container = GetRoadContainer();
        var name = "Road " + Actions.Helpers.GetLatestObjectNumber(container, "Road ");
        var road = GS.Create<Road>(name);
        road.Initialize();
        AddRoad(road);
        GO.SetParent(road, container);
        return road;
    }

    //a new intersection with no roads
    public Intersection CreateIntersection(Vector3 position) {
        var newObj = (StaticBody3D)GameEngine.Resources.InstantiateObject("Handle");
        GO.SetPosition(newObj, position);
        var handle = GS.Cast<Handle>(newObj);
        handle.SetScale(GameEngine.Vector3.one * 3);
        handle.SetHandleActive(false);
        var intersection = new Intersection(newObj, GetRoadContainer());
        AddIntersection(intersection);
        GO.SetParent(intersection.geo, GetRoadContainer());
        return intersection;
    }

    public void AddPatch(TerrainPatch patch, int id = -1) {
        patches.Add(patch);
        History.ElementRegistry.Register(patch, id);
    }

    public void AddMesh(MeshInstance mesh, int id = -1) {
        meshes.Add(mesh);
        History.ElementRegistry.Register(mesh, id);
    }

    public void AddBuildingLine(BuildingLine line, int id = -1) {
        buildings.Add(line);
        History.ElementRegistry.Register(line, id);
    }

    public void AddCustomElement(CustomElement element, int id = -1) {
        customElements.Add(element);
        History.ElementRegistry.Register(element, id);
    }

    //a new custom element of the type, without points (named after its type, numbered after the existing ones)
    public CustomElement CreateCustomElement(string type, string name = null) {
        var container = GetCustomElementContainer();
        if (name == null) {
            var baseName = Operations.CustomElements.GetTypeLabel(type) + " ";
            name = baseName + Actions.Helpers.GetLatestObjectNumber(container, baseName);
        }
        CustomElement element = GS.Create<CustomElement>(name, container);
        element.Initialize(type, GetTerrainPointContainer());
        AddCustomElement(element);
        return element;
    }

    //the custom elements in the container
    public List<CustomElement> GetCustomElementChildren(CustomElement container) {
        var res = new List<CustomElement>();
        foreach (var element in customElements) {
            if (element.container == container && !element.IsDeleted()) res.Add(element);
        }
        return res;
    }

    //the terrain points are not kept in a list, every point belongs to at least a patch or a building line
    public List<TerrainPoint> GetTerrainPoints() {
        var res = new List<TerrainPoint>();
        var done = new HashSet<TerrainPoint>();
        void AddPoints(List<TerrainPoint> points) {
            foreach (var p in points) {
                if (done.Add(p)) res.Add(p);
            }
        }
        foreach (var patch in patches) {
            if (patch.IsDeleted()) continue;
            AddPoints(patch.GetPerimeterPointsComponents());
            AddPoints(patch.GetInternalPointsComponents());
            foreach (var borderMesh in patch.GetTerrainBorderMeshes()) AddPoints(borderMesh.segment);
        }
        foreach (var line in buildings) {
            if (line.IsDeleted()) continue;
            AddPoints(line.GetPointsComponents());
        }
        foreach (var element in customElements) {
            if (element.IsDeleted()) continue;
            AddPoints(element.GetAllPoints());
        }
        return res;
    }

    GameObject GetObject(string name, ref GameObject obj) {
        if (obj == null) {
            obj = GO.Create(name);
            GO.SetParent(obj, this);
        }
        return obj;
    }

    public List<GameObject> GetObjectList() {
        var res = new List<GameObject>();
        foreach (var road in roads) {
            if (!road.ghost) res.Add(GO.GetObject(road));
        }
        foreach (var intersection in intersections) {
            if (!intersection.ghost) res.Add(intersection.geo);
        }
        foreach (var patch in patches) {
            res.Add(GO.GetObject(patch));
        }
        foreach (var mesh in meshes) {
            if (Operations.PropertyContainers.Owner(mesh.settings) == "") res.Add(GO.GetObject(mesh));
        }
        foreach (var building in buildings) {
            res.Add(GO.GetObject(building));
        }
        foreach (var element in customElements) {
            if (element.container == null && Operations.PropertyContainers.Owner(element.state) == "") res.Add(element);
        }
        res.Sort((x, y) => Actions.Helpers.PadInts(GO.GetName(x)).CompareTo(Actions.Helpers.PadInts(GO.GetName(y))));
        return res;
    }

    public void DeselectMeshes() {
        foreach (var mesh in meshes) {
            mesh.SetMoveable(false);
        }
        foreach (var road in roads) {
            road.SetParametricObjectsMoveable(false);
        }
    }

    public void ShowIntersections(bool active) {
        foreach (var road in roads) {
            if (active) road.anchorManager.ShowTerrainAnchors(false);
            road.SetActive(false, active);
        }
        foreach (var intersection in intersections) {
            intersection.SetActive(active, false);
        }
        foreach (var patch in patches) {
            if (!active) patch.SetActive(false, false);
        }
        foreach (var building in buildings) {
            if (!active) building.SetActive(false, false);
        }
        foreach (var element in customElements) {
            if (!active) element.SetActive(false, false);
            ShowCustomElementTraffic(element, false);
        }
    }

    public void ShowAllTrafficLanes() {
        foreach (var road in roads) {
            road.SetActive(true, false, false);
        }
        foreach (var intersection in intersections) {
            intersection.SetActive(true, false);
        }
        foreach (var element in customElements) {
            ShowCustomElementTraffic(element, true);
        }
    }

    //the traffic lines of the custom elements are shown and hidden with the lanes of the roads
    void ShowCustomElementTraffic(CustomElement element, bool show) {
        if (!IsDestroyed(element) && !element.IsDeleted()) element.generator.ShowTrafficLines(show);
    }

    //sets a view toggle of the core (see Rendering.ViewToggles) and shows or hides the lines it controls
    public bool SetViewToggle(string name, bool on) {
        if (!Rendering.ViewToggles.Set(name, on)) return false;
        foreach (var element in customElements) {
            if (!IsDestroyed(element) && !element.IsDeleted()) element.generator.RefreshVisibility();
        }
        return true;
    }

    //forCustomElements: also the anchors only for the custom elements; roadEnds: also the free ends of the roads (for the types with roadEndAnchors)
    public void ShowAnchors(bool active, bool forCustomElements = false, bool keepEditedElements = false, bool roadEnds = false) {
        foreach (var road in roads) {
            if (active) road.SetActive(false, false);
            road.anchorManager.ShowTerrainAnchors(active, forCustomElements, roadEnds);
        }
        foreach (var intersection in intersections) {
            if (active) intersection.SetActive(false, false);
            intersection.anchorManager.ShowTerrainAnchors(active, forCustomElements);
        }
        foreach (var patch in patches) {
            patch.SetActive(active, false);
            patch.Select(false);
        }
        foreach (var building in buildings) {
            building.SetActive(active, false);
            building.Select(false);
        }
        foreach (var element in customElements) {
            if (keepEditedElements && element.IsEditing) continue;
            if (active) ShowCustomElementTraffic(element, false);
            element.SetActive(active, false);
            element.Select(false);
        }
    }

    void PropagateCityProperties() {
        var cityProperties = GetDummy<CityProperties>();
        if (cityProperties != null && cityProperties.state.HasChanged()) {
            cityProperties.SyncSettings();
            foreach (var key in cityProperties.injectableParamMap.Keys) {
                if (cityProperties.ParameterHasChanged(key)) {
                    var entry = cityProperties.injectableParamMap[key];
                    var param = entry.injection;
                    if (param.onRoads) {
                        foreach (var road in roads) {
                            cityProperties.InjectParameter(key, road.runtimeState);
                            road.state.FlagAsChanged();
                        }
                    }
                    if (param.onCustomElements) {
                        foreach (var element in customElements) {
                            cityProperties.InjectParameter(key, element.cityPropertiesState);
                            element.state.FlagAsChanged();
                        }
                    }
                }
            }
            cityProperties.state.FlagAsUnchanged();
            cityProperties.UpdateOlds();
        }
        //the roads added after the last change of the city properties (new roads, or roads recreated by undo)
        //did not get the injected parameters, e.g. their traffic lights had no model
        if (cityProperties != null && cityProperties.injectableParamMap != null) {
            foreach (var road in roads) {
                if (road.cityPropertiesInjected) continue;
                foreach (var key in cityProperties.injectableParamMap.Keys) {
                    if (cityProperties.injectableParamMap[key].injection.onRoads) cityProperties.InjectParameter(key, road.runtimeState);
                }
                road.cityPropertiesInjected = true;
                road.state.FlagAsChanged();
            }
            foreach (var element in customElements) {
                if (element.cityPropertiesInjected) continue;
                foreach (var key in cityProperties.injectableParamMap.Keys) {
                    if (cityProperties.injectableParamMap[key].injection.onCustomElements) cityProperties.InjectParameter(key, element.cityPropertiesState);
                }
                element.cityPropertiesInjected = true;
                element.state.FlagAsChanged();
            }
        }
    }

    public void FlagAsChanged(bool propsChanged = false) {
        worldChanged = true;
        visibilityChanged = true;
        this.propsChanged = propsChanged;
    }

    void CleanupList<T>(List<T> list, System.Func<T, bool> isInvalid) {
        List<T> elemsToRemove = new List<T>();
        foreach (var elem in list) {
            if (isInvalid(elem)) {
                elemsToRemove.Add(elem);
            }
        }
        foreach (var elem in elemsToRemove) {
            list.Remove(elem);
        }
    }

    //freed nodes must be checked explicitly
    static bool IsDestroyed(GameObject obj) {
        return obj == null || !GodotObject.IsInstanceValid(obj) || obj.IsQueuedForDeletion();
    }

    void Cleanup() {
        CleanupList(roads, road => { return IsDestroyed(road) || road.deleted; });
        CleanupList(intersections, intersection => { return intersection == null || IsDestroyed(intersection.geo); });
        CleanupList(patches, patch => { return IsDestroyed(patch) || patch.IsDeleted(); });
        CleanupList(meshes, mesh => { return IsDestroyed(mesh); });
        CleanupList(buildings, building => { return IsDestroyed(building); });
        CleanupList(customElements, element => { return IsDestroyed(element) || element.IsDeleted(); });
    }

    public override void _Ready() {
        ProcessPriority = 1000; //run after every other node's _Process
    }

    public override void _Process(double delta) {
        LateUpdate();
    }

    public override void _PhysicsProcess(double delta) {
        if (!splineProjectionPending || splinePhysicsReady || History.UndoManager.IsLoading) return;
        splinePhysicsReady = true;
        foreach (var element in interactiveRoadEdit ? interactiveCustomElements : customElements) element.InvalidateSplineSurface();
        worldChanged = true;
    }

    //New/replaced collision shapes enter the physics world at its next synchronization. API replies and exports
    //wait for that step so their evaluated spline geometry agrees with the editor's following frame.
    public async System.Threading.Tasks.Task SettleProjectedSplines() {
        if (interactiveRoadEdit) {
            EndInteractiveRoadEdit();
            ProcessUpdate();
        }
        while (splineProjectionPending && PreferencesManager.workingDirectory != "" && IsInsideTree()) {
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            ProcessUpdate();
        }
    }

    void LateUpdate() {
        if (History.UndoManager.IsLoading) return; //the loader updates explicitly when the road anchors are ready
        //Only a live gizmo drag uses the local preview. Explicit API/save/export updates always settle the city.
        if (interactiveRoadEdit && CityBuilderMenuBar.staticGizmo?.isTransforming != true) EndInteractiveRoadEdit();
        if (!ProcessInteractiveRoadEdit()) ProcessUpdate();
        History.UndoManager.FinalizePending(); //after the update, so that the recorded state includes what it derived
    }

    //The road network and its attached elements stay live during a drag. Build the attachment graph once, rather
    //than rediscovering it across the city on every mouse movement.
    internal void BeginInteractiveRoadEdit(IReadOnlyList<Node3D> targets) {
        EndInteractiveRoadEdit();
        Cleanup();
        var selectedRoads = new HashSet<Road>();
        var junctions = new HashSet<Intersection>();
        foreach (var target in targets) {
            switch (History.UndoManager.ResolveOwner(target)) {
                case Road road: selectedRoads.Add(road); break;
                case Intersection intersection: junctions.Add(intersection); break;
                default: return; //mixed selections keep the normal update path
            }
        }
        foreach (var road in selectedRoads) {
            if (road.startIntersection != null) junctions.Add(road.startIntersection);
            if (road.endIntersection != null) junctions.Add(road.endIntersection);
        }
        foreach (var junction in junctions) foreach (var road in junction.roads) selectedRoads.Add(road);
        interactiveRoads.AddRange(selectedRoads);
        interactiveIntersections.AddRange(junctions);
        CollectInteractiveDependents();
        //A pending full-city projection pass may have started before this drag. Its other targets still need the
        //synchronized release update even if the drag itself ends without moving a road.
        deferredRoadSurfacesChanged |= splineProjectionPending;
        interactiveMembership = (roads.Count, intersections.Count, patches.Count, buildings.Count, customElements.Count);
        interactiveRoadEdit = interactiveRoads.Count + interactiveIntersections.Count > 0;
    }

    void CollectInteractiveDependents() {
        var dependencies = new Dictionary<object, HashSet<object>>();
        void DependsOn(object owner, object follower) {
            if (owner == null || follower == null || owner == follower) return;
            if (!dependencies.TryGetValue(owner, out var followers)) dependencies[owner] = followers = new();
            followers.Add(follower);
        }
        var points = new HashSet<TerrainPoint>();
        void Owns(object owner, IEnumerable<TerrainPoint> controls, bool canMoveOtherPoints = false) {
            foreach (var point in controls) {
                if (point == null || point.IsDeleted()) continue;
                points.Add(point);
                DependsOn(point, owner);
                //Only constraints/derived points can spread a change to the element's other points. Traversing all
                //points of every patch would otherwise pull the entire shared terrain network into the preview.
                if (canMoveOtherPoints) DependsOn(owner, point);
            }
        }
        foreach (var patch in patches) {
            Owns(patch, patch.GetPerimeterPointsComponents());
            Owns(patch, patch.GetInternalPointsComponents());
            foreach (var border in patch.GetTerrainBorderMeshes()) Owns(patch, border.segment);
        }
        foreach (var line in buildings) Owns(line, line.GetPointsComponents());
        foreach (var element in customElements) {
            var type = element.GetCustomElementType();
            Owns(element, element.GetAllPoints(), type?.constraintsFunction != null || element.IsDerived);
            //Derived geometry reads the other elements replacing the same source, even without shared points.
            DependsOn(element.source, element);
            if (element.source != null) DependsOn(element, element.source);
            foreach (var key in type?.GetReferenceParameters() ?? Array.Empty<string>()) {
                var reference = element.state.State(key);
                if (reference != null) DependsOn(History.ElementRegistry.Get(reference.Int("id", -1)), element);
            }
        }
        foreach (var point in points) {
            DependsOn(point.GetObject(), point);
            foreach (var owner in point.GetAnchorOwners()) DependsOn(owner, point);
            //Column points and their masters share their horizontal position.
            if (point.anchor is ColumnAnchor column) DependsOn(point, column.master);
        }
        var pending = new Queue<object>();
        foreach (var road in interactiveRoads) pending.Enqueue(road);
        foreach (var intersection in interactiveIntersections) {
            pending.Enqueue(intersection);
            pending.Enqueue(intersection.geo);
        }
        var seen = new HashSet<object>();
        while (pending.TryDequeue(out var current)) {
            if (!seen.Add(current)) continue;
            switch (current) {
                case TerrainPatch patch: interactivePatches.Add(patch); break;
                case BuildingLine line: interactiveBuildings.Add(line); break;
                case CustomElement element: interactiveCustomElements.Add(element); break;
                case TerrainPoint point:
                    interactivePoints.Add(point);
                    pending.Enqueue(point.GetObject());
                    break;
            }
            if (dependencies.TryGetValue(current, out var followers)) foreach (var follower in followers) pending.Enqueue(follower);
        }
    }

    internal void EndInteractiveRoadEdit() {
        if (interactiveRoadEdit) worldChanged = true;
        interactiveRoadEdit = false;
        interactiveRoads.Clear();
        interactiveIntersections.Clear();
        interactivePatches.Clear();
        interactiveBuildings.Clear();
        interactiveCustomElements.Clear();
        interactivePoints.Clear();
    }

    internal bool ProcessInteractiveRoadEdit() {
        if (!interactiveRoadEdit) return false;
        if (!worldChanged) return true;
        //A topology change or city-property edit during a drag requires the normal propagation/cleanup path.
        if (splitParts.Count > 0 || GetDummy<CityProperties>().state.HasChanged()
            || interactiveMembership != (roads.Count, intersections.Count, patches.Count, buildings.Count, customElements.Count)
            || interactiveRoads.Exists(r => IsDestroyed(r) || r.deleted)
            || interactiveIntersections.Exists(i => IsDestroyed(i.geo))
            || interactivePatches.Exists(p => IsDestroyed(p) || p.IsDeleted())
            || interactiveBuildings.Exists(b => IsDestroyed(b) || b.IsDeleted())
            || interactiveCustomElements.Exists(e => IsDestroyed(e) || e.IsDeleted())
            || interactivePoints.Exists(p => p.IsDeleted())) {
            EndInteractiveRoadEdit();
            return false;
        }
        worldChanged = false;
        var counts = UpdateRoadNetwork(interactiveRoads, interactiveIntersections);
        foreach (var point in interactivePoints) point.UpdatePosition();
        var patchesRebuilt = 0;
        foreach (var patch in interactivePatches) patchesRebuilt += patch.UpdatePatch();
        foreach (var line in interactiveBuildings) line.UpdateLine();
        if (counts.roads + counts.intersections + patchesRebuilt > 0) InvalidateInteractiveProjection();
        var customRebuilt = 0;
        for (int pass = 0; pass < MaxConstraintPasses; pass++) {
            customRebuilt += UpdateCustomElements(interactiveCustomElements);
            var followed = false;
            foreach (var point in interactivePoints) {
                if (point.IsDeleted()) continue;
                var before = point.GetPoint();
                point.UpdatePosition();
                followed |= !GeometryHelper.AreVectorsEqual(before, point.GetPoint());
            }
            if (!followed) break;
        }
        //Custom constraints/derived points can move a point shared with a patch/building, or an anchor of another
        //follower. Rebuild those owners after the constraints too, so they agree within the same preview frame.
        var constrainedPatches = 0;
        if (customRebuilt > 0) {
            foreach (var patch in interactivePatches) constrainedPatches += patch.UpdatePatch();
            foreach (var line in interactiveBuildings) line.UpdateLine();
        }
        if (constrainedPatches > 0) InvalidateInteractiveProjection();
        deferredRoadSurfacesChanged |= counts.roads + counts.intersections + patchesRebuilt + constrainedPatches + customRebuilt > 0;
        foreach (var road in interactiveRoads) road.UpdateObjects();
        foreach (var element in interactiveCustomElements) element.UpdateObjects();
        lastInteractiveDependencyVisits = (interactivePatches.Count, interactiveBuildings.Count, interactiveCustomElements.Count);
        lastRebuildCounts = (counts.roads, counts.intersections, customRebuilt);
        builder?.NotifyUpdateCompleted();
        return true;
    }

    void InvalidateInteractiveProjection() {
        foreach (var element in interactiveCustomElements) {
            if (!element.ProjectsSurface) continue;
            element.InvalidateSplineSurface();
            splineProjectionPending = true;
            splinePhysicsReady = false;
        }
    }

    (int roads, int intersections) UpdateRoadNetwork(List<Road> updatedRoads, List<Intersection> updatedIntersections, bool finishSplits = false) {
        lastRoadUpdateVisits = (updatedRoads.Count, updatedIntersections.Count);
        foreach (var road in updatedRoads) road.UpdateLine();
        foreach (var intersection in updatedIntersections) intersection.RebuildRoads(null);
        foreach (var road in updatedRoads) road.UpdateLine(); //in case an intersection was moved
        foreach (var intersection in updatedIntersections) intersection.RecalculateSize();
        if (finishSplits) FinishSplitParts();
        var roadsRebuilt = 0;
        var intersectionsRebuilt = 0;
        foreach (var road in updatedRoads) roadsRebuilt += road.UpdateMesh();
        foreach (var intersection in updatedIntersections) intersectionsRebuilt += intersection.RebuildMesh();
        return (roadsRebuilt, intersectionsRebuilt);
    }

    bool MeshProjectionSurfacesChanged() {
        nextProjectionMeshes.Clear();
        var changed = false;
        foreach (var mesh in meshes) {
            if (!mesh.IsProjectionSurface) continue;
            var state = new ProjectionMeshState(GO.GetPosition(mesh), GO.GetRotation(mesh), GO.GetGlobalScale(mesh),
                mesh.meshPath, mesh.TypeName, GO.GetEnabled(mesh));
            nextProjectionMeshes.Add(mesh, state);
            if (!projectionMeshes.TryGetValue(mesh, out var previous) || previous != state) changed = true;
        }
        changed |= nextProjectionMeshes.Count != projectionMeshes.Count;
        (projectionMeshes, nextProjectionMeshes) = (nextProjectionMeshes, projectionMeshes);
        return changed;
    }

    public void ProcessUpdate() {
        if (PreferencesManager.workingDirectory == "") return;
        if (worldChanged || visibilityChanged) {
            //a physics step between the input and _Process flushes the delete queue: what the UI deleted can be already freed
            Cleanup();
            Operations.PropertyContainers.Sync(this);
            Operations.PropertyContainers.ApplyVisibility();
        }
        if (visibilityChanged) {
            visibilityChanged = false;
            foreach (var road in roads) {
                road.SyncVisibility();
            }
            GetDummy<CityProperties>().SyncVisibility();
        }
        if (!worldChanged) return;
        cityUpdateCount++;
        worldChanged = false;
        Cleanup();

        PropagateCityProperties();

        var (roadsRebuilt, intersectionsRebuilt) = UpdateRoadNetwork(roads, intersections, true);
        var patchesRebuilt = 0;
        foreach (var patch in patches) {
            patchesRebuilt += patch.UpdatePatch();
        }
        var buildingsRebuilt = 0;
        foreach (var building in buildings) {
            buildingsRebuilt += building.UpdateLine();
        }
        //Mesh type/layer changes must be applied before the projected splines read their colliders.
        var instancesUpdated = 0;
        foreach (var mesh in meshes) {
            instancesUpdated += mesh.ManualUpdate();
        }
        var meshSurfacesChanged = MeshProjectionSurfacesChanged();
        if (deferredRoadSurfacesChanged || meshSurfacesChanged || roadsRebuilt + intersectionsRebuilt + patchesRebuilt > 0 || splineTerrainVersion != GameEngine.Terrain.HeightmapVersion) {
            foreach (var element in customElements) {
                if (!element.ProjectsSurface) continue;
                element.InvalidateSplineSurface();
                splineProjectionPending = true;
                splinePhysicsReady = false;
            }
        }
        deferredRoadSurfacesChanged = false;
        splineTerrainVersion = GameEngine.Terrain.HeightmapVersion;
        var customElementsRebuilt = UpdateCustomElements();
        var objectsUpdated = 0;
        foreach (var road in roads) {
            objectsUpdated += road.UpdateObjects();
        }
        objectsUpdated += GetDummy<CityProperties>().UpdateObjects();
        foreach (var element in customElements) objectsUpdated += element.UpdateObjects();
        Operations.PropertyContainers.ApplyVisibility();
        lastRebuildCounts = (roadsRebuilt, intersectionsRebuilt, customElementsRebuilt);
        if (GameEngine.IsEditor()) GameEngine.Print(
            "rebuilt " + roadsRebuilt + " roads, "
            + intersectionsRebuilt + " intersections, "
            + patchesRebuilt + " patches, "
            + buildingsRebuilt + " building sides, "
            + customElementsRebuilt + " custom elements, "
            + instancesUpdated + " meshes, "
            + objectsUpdated + " parametric objects"
        );
        if (builder != null) builder.NotifyUpdateCompleted();
    }

    //the points follow their anchors, then the constraints of the elements run (a few passes, since moving a point shared
    //with another element can make the constraints of that one move other points), then the derived points are computed
    //from the points as the constraints left them (and the constraints run again if they changed), then the geometry is
    //rebuilt
    const int MaxConstraintPasses = 4;

    int UpdateCustomElements(List<CustomElement> elements = null) {
        var full = elements == null;
        elements ??= customElements;
        foreach (var element in elements) element.BeginUpdate();
        try {
            return UpdateCustomElementGeometry(elements, full);
        } finally {
            foreach (var element in elements) element.EndUpdate();
        }
    }

    int UpdateCustomElementGeometry(List<CustomElement> elements, bool full) {
        MoveByHandles(elements);
        foreach (var element in elements) element.RefreshPoints();
        //Check actual inputs before starting the constraint/derived/geometry passes. Membership changes
        //and undo also invalidate sibling inputs even when the surviving elements didn't move.
        var changed = full && (customElementAppliedVersion != History.UndoManager.AppliedVersion
            || updatedCustomElements.Count != elements.Count);
        for (int i = 0; i < elements.Count && !changed; i++) {
            changed = (full && updatedCustomElements[i] != elements[i]) || elements[i].NeedsUpdate();
        }
        if (!changed) {
            if (splinePhysicsReady) splineProjectionPending = false; //the projection of the point elements moved nothing
            PlaceMoveHandles(elements);
            return 0;
        }
        ApplyCustomElementConstraints(elements);
        var derived = false;
        var sources = new Dictionary<object, List<CustomElement>>();
        foreach (var element in elements) {
            if (element.source == null) continue;
            if (!sources.TryGetValue(element.source, out var siblings)) sources[element.source] = siblings = new List<CustomElement>();
            siblings.Add(element);
        }
        foreach (var element in elements) {
            var siblings = element.source != null ? sources[element.source] : null;
            if (element.UpdateDerivedPoints(siblings ?? elements)) derived = true;
        }
        if (derived) ApplyCustomElementConstraints(elements);
        //Projected splines read generated colliders: rebuild those surfaces first, regardless of creation order.
        var surfaces = new List<CustomElement>();
        var projected = new List<CustomElement>();
        foreach (var element in elements) {
            if (element.ProjectsSurface) projected.Add(element);
            else surfaces.Add(element);
        }
        var rebuilt = CustomElementGeometryBatch.Rebuild(surfaces);
        if (rebuilt > 0 && projected.Count > 0) {
            foreach (var element in projected) element.InvalidateSplineSurface();
            splineProjectionPending = true;
            splinePhysicsReady = false;
        }
        var projectedRebuilt = CustomElementGeometryBatch.Rebuild(projected);
        //A newly created/loaded spline can follow meshes installed earlier in this same frame, before it existed.
        //It also needs one synchronized pass even if those surfaces no longer report a change.
        if (projectedRebuilt > 0 && !splineProjectionPending) {
            splineProjectionPending = true;
            splinePhysicsReady = false;
        }
        rebuilt += projectedRebuilt;
        if (splinePhysicsReady || projected.Count == 0) splineProjectionPending = false;
        if (full) UpdateCustomElementPlaceholders();
        PlaceMoveHandles(elements);
        if (full) {
            updatedCustomElements.Clear();
            updatedCustomElements.AddRange(elements);
            foreach (var element in elements) element.RecordUpdate();
            customElementAppliedVersion = History.UndoManager.AppliedVersion;
        }
        return rebuilt;
    }

    //the containers whose move handle was moved (with the gizmo or the transform panel) move with their contents, in the
    //undo step of the drag or of the change in the panel. The other elements sharing their points (updated before them)
    //follow at the next update
    void MoveByHandles(List<CustomElement> elements) {
        foreach (var element in elements) {
            var offset = element.TakeMoveHandleOffset();
            if (!offset.HasValue) continue;
            History.UndoManager.RecordDerived(() => Operations.CustomElements.Move(this, element, offset.Value));
            worldChanged = true;
        }
    }

    //the move handles go to the center of their container and contents, unless selected (e.g. being dragged)
    void PlaceMoveHandles(List<CustomElement> elements) {
        foreach (var element in elements) {
            var handle = element.GetMoveHandle();
            if (handle == null || GameEngine.Gizmo.IsTarget(handle)) continue;
            var center = Operations.CustomElements.CenterWithContents(this, element);
            if (center.HasValue) element.PlaceMoveHandle(center.Value);
        }
    }

    //the placeholder meshes of the containers are shown while none of their contents draws a mesh
    void UpdateCustomElementPlaceholders() {
        var drawn = new HashSet<CustomElement>();
        foreach (var element in customElements) {
            if (element.container != null && !element.IsDeleted() && element.generator.DrawsMesh()) drawn.Add(element.container);
        }
        foreach (var element in customElements) {
            if (!element.IsDeleted()) element.generator.ShowPlaceholder(element.generator.HasPlaceholder() && !drawn.Contains(element));
        }
    }

    void ApplyCustomElementConstraints(List<CustomElement> elements) {
        for (int pass = 0; pass < MaxConstraintPasses; pass++) {
            var moved = false;
            foreach (var element in elements) {
                if (element.ApplyConstraints()) moved = true;
            }
            if (!moved) break;
        }
    }

    public void CloseCity() {
        PreferencesManager.workingDirectory = "";
        PreferencesManager.Unload();
        CoreManager.Reset();
        EraseCity();
        CityElements.Types.Parsers.TypeParser.Reset();
    }

    public void EraseCity() {
        EndInteractiveRoadEdit();
        deferredRoadSurfacesChanged = false;
        History.UndoManager.Suppress(true);
        MaterialManager.ClearCache();
        PresetManager.loaded = false;

        ArrayObject.DeselectAll();
        builder.NotifyVisibilityChange();

        foreach (var elem in meshes) {
            elem.Delete();
        }
        meshes.Clear();
        projectionMeshes.Clear();
        nextProjectionMeshes.Clear();
        splineProjectionPending = splinePhysicsReady = false;

        foreach (var elem in roads) {
            elem.Delete();
        }
        roads.Clear();

        foreach (var elem in intersections) {
            elem.Delete();
        }
        intersections.Clear();

        foreach (var elem in patches) {
            elem.Delete();
        }
        patches.Clear();

        foreach (var elem in buildings) {
            elem.Delete();
        }
        buildings.Clear();

        foreach (var elem in customElements) {
            elem.Delete();
        }
        customElements.Clear();
        updatedCustomElements.Clear();
        customElementAppliedVersion = -1;

        GetDummy<CityProperties>().Delete();

        MaterialManager.GetInstance().UnloadAll();
        MeshManager.GetInstance().UnloadAll();
        PythonManager.StopServer();
        History.UndoManager.Clear();
        History.ElementRegistry.Reset();
        History.UndoManager.Suppress(false);
    }

    public void EraseCityTerrain() {
        foreach (var patch in patches) {
            patch.Delete();
        }
        patches.Clear();
    }

    public void EraseCityBuildings() {
        foreach (var line in buildings) {
            line.Delete();
        }
        buildings.Clear();
    }

    public (List<TerrainAnchor> anchors, List<TerrainAnchor> doneAnchors) InitRoadAnchorsListForCitywideGeneration<T>(List<T> list, Func<T, List<TerrainPoint>> pointGetter) {
        //TODO maybe: move somewhere else?
        var anchors = new List<TerrainAnchor>();
        var doneAnchors = new List<TerrainAnchor>();
        foreach (var anchor in Operations.Anchors.GetAll(this)) {
            if (anchor.customElementsOnly) continue;
            var notUsed = true;
            foreach (var elem in list) {
                foreach (var point in pointGetter.Invoke(elem)) {
                    if (anchor.Equals(point.anchor)) {
                        notUsed = false;
                        break;
                    }
                }
                if (!notUsed) break;
            }
            if (notUsed) anchors.Add(anchor);
            else doneAnchors.Add(anchor);
        }
        return (anchors, doneAnchors);
    }

    public System.Threading.Tasks.Task CreateCityTerrain(float distance, float segmentLength, float vertexFusionDistance, int smooth, float internalDistance,
        IProgress<(float, string)> progress = null) {

        var ctg = new CitywideTerrainGenerator();
        return ctg.CreateCityTerrainCoroutine(distance, segmentLength, vertexFusionDistance, smooth, internalDistance, this, this, progress);
    }

    public System.Threading.Tasks.Task CreateCityBuildings(float minLength, float maxLength, List<ObjectState> states,
        bool subdivide, ObjectState lineState, List<string> texturesToPlaceOn, IProgress<(float, string)> progress = null) {

        var ctg = new CitywideBuildingGenerator();
        return ctg.CreateCityBuildingsCoroutine(minLength, maxLength, states, subdivide, this, lineState, texturesToPlaceOn, this, progress);
    }
}
