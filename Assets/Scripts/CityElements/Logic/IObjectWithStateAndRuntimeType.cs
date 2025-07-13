using UnityEngine;

public interface IObjectWithStateAndRuntimeType : IObjectWithState {
    public Vector3 GetRuntimeVec3(string variable);
    public Vector2 GetRuntimeVec2(string variable);
    public float GetRuntimeFloat(string variable);
    public bool GetRuntimeBool(string variable);
}