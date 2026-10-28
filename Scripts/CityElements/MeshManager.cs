using System.Collections;
using System.Collections.Generic;
using System.IO;
using Godot;
using Scriptable = Godot.Node3D;
using GameObject = Godot.Node3D;
using Bounds = Godot.Aabb;
using GO = GameEngine.GameObject;


public class MeshManager {
    //the transform of a placed mesh from its placement point, in program coordinates
    public struct MeshTransform {
        public Vector3 position;
        public Quaternion rotation;
        public Vector3 scale;

        public static readonly MeshTransform identity = new MeshTransform { position = Vector3.Zero, rotation = Quaternion.Identity, scale = Vector3.One };

        public bool IsIdentity() {
            return position == Vector3.Zero && rotation == Quaternion.Identity && scale == Vector3.One;
        }

        //applies it to the local transform of obj (the mesh under the placed object), or removes it
        public void ApplyTo(GameObject obj, bool inverse = false) {
            if (!IsIdentity()) GO.PreTransformLocal(obj, position, rotation, scale, inverse);
        }
    }

    static MeshManager instance = null;

    GameObject parent;

    Dictionary<string, GameObject> meshes = new Dictionary<string, GameObject>();
    Dictionary<string, Bounds> bounds = new Dictionary<string, Bounds>();
    Dictionary<string, MeshTransform?> coreTransforms = new Dictionary<string, MeshTransform?>();
    HashSet<string> coreTransformWarnings = new HashSet<string>();

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
            res[i] = (key, GetBounds_instance(key), meshes[key]); //no bounds if they could not be computed
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

    //the transform of a mesh from its placement point: the one given by the core (server/mesh_transforms.py) if useCoreTransform
    //and the core gives one (e.g. the MM2 props, drawn from their centre of gravity), otherwise, if autoYOffset, a vertical offset
    //that puts the lowest point of the mesh on the placement point
    public static MeshTransform GetPlacementTransform(string mesh, bool useCoreTransform, bool autoYOffset) {
        if (useCoreTransform) {
            var coreTransform = GetInstance().GetCoreTransform_instance(mesh);
            if (coreTransform.HasValue) return coreTransform.Value;
        }
        var res = MeshTransform.identity;
        if (autoYOffset) res.position = GameEngine.Vector3.up * -GameEngine.Bounds.GetMin(GetBounds(mesh))[1];
        return res;
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
        coreTransforms.Clear();
        coreTransformWarnings.Clear();
    }

    Bounds GetBounds_instance(string mesh) {
        if (mesh == null || !bounds.ContainsKey(mesh)) return new Bounds();
        return bounds[mesh];
    }

    MeshTransform? GetCoreTransform_instance(string mesh) {
        if (mesh == null) return null;
        if (coreTransforms.TryGetValue(mesh, out var res)) return res;
        string warning;
        (res, warning) = MeshImporter.LoadTransform(mesh);
        coreTransforms[mesh] = res;
        //the same warning may be given for many meshes (e.g. a missing folder of the game files)
        if (!string.IsNullOrEmpty(warning) && coreTransformWarnings.Add(warning)) GameEngine.LogWarning(warning);
        return res;
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
