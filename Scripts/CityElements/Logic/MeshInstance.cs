using System.Collections;
using System.Collections.Generic;
using Godot;
using Scriptable = Godot.Node3D;
using PhysicsScriptable = Godot.StaticBody3D;
using GameObject = Godot.Node3D;
using PhysicsGameObject = Godot.Node3D;
using GO = GameEngine.GameObject;
using GS = GameEngine.Scriptable;

public partial class MeshInstance : PhysicsScriptable
{
    public string meshPath;
    public ObjectState settings = null;
    public CityElements.Types.Parameter objectParameter = null;
    bool initialized = false;
    MeshManager.MeshTransform meshTransform = MeshManager.MeshTransform.identity; //of the mesh from the placement point

    //the mesh instance type of a plain instance, in its settings ("type")
    public string TypeName => objectParameter == null ? settings?.Str("type", null) : null;

    //settings.type must be a mesh instance type of the core: a missing or unknown one (e.g. a city saved before the
    //types, or with another core) is replaced by the first type, so that the instance is kept
    static void EnsureType(ObjectState settings, string name) {
        var types = CityElements.Types.Parsers.TypeParser.GetMeshInstanceTypes();
        var type = settings.Str("type", null);
        if (type != null && types.ContainsKey(type)) return;
        if (types.Count == 0) {
            GameEngine.LogWarning("The core has no mesh instance types: " + (name ?? "the new mesh instance") + " has no placement rules");
            return;
        }
        var keys = new List<string>(types.Keys);
        keys.Sort(System.StringComparer.Ordinal);
        GameEngine.LogWarning("Unknown mesh instance type '" + type + "'" + (name != null ? " of " + name : "") + ", using '" + keys[0] + "'");
        settings.SetStr("type", keys[0]);
    }

    public static MeshInstance Create(string meshPath, Vector3 curPoint, GameObject container, ObjectState settings, string name = null) {
        MeshInstance mesh = GS.Create<MeshInstance>(name, container);
        var obj = GO.GetObject(mesh);
        var subObj = MeshManager.GetMesh(meshPath, container);
        GO.SetParent(subObj, obj);
        GO.SetPosition(mesh, curPoint);
        mesh.meshPath = meshPath;
        mesh.settings = (ObjectState)settings.Clone();
        EnsureType(mesh.settings, name);
        if (!string.IsNullOrEmpty(name)) return mesh; //loading: the saved name needs no search through the container
        var prefix = mesh.GetMeshInstanceType()?.Settings.namePrefix ?? "Mesh";
        GO.SetName(obj, prefix + " " + Actions.Helpers.GetLatestObjectNumber(container, prefix + " ") + " (" + meshPath + ")");
        return mesh;
    }

    MeshInstance Clone(string meshPath) {
        MeshInstance mesh = GS.Create<MeshInstance>(null, GO.GetParent(this));
        var obj = GO.GetObject(mesh);
        var subObj = MeshManager.GetMesh(meshPath, GO.GetParent(this));
        GO.SetParent(subObj, obj);
        GO.SetPosition(mesh, GO.GetPosition(this));
        GO.SetRotation(mesh, GO.GetRotation(this));
        GO.SetLocalScale(mesh, GO.GetLocalScale(this));    
        mesh.meshPath = meshPath;
        mesh.settings = (ObjectState)settings.Clone();
        GO.SetName(obj, GO.GetName(this));
        return mesh;
    }

    public static MeshInstance CreateParametric(string meshPath, GameObject container, bool createContainer, string name, CityElements.Types.Parameter objectParameter, ObjectState settings) {
        GameObject parentObj;
        if (createContainer) {
            parentObj = GO.Create(name + " (container)");
            GO.SetParent(parentObj, container);
        }
        else {
            parentObj = container;
        }
        MeshInstance mesh = GS.Create<MeshInstance>(null, parentObj);
        var obj = GO.GetObject(mesh);
        var subObj = MeshManager.GetMesh(meshPath, parentObj);
        if (subObj == null) subObj = MeshManager.GetMesh("", parentObj); //fallback
        GO.SetParent(subObj, obj);
        mesh.meshPath = meshPath;
        mesh.settings = settings; //not a clone since it's tied to the parent
        mesh.settings.SetStr("meshPath", meshPath);
        mesh.objectParameter = objectParameter;
        GO.SetName(obj, name);

        //custom additive values
        GO.SetLocalPosition(mesh, settings.Vector3("localPosition"));
        GO.SetLocalRotation(mesh, settings.Vector3("localRotation"));
        GO.SetLocalScale(mesh, settings.Vector3("localScale", GameEngine.Vector3.one));

        return mesh;
    }

