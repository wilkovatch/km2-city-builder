using System.Collections;
using System.Collections.Generic;
using Godot;
using GS = GameEngine.Scriptable;

class ActionHandlerManager {
    public static ElementManager manager;
    static Node3D handle = null;
    static Node3D discHandle = null;
    static ShaderMaterial discHandleMaterial = null;

    static Node3D GetHandle() {
        if (handle == null) {
            handle = GameEngine.Resources.InstantiateObject("Handle", manager);
            handle.Scale = GameEngine.Vector3.one;
            // The preview handle sits under the cursor, so it must not intercept the editor raycasts.
            var collider = GS.FindRecursive<CollisionShape3D>(handle);
            if (collider != null) collider.Disabled = true;
            GS.Cast<Handle>(handle).hideIfUnusedMode = true;
        }
        return handle;
    }

    static Node3D GetDiscHandle() {
        if (discHandle == null) {
            discHandle = GameEngine.Resources.InstantiateObject("DiscHandle", manager);
            discHandle.Scale = GameEngine.Vector3.one;
            // The scene has no collider.
            // Give this instance its own copy of the shared material.
            var mesh = GS.FindRecursive<MeshInstance3D>(discHandle);
            if (mesh != null) {
                discHandleMaterial = (ShaderMaterial)mesh.GetSurfaceOverrideMaterial(0).Duplicate();
                mesh.SetSurfaceOverrideMaterial(0, discHandleMaterial);
            }
        }
        return discHandle;
    }

    static void SetColor(Color color) {
        //handle.GetComponent<MeshRenderer>().material = MaterialManager.GetHandleMaterial(color); //TODO: FIX
    }

    public static void ShowHandle(Vector3 position, float size, Color color) {
        if (float.IsNaN(position.X) || float.IsNaN(position.Y) || float.IsNaN(position.Z)) return;
        var handle = GetHandle();
        GameEngine.GameObject.SetEnabled(handle, true);
        GS.Cast<Handle>(handle).hideIfUnusedShow = true;
        GameEngine.GameObject.SetPosition(handle, position);
        var handleComp = GS.Cast<Handle>(handle);
        handleComp.SetColor(color);
        handleComp.SetScale(GameEngine.Vector3.one * size);
    }

    public static void ShowDiscHandle(Vector3 position, float size, Color color) {
        if (float.IsNaN(position.X) || float.IsNaN(position.Y) || float.IsNaN(position.Z)) return;
        var handle = GetDiscHandle();
        GameEngine.GameObject.SetEnabled(handle, true);
        GS.Cast<DiscHandle>(handle).show = true;
        GameEngine.GameObject.SetPosition(handle, position);
        handle.Scale = new Vector3(size, 1, size);
        GameEngine.Materials.SetColor(discHandleMaterial, color);
    }
}