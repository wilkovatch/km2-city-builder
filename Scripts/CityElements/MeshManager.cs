using System.Collections;
using System.Collections.Generic;
using System.IO;
using Godot;
using Scriptable = Godot.Node3D;
using GameObject = Godot.Node3D;
using Bounds = Godot.Aabb;
using GO = GameEngine.GameObject;


public class MeshManager {
    static MeshManager instance = null;

    GameObject parent;

    Dictionary<string, GameObject> meshes = new Dictionary<string, GameObject>();
    Dictionary<string, Bounds> bounds = new Dictionary<string, Bounds>();

    public static GameObject GetMesh(string name, GameObject parent) {
        return GetInstance().GetMesh_instance(name, parent);
    }

    MeshManager() {
        parent = GO.Create("MeshPool");
        GO.SetEnabled(parent, false);
    }

    public (string, Bounds, GameObject)[] GetAllMeshes() {
        var res = new (string, Bounds, GameObject)[meshes.Count];
        int i = 0;
        foreach (var key in meshes.Keys) {
            res[i] = (key, bounds[key], meshes[key]);
            i++;
        }
        return res;
    }

    public static MeshManager GetInstance() {
        if (instance == null) instance = new MeshManager();
        return instance;
    }

    public static Bounds GetBounds(string mesh) {
        return GetInstance().GetBounds_instance(mesh);
    }

    // The pool is an orphan node (outside the scene tree, so the
    // loaded templates never get ready/process callbacks), which must be freed explicitly or it leaks at exit
    public static void Shutdown() {
        if (instance == null) return;
        GO.Destroy(instance.parent);
        instance = null;
    }

    public void UnloadAll() {
        foreach(var elem in meshes) {
            GO.Destroy(elem.Value);
        }
        meshes.Clear();
        bounds.Clear();
    }

    Bounds GetBounds_instance(string mesh) {
        if (mesh == null || !bounds.ContainsKey(mesh)) return new Bounds();
        return bounds[mesh];
    }

    GameObject GetMesh_instance(string name, GameObject parent) {
        GameObject res = null;
        if (name == null) return res;
        if (meshes.TryGetValue(name, out res)) {
            return res != null ? GO.Instantiate(res, parent) : null;
        } else {
            res = MeshImporter.LoadMesh(name, this.parent);
            meshes.Add(name, res);
            try {
                bounds.Add(name, GameEngine.Bounds.GetFromMesh(GameEngine.Scriptable.FindMeshRecursive(res)));
            } catch (System.Exception) {

            }
            return res != null ? GO.Instantiate(res, parent) : null;
        }
    }
}