    public void SyncParametricObjectState(ObjectState newSettings = null) {
        if (objectParameter != null) {
            if (newSettings != null) {
                settings = newSettings;
                GO.SetLocalPosition(this, settings.Vector3("localPosition", GO.GetLocalPosition(this)));
                GO.SetLocalRotation(this, settings.Vector3("localRotation", GO.GetLocalRotation(this)));
                GO.SetLocalScale(this, settings.Vector3("localScale", GO.GetLocalScale(this)));
            }
            else {
                if (!GeometryHelper.AreVectorsEqual(settings.Vector3("localPosition"), GO.GetLocalPosition(this))) {
                    settings.SetVector3("localPosition", GO.GetLocalPosition(this), false);
                }
                if (!GeometryHelper.AreVectorsEqual(settings.Vector3("localRotation"), GO.GetLocalRotation(this))) {
                    settings.SetVector3("localRotation", GO.GetLocalRotation(this), false);
                }
                if (!GeometryHelper.AreVectorsEqual(settings.Vector3("localScale"), GO.GetLocalScale(this))) {
                    settings.SetVector3("localScale", GO.GetLocalScale(this), false);
                }
            }
        }
    }

    //the mesh instance type of a plain instance (null for the parametric objects, or a core without mesh instance types)
    public CityElements.Types.Runtime.MeshInstanceType GetMeshInstanceType() {
        var type = TypeName;
        if (type == null) return null;
        return CityElements.Types.Parsers.TypeParser.GetMeshInstanceTypes().TryGetValue(type, out var t) ? t : null;
    }

    //the placement rules (of the mesh instance type, or of the parametric object type), with the variables of the settings
    public CityElements.Types.Runtime.IMeshPlacementType GetPlacementType() {
        CityElements.Types.Runtime.IMeshPlacementType res;
        if (objectParameter != null) {
            res = CityElements.Types.Parsers.TypeParser.GetParametricMeshInstanceSettings()[objectParameter.objectInstanceSettings.type];
        } else {
            res = GetMeshInstanceType();
        }
        res?.FillInitialVariables(res.VariableContainer, settings);
        return res;
    }

    public int GetLayer() {
        var type = GetPlacementType();
        if (type == null) return 11;
        return (int)type.Rules.layer.GetValue(type.VariableContainer);
    }

    MeshManager.MeshTransform GetMeshTransform() {
        var type = GetPlacementType();
        if (type == null) return MeshManager.GetPlacementTransform(meshPath, false, false);
        var vc = type.VariableContainer;
        var rules = type.Rules;
        var useCoreTransform = rules.meshTransform != null && rules.meshTransform.GetValue(vc);
        return MeshManager.GetPlacementTransform(meshPath, useCoreTransform, rules.autoYOffset.GetValue(vc));
    }

    //puts the object on the layers it is placed on, if its placement rules ask for it (snapToSurface),
    //e.g. after being moved with the gizmo; returns whether it was projected
    public bool SnapToSurface() {
        var type = GetPlacementType();
        var rules = type?.Rules;
        if (rules?.snapToSurface == null) return false;
        if (!rules.snapToSurface.GetValue(type.VariableContainer)) return false;
        var mask = rules.placerLayerMask != null ? (int)rules.placerLayerMask.GetValue(type.VariableContainer) : 1 << 3;
        //not on itself (on its layer, or on the moveable one while it is moved)
        mask &= ~((1 << 8) | (1 << GetLayer()));
        GO.SetPosition(this, GeometryHelper.ProjectOnSurface(GO.GetPosition(this), mask));
        SyncParametricObjectState();
        return true;
    }

