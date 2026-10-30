using System.Collections;
using System.Collections.Generic;
using Godot;
using RaycastHit = GameEngine.Raycast.RaycastHit;

public abstract class TerrainAction {
    public abstract void ShowOnMap(List<RaycastHit?> hits);
    public abstract void Apply(List<RaycastHit?> hits);
    public virtual bool Raycast(Node3D parent, Vector3 origin, Vector3 direction, out RaycastHit hit, float distance, int mask) {
        return GameEngine.Raycast.DoRaycast(parent, origin, direction, out hit, distance, mask);
    }
    public virtual List<int> GetLayerMasks() {
        return new List<int> { ~(1 << 7) };
    }
}
