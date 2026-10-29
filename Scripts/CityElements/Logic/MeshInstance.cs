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

    public static MeshInstance Create(string meshPath, Vector3 curPoint, GameObject container, ObjectState settings) {
        MeshInstance mesh = GS.Create<MeshInstance>(null, container);
        var obj = GO.GetObject(mesh);
        var subObj = MeshManager.GetMesh(meshPath, container);
        GO.SetParent(subObj, obj);
        GO.SetPosition(mesh, curPoint);
        mesh.meshPath = meshPath;
        mesh.settings = (ObjectState)settings.Clone();
        if (mesh.AutoOffsetActive()) {
            var newObjName = "Prop " + Actions.Helpers.GetLatestObjectNumber(container, "Prop ") + " (" + meshPath + ")"; //todo: get name from config
            GO.SetName(obj, newObjName);
        }
        else {
            var newObjName = "Mesh " + Actions.Helpers.GetLatestObjectNumber(container, "Mesh ") + " (" + meshPath + ")";
            GO.SetName(obj, newObjName);
        }
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

    CityElements.Types.Runtime.MeshInstanceSettings GetMeshInstanceSettings() {
        if (objectParameter != null) {
            return CityElements.Types.Parsers.TypeParser.GetParametricMeshInstanceSettings()[objectParameter.objectInstanceSettings.type];
        } else {
            return CityElements.Types.Parsers.TypeParser.GetMeshInstanceSettings();
        }
    }

    int GetLayer() {
        var meshSettings = GetMeshInstanceSettings();
        meshSettings.FillInitialVariables(meshSettings.variableContainer, settings);
        var layer = (int)meshSettings.rules.layer.GetValue(meshSettings.variableContainer);
        return layer;
    }

    bool AutoOffsetActive() {
        var meshSettings = GetMeshInstanceSettings();
        meshSettings.FillInitialVariables(meshSettings.variableContainer, settings);
        return meshSettings.rules.autoYOffset.GetValue(meshSettings.variableContainer);
    }

    MeshManager.MeshTransform GetMeshTransform() {
        var meshSettings = GetMeshInstanceSettings();
        var vc = meshSettings.variableContainer;
        meshSettings.FillInitialVariables(vc, settings);
        var rules = meshSettings.rules;
        var useCoreTransform = rules.meshTransform != null && rules.meshTransform.GetValue(vc);
        return MeshManager.GetPlacementTransform(meshPath, useCoreTransform, rules.autoYOffset.GetValue(vc));
    }

    //puts the object on the layers it is placed on, if its placement rules ask for it (snapToSurface),
    //e.g. after being moved with the gizmo; returns whether it was projected
    public bool SnapToSurface() {
        var meshSettings = GetMeshInstanceSettings();
        var rules = meshSettings.rules;
        if (rules.snapToSurface == null) return false;
        meshSettings.FillInitialVariables(meshSettings.variableContainer, settings);
        if (!rules.snapToSurface.GetValue(meshSettings.variableContainer)) return false;
        var mask = rules.placerLayerMask != null ? (int)rules.placerLayerMask.GetValue(meshSettings.variableContainer) : 1 << 3;
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
        
        History.UndoManager.Touch(instance);
        var newInstance = Create(newMesh, GO.GetPosition(instance), GO.GetParent(instance), instance.settings);
        GO.SetRotation(newInstance, GO.GetRotation(instance));
        GO.SetLocalScale(newInstance, GO.GetLocalScale(instance));
        GO.Destroy(instance);
        manager.meshes[index] = newInstance;
        History.ElementRegistry.Transfer(instance, newInstance);
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
            settings.FlagAsUnchanged();
            return 1;
        }
        return 0;
    }

    //moves the children (the mesh, and the collider once created) from the placement point as the placement rules ask
    //(e.g. after the prop flag changed): the position of the instance stays the placement point, which is exported
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