    public static void SetNewMesh(string newMesh, MeshInstance instance, ElementManager manager) {
        if (instance == null) return;
        if (instance.meshPath == newMesh) return;
        var index = manager.meshes.IndexOf(instance);
        if (index < 0) return;
        
        instance.ReplaceMesh(newMesh, manager); //keeps the name
    }

    public void Initialize() {
        ReloadMeshTransform();
        InitCollider(); //after the mesh is moved, the bounds are those of the moved mesh
        Actions.Helpers.SetLayerRecursively(GO.GetObject(this), GetLayer());
        initialized = true;
    }

    void InitCollider() {
        var shape = MeshManager.GetSelectionShape(meshPath);
        if (shape != null) {
            var collider = GameEngine.Mesh.CreateShapeCollider(GO.GetObject(this), shape);
            meshTransform.ApplyTo(collider);
            return;
        }
        //Empty or non-triangle assets keep the bounds fallback. Missing files have a placeholder mesh.
        var bounds = GameEngine.Mesh.GetEncapsulatingBoundsForChildMeshes(GO.GetObject(this));
        var size = GameEngine.Bounds.GetSize(bounds);
        if (GameEngine.Vector3.Magnitude(size) > 0) {
            GameEngine.Mesh.CreateBoxCollider(GO.GetObject(this), size, GameEngine.Bounds.GetCenter(bounds));
        }
    }

    public int ManualUpdate() {
        if (settings.HasChanged()) {
            ReloadMeshTransform();
            //the layer follows the rules too (e.g. after a type change), unless the instance is being moved
            if (initialized && GO.GetLayer(this) != 8) Actions.Helpers.SetLayerRecursively(GO.GetObject(this), GetLayer());
            settings.FlagAsUnchanged();
            return 1;
        }
        return 0;
    }

    //moves the children (the mesh, and the collider once created) from the placement point as the placement rules ask
    //(e.g. after the type changed): the position of the instance stays the placement point, which is exported
    void ReloadMeshTransform() {
        var newTransform = GetMeshTransform();
        var obj = GO.GetObject(this);
        for (int i = 0; i < GO.GetChildCount(obj); i++) {
            var child = GO.GetChildI(obj, i);
            if (child == null) continue;
            meshTransform.ApplyTo(child, true);
            newTransform.ApplyTo(child);
        }
        meshTransform = newTransform;
    }

    public Vector3 GetRealPosition() {
        return GO.GetPosition(this);
    }

    public override void _Ready() {
        //Start is called after the creating code has finished setting up the object (e.g. adding the mesh children),
        //while _Ready runs as soon as the node is parented, so defer it
        Callable.From(() => { if (IsInstanceValid(this) && !IsQueuedForDeletion()) Start(); }).CallDeferred();
    }

    public void Start() {
        if (!initialized) Initialize();
    }

    public void SetMoveable(bool active) {
        Actions.Helpers.SetLayerRecursively(GO.GetObject(this), active ? 8 : GetLayer());
    }

    public void Delete() {
        History.UndoManager.Touch(this);
        if (objectParameter != null) {
            GO.Destroy(GO.GetParent(this));
        } else {
            GO.Destroy(this);
            
        }
    }

    public MeshInstance ReplaceMesh(string newPath, ElementManager manager) {
        if (meshPath == newPath) return this;
        if (objectParameter != null) {
            return ReplaceParametricMesh(newPath);
        } else {
            History.UndoManager.Touch(this);
            var newInstance = Clone(newPath);
            var idx = manager.meshes.IndexOf(this);
            manager.meshes[idx] = newInstance;
            GO.Destroy(this);
            History.ElementRegistry.Transfer(this, newInstance);
            return newInstance;
        }
    }

    public MeshInstance ReplaceParametricMesh(string newPath) {
        if (meshPath == newPath) return this;
        var newInstance = CreateParametric(newPath, GO.GetParent(this), false, GO.GetName(this), objectParameter, settings);
        GO.Destroy(this);
        return newInstance;
    }
}
