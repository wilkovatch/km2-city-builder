using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using States;

public class TerrainPatchGenerator : MonoBehaviour {
    public MeshRenderer mr;
    MeshFilter mf;
    public Mesh m;
    MeshCollider mc;
    public LineRenderer lr;

    public void Initialize() {
        gameObject.layer = 7;
        mr = gameObject.AddComponent<MeshRenderer>();
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        mf = gameObject.AddComponent<MeshFilter>();
        m = new Mesh();
        mf.mesh = m;
        mr.material = MaterialManager.GetMaterial("");
        mc = gameObject.AddComponent<MeshCollider>();
        mc.sharedMesh = m;

        lr = gameObject.AddComponent<LineRenderer>();
        lr.material = Resources.Load<Material>("Materials/Line");
        lr.startWidth = 0.5f;
        lr.endWidth = 0.5f;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;
        lr.material = MaterialManager.GetHandleMaterial((Color.red + Color.yellow) * 0.5f);
    }

    public void RebuildMesh(List<Vector3> perimeterPoints, List<Vector3> internalPoints, List<(List<Vector3> segment,
        List<Vector3> lefts, ObjectState state)> borderMeshes, string tex, int smooth, float uMult, float vMult) {

        Clear();

        var terrain = GeometryHelpers.TerrainGeneration.GetMesh(perimeterPoints, internalPoints, borderMeshes, tex, smooth, uMult, vMult);
        if (terrain.res == 0) return;
        lr.positionCount = terrain.linePoints.Count;
        lr.SetPositions(terrain.linePoints.ToArray());

        mr.materials = terrain.materials.ToArray();
        m.Clear();
        m.indexFormat = terrain.vertices.Count > 65535 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
        m.subMeshCount = terrain.numMeshes;
        m.vertices = terrain.vertices.ToArray();
        m.SetUVs(0, terrain.uvs.ToArray());
        var indices = terrain.GetIndices();
        for (int i = 0; i < indices.Count; i++) {
            m.SetIndices(indices[i], MeshTopology.Triangles, i);
        }
        m.RecalculateNormals();
        m.RecalculateBounds();
        m.name = gameObject.name;

        //refresh the collider
        mc.enabled = false;
        mc.enabled = true;
    }

    public void Clear() {
        lr.positionCount = 0;
        lr.SetPositions(new Vector3[] { });
        m.Clear();
    }
}