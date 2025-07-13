using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class MeshInstance : MonoBehaviour
{
    public string meshPath;
    public ObjectState settings = null;
    public CityElements.Types.Parameter objectParameter = null;
    bool initialized = false;
    float yOffset = 0.0f;

    public static MeshInstance Create(string meshPath, Vector3 curPoint, GameObject container, ObjectState settings) {
        var obj = MeshManager.GetMesh(meshPath, container);
        var mesh = obj.AddComponent<MeshInstance>();
        mesh.transform.position = curPoint;
        mesh.meshPath = meshPath;
        mesh.settings = (ObjectState)settings.Clone();
        if (mesh.AutoOffsetActive()) {
            obj.name = "Prop " + Actions.Helpers.GetLatestObjectNumber(container, "Prop ") + " (" + meshPath + ")"; //todo: get name from config
            mesh.yOffset = -MeshManager.GetBounds(meshPath).min.y;
        } else {
            obj.name = "Mesh " + Actions.Helpers.GetLatestObjectNumber(container, "Mesh ") + " (" + meshPath + ")";
        }
        return mesh;
    }

    MeshInstance Clone(string meshPath) {
        var obj = MeshManager.GetMesh(meshPath, transform.parent.gameObject);
        var mesh = obj.AddComponent<MeshInstance>();
        mesh.transform.position = transform.position;
        mesh.transform.rotation = transform.rotation;
        mesh.transform.localScale = transform.localScale;
        mesh.meshPath = meshPath;
        mesh.settings = (ObjectState)settings.Clone();
        obj.name = gameObject.name;
        if (mesh.AutoOffsetActive()) {
            mesh.yOffset = -MeshManager.GetBounds(meshPath).min.y;
        }
        return mesh;
    }

    public static MeshInstance CreateParametric(string meshPath, GameObject container, bool createContainer, string name, CityElements.Types.Parameter objectParameter, ObjectState settings) {
        GameObject parentObj;
        if (createContainer) {
            parentObj = new GameObject(name + " (container)");
            parentObj.transform.parent = container.transform;
        } else {
            parentObj = container;
        }
        var obj = MeshManager.GetMesh(meshPath, parentObj);
        if (!obj) obj = MeshManager.GetMesh("", parentObj); //fallback
        var mesh = obj.AddComponent<MeshInstance>();
        mesh.meshPath = meshPath;
        mesh.settings = settings; //not a clone since it's tied to the parent
        mesh.settings.SetStr("meshPath", meshPath);
        mesh.objectParameter = objectParameter;
        obj.name = name;
        if (mesh.AutoOffsetActive()) {
            mesh.yOffset = -MeshManager.GetBounds(meshPath).min.y;
        }

        //custom additive values
        mesh.transform.localPosition = settings.Vector3("localPosition");
        mesh.transform.localRotation = Quaternion.Euler(settings.Vector3("localRotation"));
        mesh.transform.localScale = settings.Vector3("localScale", Vector3.one);

        return mesh;
    }

    public void SyncParametricObjectState(ObjectState newSettings = null) {
        if (objectParameter != null) {
            if (newSettings != null) {
                settings = newSettings;
                transform.localPosition = settings.Vector3("localPosition", transform.localPosition);
                transform.localRotation = Quaternion.Euler(settings.Vector3("localRotation", transform.localRotation.eulerAngles));
                transform.localScale = settings.Vector3("localScale", transform.localScale);
            } else {
                if (!GeometryHelper.AreVectorsEqual(settings.Vector3("localPosition"), transform.localPosition)) {
                    settings.SetVector3("localPosition", transform.localPosition, false);
                }
                if (!GeometryHelper.AreVectorsEqual(settings.Vector3("localRotation"), transform.localRotation.eulerAngles)) {
                    settings.SetVector3("localRotation", transform.localRotation.eulerAngles, false);
                }
                if (!GeometryHelper.AreVectorsEqual(settings.Vector3("localScale"), transform.localScale)) {
                    settings.SetVector3("localScale", transform.localScale, false);
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

    public float GetYOffset() {
        return yOffset;
    }

    public static void SetNewMesh(string newMesh, MeshInstance instance, ElementManager manager) {
        if (instance == null) return;
        if (instance.meshPath == newMesh) return;
        var index = manager.meshes.IndexOf(instance);
        if (index < 0) return;
        var newInstance = Create(newMesh, instance.transform.position - Vector3.up * instance.GetYOffset(), instance.transform.parent.gameObject, instance.settings);
        newInstance.transform.rotation = instance.transform.rotation;
        newInstance.transform.localScale = instance.transform.localScale;
        Destroy(instance.gameObject);
        manager.meshes[index] = newInstance;
    }

    public void Initialize() {
        ReloadYOffset();
        InitCollider();
        Actions.Helpers.SetLayerRecursively(gameObject, GetLayer());
        if (yOffset != 0) Actions.Helpers.OffsetChildren(gameObject, yOffset);
        initialized = true;
    }

    void InitCollider() {
        MeshFilter[] mfs = gameObject.GetComponentsInChildren<MeshFilter>();
        if (mfs.Length > 0) {
            //calculate the bounds
            var bounds = mfs[0].sharedMesh.bounds;
            foreach (MeshFilter mf in mfs) {
                bounds.Encapsulate(mf.sharedMesh.bounds);
            }

            //create the collider
            var box = gameObject.AddComponent<BoxCollider>();
            box.size = bounds.size;
            box.center = bounds.center + Vector3.up * GetYOffset();
        }
    }

    public int ManualUpdate() {
        if (settings.HasChanged()) {
            ReloadYOffset();
            settings.FlagAsUnchanged();
            return 1;
        }
        return 0;
    }

    void ReloadYOffset() {
        var oldOffset = yOffset;
        if (AutoOffsetActive()) {
            yOffset = -MeshManager.GetBounds(meshPath).min.y;
        } else {
            yOffset = 0;
        }
        Actions.Helpers.OffsetChildren(gameObject, -oldOffset + yOffset);
    }

    public Vector3 GetRealPosition() {
        return transform.position;
    }

    public void Start() {
        if (!initialized) Initialize();
    }

    public void SetMoveable(bool active) {
        Actions.Helpers.SetLayerRecursively(gameObject, active ? 8 : GetLayer());
    }

    public void Delete() {
        if (objectParameter != null) {
            Destroy(gameObject.transform.parent.gameObject);
        } else {
            Destroy(gameObject);
        }
    }

    public MeshInstance ReplaceMesh(string newPath, ElementManager manager) {
        if (meshPath == newPath) return this;
        if (objectParameter != null) {
            return ReplaceParametricMesh(newPath);
        } else {
            var newInstance = Clone(newPath);
            var idx = manager.meshes.IndexOf(this);
            manager.meshes[idx] = newInstance;
            Destroy(gameObject);
            return newInstance;
        }
    }

    public MeshInstance ReplaceParametricMesh(string newPath) {
        if (meshPath == newPath) return this;
        var newInstance = CreateParametric(newPath, transform.parent.gameObject, false, gameObject.name, objectParameter, settings);
        Destroy(gameObject);
        return newInstance;
    }
}
