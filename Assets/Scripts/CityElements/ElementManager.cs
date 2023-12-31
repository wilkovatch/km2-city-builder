using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using SM = StringManager;

public class ElementManager : MonoBehaviour {
    public List<Road> roads = new List<Road>();
    public List<Intersection> intersections = new List<Intersection>();
    public List<TerrainPatch> patches = new List<TerrainPatch>();
    public List<MeshInstance> meshes = new List<MeshInstance>();
    public List<BuildingLine> buildings = new List<BuildingLine>();
    public bool worldChanged = false;
    public bool propsChanged = false;
    GameObject terrainPointContainer = null;
    GameObject terrainContainer = null;
    GameObject roadContainer = null;
    GameObject meshContainer = null;
    GameObject buildingContainer = null;
    GameObject dummyContainer = null;
    Dictionary<System.Type, GameObject> dummies = new Dictionary<System.Type, GameObject>();
    public CityBuilderMenuBar builder;
    float lastGCTime = 0.0f;

    public GameObject GetDummyContainer() {
        var obj = GetObject("Dummies", ref dummyContainer);
        if (obj.activeSelf) obj.SetActive(false);
        return obj;
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

    bool IsDummyPossible<T>() {
        var t = typeof(T);
        if (t == typeof(Road)) {
            return true;
        } else if (t == typeof(BuildingLine)) {
            var preset = PresetManager.GetPreset("buildingLine", 0);
            return preset.Str("type", null) != null;
        }
        return false;
    }

    public T GetDummy<T>() where T : MonoBehaviour {
        if (!IsDummyPossible<T>()) return null;
        GameObject dummy;
        if (dummies.ContainsKey(typeof(T)) && dummies[typeof(T)] != null) {
            dummy = dummies[typeof(T)];
        } else {
            dummy = new GameObject("dummy");
            dummy.transform.parent = GetDummyContainer().transform;
            var c = dummy.AddComponent<T>();
            dummies[typeof(T)] = dummy;
            if (c is Road r) {
                r.Initialize();
            } else if (c is BuildingLine bl) {
                bl.Initialize(dummy);
                bl.AddPoint(null, new Vector3(0, 0, 0), true);
                bl.AddPoint(null, new Vector3(1, 0, 0), true);
                bl.UpdateLine();
            }
        }
        var res = dummy.GetComponent<T>();
        return res;
    }

    public Intersection GetDummyIntersection() {
        GameObject dummy;
        Intersection intersection;
        if (dummies.ContainsKey(typeof(Intersection)) && dummies[typeof(Intersection)] != null) {
            dummy = dummies[typeof(Intersection)];
            intersection = dummy.GetComponent<Intersection.IntersectionComponent>().intersection;
        } else {
            dummy = new GameObject("dummy");
            intersection = new Intersection(dummy, null);
            dummies[typeof(Intersection)] = dummy;
            dummy.transform.parent = GetDummyContainer().transform;
        }
        return intersection;
    }

    GameObject GetObject(string name, ref GameObject obj) {
        if (obj == null) {
            obj = new GameObject(name);
            obj.transform.parent = transform;
        }
        return obj;
    }

    public List<GameObject> GetObjectList() {
        var res = new List<GameObject>();
        foreach (var road in roads) {
            res.Add(road.gameObject);
        }
        foreach (var intersection in intersections) {
            res.Add(intersection.geo);
        }
        foreach (var patch in patches) {
            res.Add(patch.gameObject);
        }
        foreach (var mesh in meshes) {
            res.Add(mesh.gameObject);
        }
        foreach (var building in buildings) {
            res.Add(building.gameObject);
        }
        res.Sort((x, y) => Actions.Helpers.PadInts(x.name).CompareTo(Actions.Helpers.PadInts(y.name)));
        return res;
    }

    public void DeselectMeshes() {
        foreach (var mesh in meshes) {
            mesh.SetMoveable(false);
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
    }

    public void ShowAllTrafficLanes() {
        foreach (var road in roads) {
            road.SetActive(true, true);
        }
        foreach (var intersection in intersections) {
            intersection.SetActive(true, false);
        }
    }

    public void ShowAnchors(bool active) {
        foreach (var road in roads) {
            if (active) road.SetActive(false, false);
            road.anchorManager.ShowTerrainAnchors(active);
        }
        foreach (var intersection in intersections) {
            if (active) intersection.SetActive(false, false);
            intersection.anchorManager.ShowTerrainAnchors(active);
        }
        foreach (var patch in patches) {
            patch.SetActive(active, false);
            patch.Select(false);
        }
        foreach (var building in buildings) {
            building.SetActive(active, false);
            building.Select(false);
        }
    }

    public void FlagAsChanged(bool propsChanged = false) {
        worldChanged = true;
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

    void Cleanup() {
        CleanupList(roads, road => { return road == null || road.gameObject == null || road.deleted; });
        CleanupList(intersections, intersection => { return intersection == null || intersection.geo == null; });
        CleanupList(patches, patch => { return patch == null || patch.gameObject == null || patch.IsDeleted(); });
        CleanupList(meshes, mesh => { return mesh == null || mesh.gameObject == null; });
        CleanupList(buildings, building => { return building == null || building.gameObject == null; });
    }

    void LateUpdate() {
        ProcessUpdate();
    }

    public void ProcessUpdate() {
        if (!worldChanged || PreferencesManager.workingDirectory == "") return;
        worldChanged = false;
        Cleanup();

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
        var instancesUpdated = 0;
        foreach (var mesh in meshes) {
            instancesUpdated += mesh.ManualUpdate();
        }
        if (Application.isEditor) print(
            "rebuilt " + roadsRebuilt + " roads, "
            + intersectionsRebuilt + " intersections, "
            + patchesRebuilt + " patches, "
            + buildingsRebuilt + " building sides, "
            + instancesUpdated + " meshes"
        );
        if (builder != null) builder.NotifyUpdateCompleted();
        if (Time.time > lastGCTime + 10.0f) {
            lastGCTime = Time.time;
            System.GC.Collect();
        }
    }

    public void CloseCity() {
        PreferencesManager.workingDirectory = "";
        CoreManager.Reset();
        EraseCity();
    }

    public void EraseCity() {
        MaterialManager.ClearCache();
        PresetManager.loaded = false;
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

        MaterialManager.GetInstance().UnloadAll();
        MeshManager.GetInstance().UnloadAll();
        PythonManager.StopServer();
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

    public void CreateCityTerrain(GameObject parent, float distance, float segmentLength, float vertexFusionDistance, int smooth, float internalDistance) {
        var ctg = new CitywideTerrainGenerator();
        StartCoroutine(ctg.CreateCityTerrainCoroutine(parent, distance, segmentLength, vertexFusionDistance, smooth, internalDistance, this));
    }

    public void CreateCityBuildings(GameObject parent, float minLength, float maxLength, List<ObjectState> states,
        bool subdivide, ObjectState lineState, List<string> texturesToPlaceOn) {

        var ctg = new CitywideBuildingGenerator();
        StartCoroutine(ctg.CreateCityBuildingsCoroutine(parent, minLength, maxLength, states, subdivide, this, lineState, texturesToPlaceOn));
    }
}
