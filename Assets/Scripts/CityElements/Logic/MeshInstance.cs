using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class MeshInstance : MonoBehaviour
{
    public string meshPath;
    public ObjectState settings = null;
    bool initialized = false;
    float yOffset = 0.0f;

    public static MeshInstance Create(string meshPath, Vector3 curPoint, GameObject container, ObjectState settings) {
        var obj = MeshManager.GetMesh(meshPath, container);
        var mesh = obj.AddComponent<MeshInstance>();
        mesh.transform.position = curPoint;
        mesh.meshPath = meshPath;
        mesh.settings = (ObjectState)settings.Clone();
        if (mesh.AutoOffsetActive()) {
            obj.name = "Prop " + Actions.Helpers.GetLatestObjectNumber(container, "Prop ") + " (" + meshPath + ")";
            mesh.yOffset = -MeshManager.GetBounds(meshPath).min.y;
        } else {
            obj.name = "Mesh " + Actions.Helpers.GetLatestObjectNumber(container, "Mesh ") + " (" + meshPath + ")";
        }
        return mesh;
    }

    int GetLayer() {
        var meshSettings = CityElements.Types.Parsers.TypeParser.GetMeshInstanceSettings();
        meshSettings.FillInitialVariables(meshSettings.variableContainer, settings);
        var layer = (int)meshSettings.rules.layer.GetValue(meshSettings.variableContainer);
        return layer;
    }

    bool AutoOffsetActive() {
        var meshSettings = CityElements.Types.Parsers.TypeParser.GetMeshInstanceSettings();
        meshSettings.FillInitialVariables(meshSettings.variableContainer, settings);
        return meshSettings.rules.autoYOffset.GetValue(meshSettings.variableContainer);
    }

    public float GetYOffset() {
        return yOffset;
    }

    public static void SetNewMesh(string newMesh, MeshInstance instance, ElementManager manager) {
        if (instance == null) return;
        var index = manager.meshes.IndexOf(instance);
        if (index < 0) return;
        var newInstance = Create(newMesh, instance.transform.position - Vector3.up * instance.GetYOffset(), instance.transform.parent.gameObject, instance.settings);
        newInstance.transform.rotation = instance.transform.rotation;
        newInstance.transform.localScale = instance.transform.localScale;
        Destroy(instance.gameObject);
        manager.meshes[index] = newInstance;
    }

    public void Initialize() {
        Renderer[] renderers = gameObject.GetComponentsInChildren<Renderer>();
        if (renderers.Length > 0) {
            var bounds = renderers[0].bounds;
            foreach (Renderer renderer in renderers) {
                bounds.Encapsulate(renderer.bounds);
            }
            var box = gameObject.AddComponent<BoxCollider>();
            box.size = bounds.size;
            box.center = bounds.center - gameObject.transform.position;
        }
        Actions.Helpers.SetLayerRecursively(gameObject, GetLayer());
        if (yOffset != 0) Actions.Helpers.OffsetChildren(gameObject, yOffset);
        initialized = true;
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
        Destroy(gameObject);
    }
}
