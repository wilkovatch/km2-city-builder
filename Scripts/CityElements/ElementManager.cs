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
    public List<LowLevelElement> lowLevelElements = new List<LowLevelElement>();
    public bool worldChanged = false;
    public bool propsChanged = false;
    public bool visibilityChanged = false;
    GameObject terrainPointContainer = null;
    GameObject terrainContainer = null;
    GameObject roadContainer = null;
    GameObject meshContainer = null;
    GameObject buildingContainer = null;
    GameObject lowLevelContainer = null;
    GameObject dummyContainer = null;
    GameObject activeDummyContainer = null;
    Dictionary<System.Type, GameObject> dummies = new Dictionary<System.Type, GameObject>();
    public CityBuilderMenuBar builder;
    //the parts of the roads split by the last action, finished at the next update (see RoadSplitter.FinishPart)
    readonly Dictionary<Road, RoadSplitter.Part> splitParts = new Dictionary<Road, RoadSplitter.Part>();
    readonly List<LowLevelElement> updatedLowLevelElements = new List<LowLevelElement>();
    int lowLevelAppliedVersion = -1;
    internal (int roads, int intersections, int lowLevel) lastRebuildCounts;

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

    public GameObject GetLowLevelContainer() {
        return GetObject("LowLevel", ref lowLevelContainer);
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

    public void AddLowLevelElement(LowLevelElement element, int id = -1) {
        lowLevelElements.Add(element);
        History.ElementRegistry.Register(element, id);
    }

    //a new low-level element of the type, without points (named after its type, numbered after the existing ones)
    public LowLevelElement CreateLowLevelElement(string type, string name = null) {
        var container = GetLowLevelContainer();
        if (name == null) {
            var baseName = Operations.LowLevel.GetTypeLabel(type) + " ";
            name = baseName + Actions.Helpers.GetLatestObjectNumber(container, baseName);
        }
        LowLevelElement element = type == PropPath.TypeId ? GS.Create<PropPath>(name, container) : GS.Create<LowLevelElement>(name, container);
        element.Initialize(type, GetTerrainPointContainer());
        AddLowLevelElement(element);
        return element;
    }

    //the low-level elements in the container
    public List<LowLevelElement> GetLowLevelChildren(LowLevelElement container) {
        var res = new List<LowLevelElement>();
        foreach (var element in lowLevelElements) {
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
        foreach (var element in lowLevelElements) {
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
            res.Add(GO.GetObject(mesh));
        }
        foreach (var building in buildings) {
            res.Add(GO.GetObject(building));
        }
        foreach (var element in lowLevelElements) {
            res.Add(element);
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
        foreach (var element in lowLevelElements) {
            if (!active) element.SetActive(false, false);
            ShowLowLevelTraffic(element, false);
        }
    }

    public void ShowAllTrafficLanes() {
        foreach (var road in roads) {
            road.SetActive(true, true);
        }
        foreach (var intersection in intersections) {
            intersection.SetActive(true, false);
        }
        foreach (var element in lowLevelElements) {
            ShowLowLevelTraffic(element, true);
        }
    }

    //the traffic lines of the low-level elements are shown and hidden with the lanes of the roads
    void ShowLowLevelTraffic(LowLevelElement element, bool show) {
        if (!IsDestroyed(element) && !element.IsDeleted()) element.generator.ShowTrafficLines(show);
    }

    //sets a view toggle of the core (see Rendering.ViewToggles) and shows or hides the lines it controls
    public bool SetViewToggle(string name, bool on) {
        if (!Rendering.ViewToggles.Set(name, on)) return false;
        foreach (var element in lowLevelElements) {
            if (!IsDestroyed(element) && !element.IsDeleted()) element.generator.RefreshVisibility();
        }
        return true;
    }

    //lowLevel: also the anchors only for the low-level elements
    public void ShowAnchors(bool active, bool lowLevel = false) {
        foreach (var road in roads) {
            if (active) road.SetActive(false, false);
            road.anchorManager.ShowTerrainAnchors(active, lowLevel);
        }
        foreach (var intersection in intersections) {
            if (active) intersection.SetActive(false, false);
            intersection.anchorManager.ShowTerrainAnchors(active, lowLevel);
        }
        foreach (var patch in patches) {
            patch.SetActive(active, false);
            patch.Select(false);
        }
        foreach (var building in buildings) {
            building.SetActive(active, false);
            building.Select(false);
        }
        foreach (var element in lowLevelElements) {
            if (active) ShowLowLevelTraffic(element, false);
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
        CleanupList(lowLevelElements, element => { return IsDestroyed(element) || element.IsDeleted(); });
    }

    public override void _Ready() {
        ProcessPriority = 1000; //run after every other node's _Process
    }

    public override void _Process(double delta) {
        LateUpdate();
    }

    void LateUpdate() {
        if (History.UndoManager.IsLoading) return; //the loader updates explicitly when the road anchors are ready
        ProcessUpdate();
        History.UndoManager.FinalizePending(); //after the update, so that the recorded state includes what it derived
    }

    public void ProcessUpdate() {
        if (PreferencesManager.workingDirectory == "") return;
        if (visibilityChanged) {
            visibilityChanged = false;
            foreach (var road in roads) {
                road.SyncVisibility();
            }
            GetDummy<CityProperties>().SyncVisibility();
        }
        if (!worldChanged) return;
        worldChanged = false;
        Cleanup();

        PropagateCityProperties();

        foreach (var road in roads) {
            road.UpdateLine();
        }
        foreach (var intersection in intersections) {
            intersection.RebuildRoads(null);
        }
        foreach (var road in roads) { //in case an intersection was moved
            road.UpdateLine();
        }
        foreach (var intersection in intersections) {
            intersection.RecalculateSize();
        }
        FinishSplitParts();
        var roadsRebuilt = 0;
        var intersectionsRebuilt = 0;
        foreach (var road in roads) {
            roadsRebuilt += road.UpdateMesh();
        }
        foreach (var intersection in intersections) {
            intersectionsRebuilt += intersection.RebuildMesh();
        }
        var patchesRebuilt = 0;
        foreach (var patch in patches) {
            patchesRebuilt += patch.UpdatePatch();
        }
        var buildingsRebuilt = 0;
        foreach (var building in buildings) {
            buildingsRebuilt += building.UpdateLine();
        }
        var lowLevelRebuilt = UpdateLowLevelElements();
        var instancesUpdated = 0;
        foreach (var mesh in meshes) {
            instancesUpdated += mesh.ManualUpdate();
        }
        var objectsUpdated = 0;
        foreach (var road in roads) {
            objectsUpdated += road.UpdateObjects();
        }
        objectsUpdated += GetDummy<CityProperties>().UpdateObjects();
        lastRebuildCounts = (roadsRebuilt, intersectionsRebuilt, lowLevelRebuilt);
        if (GameEngine.IsEditor()) GameEngine.Print(
            "rebuilt " + roadsRebuilt + " roads, "
            + intersectionsRebuilt + " intersections, "
            + patchesRebuilt + " patches, "
            + buildingsRebuilt + " building sides, "
            + lowLevelRebuilt + " low-level elements, "
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

    int UpdateLowLevelElements() {
        foreach (var element in lowLevelElements) element.BeginUpdate();
        try {
            return UpdateLowLevelGeometry();
        } finally {
            foreach (var element in lowLevelElements) element.EndUpdate();
        }
    }

    int UpdateLowLevelGeometry() {
        MoveByHandles();
        foreach (var element in lowLevelElements) element.RefreshPoints();
        //Check actual inputs before starting the constraint/derived/geometry passes. Membership changes
        //and undo also invalidate sibling inputs even when the surviving elements didn't move.
        var changed = lowLevelAppliedVersion != History.UndoManager.AppliedVersion
            || updatedLowLevelElements.Count != lowLevelElements.Count;
        for (int i = 0; i < lowLevelElements.Count && !changed; i++) {
            changed = updatedLowLevelElements[i] != lowLevelElements[i] || lowLevelElements[i].NeedsUpdate();
        }
        if (!changed) {
            PlaceMoveHandles();
            return 0;
        }
        ApplyLowLevelConstraints();
        var derived = false;
        var sources = new Dictionary<object, List<LowLevelElement>>();
        foreach (var element in lowLevelElements) {
            if (element.source == null) continue;
            if (!sources.TryGetValue(element.source, out var siblings)) sources[element.source] = siblings = new List<LowLevelElement>();
            siblings.Add(element);
        }
        foreach (var element in lowLevelElements) {
            var siblings = element.source != null ? sources[element.source] : null;
            if (element.UpdateDerivedPoints(siblings ?? lowLevelElements)) derived = true;
        }
        if (derived) ApplyLowLevelConstraints();
        var rebuilt = LowLevelGeometryBatch.Rebuild(lowLevelElements);
        UpdateLowLevelPlaceholders();
        PlaceMoveHandles();
        updatedLowLevelElements.Clear();
        updatedLowLevelElements.AddRange(lowLevelElements);
        foreach (var element in lowLevelElements) element.RecordUpdate();
        lowLevelAppliedVersion = History.UndoManager.AppliedVersion;
        return rebuilt;
    }

    //the containers whose move handle was moved (with the gizmo or the transform panel) move with their contents, in the
    //undo step of the drag or of the change in the panel. The other elements sharing their points (updated before them)
    //follow at the next update
    void MoveByHandles() {
        foreach (var element in lowLevelElements) {
            var offset = element.TakeMoveHandleOffset();
            if (!offset.HasValue) continue;
            History.UndoManager.RecordDerived(() => Operations.LowLevel.Move(this, element, offset.Value));
            worldChanged = true;
        }
    }

    //the move handles go to the center of their container and contents, unless selected (e.g. being dragged)
    void PlaceMoveHandles() {
        foreach (var element in lowLevelElements) {
            var handle = element.GetMoveHandle();
            if (handle == null || GameEngine.Gizmo.IsTarget(handle)) continue;
            var center = Operations.LowLevel.CenterWithContents(this, element);
            if (center.HasValue) element.PlaceMoveHandle(center.Value);
        }
    }

    //the placeholder meshes of the containers are shown while none of their contents draws a mesh
    void UpdateLowLevelPlaceholders() {
        var drawn = new HashSet<LowLevelElement>();
        foreach (var element in lowLevelElements) {
            if (element.container != null && !element.IsDeleted() && element.generator.DrawsMesh()) drawn.Add(element.container);
        }
        foreach (var element in lowLevelElements) {
            if (!element.IsDeleted()) element.generator.ShowPlaceholder(element.generator.HasPlaceholder() && !drawn.Contains(element));
        }
    }

    void ApplyLowLevelConstraints() {
        for (int pass = 0; pass < MaxConstraintPasses; pass++) {
            var moved = false;
            foreach (var element in lowLevelElements) {
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
        History.UndoManager.Suppress(true);
        MaterialManager.ClearCache();
        PresetManager.loaded = false;

        ArrayObject.DeselectAll();
        builder.NotifyVisibilityChange();

        foreach (var elem in meshes) {
            elem.Delete();
        }
        meshes.Clear();

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

        foreach (var elem in lowLevelElements) {
            elem.Delete();
        }
        lowLevelElements.Clear();
        updatedLowLevelElements.Clear();
        lowLevelAppliedVersion = -1;

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
            if (anchor.lowLevelOnly) continue;
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
