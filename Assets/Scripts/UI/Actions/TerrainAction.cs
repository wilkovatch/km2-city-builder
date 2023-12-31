using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public abstract class TerrainAction {
    public abstract void ShowOnMap(List<RaycastHit?> hits);
    public abstract void Apply(List<RaycastHit?> hits);
    public virtual List<int> GetLayerMasks() {
        return new List<int> { ~(1 << 7) };
    }
}
