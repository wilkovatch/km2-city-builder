using System.Collections.Generic;

namespace IO.ExportedCity {
    [System.Serializable]
    public class SubmeshData {
        public int materialId;
        public string b64ia_indices; //int[]
    }

    [System.Serializable]
    public class MeshData {
        public string b64v3a_vertices; //Vector3[]
        public string b64v3a_normals; //Vector3[]
        public string b64v2a_uvs; //Vector2[]
        public SubmeshData[] submeshes;
    }

    [System.Serializable]
    public class DictMesh {
        public string name;
        public string b64v3_boundsMin; //Vector3
        public string b64v3_boundsMax; //Vector3
    }

    [System.Serializable]
    public class MeshReference {
        public int meshId;
        public string b64v3_position; //Vector3
        public string b64v3_scale; //Vector3
        public string b64q_rotation; //Quaternion
    }

    [System.Serializable]
    public class MeshInstance {
        public int id;
        public string name;
        public ObjectState settings;
        public MeshReference reference;
    }

    [System.Serializable]
    public class PropLine {
        public string name;
        public MeshReference[] props;
    }

    [System.Serializable]
    public class BaseData {
        public string name;
        public ObjectState fields;
        public MeshData mesh;
        public MeshData collider;
        public PropLine[] propLines;
    }

    [System.Serializable]
    public class Road {
        public int id;
        public BaseData data;
        public string b64v3a_points; //Vector3[]
        public int startIntersectionId;
        public int endIntersectionId;
    }

    [System.Serializable]
    public class Intersection {
        public int id;
        public BaseData data;
        public string b64v3_center; //Vector3
        public Road[] roadsThrough;
        public int[] roads;
    }

    [System.Serializable]
    public class TerrainBorderMesh {
        public ObjectState fields;
        public string b64v3a_segment; //Vector3[]
    }

    [System.Serializable]
    public class TerrainPatch {
        public int id;
        public BaseData data;
        public string b64v3a_perimeterPoints; //Vector3[]
        public string b64v3a_internalPoints; //Vector3[]
        public TerrainBorderMesh[] borderMeshes;
    }

    [System.Serializable]
    public class Facade {
        public Dictionary<int, string[][]> b64f_instances; //int, List<List<Matrix4x4>> /*float[][][]*/ 
    }

    [System.Serializable]
    public class BuildingSide {
        public BaseData data;
        public Facade[] facades;
        public MeshData[] meshDict;
        public MeshData[] paramMeshes;
    }

    [System.Serializable]
    public class Building {
        public BaseData data;
        public BuildingSide front;
        public BuildingSide left;
        public BuildingSide right;
        public BuildingSide back;
        public MeshData roof;
        public string b64v3a_spline; //Vector3[]
        public string b64v3a_splineNormals; //Vector3[]
        public string b64v3a_splineActualNormals; //Vector3[]
    }

    [System.Serializable]
    public class BuildingLine {
        public int id;
        public BaseData data;
        public BuildingSide[] sides;
        public MeshData roof;
        public string b64v3a_linePoints; //Vector3[]
        public Building[] buildings;
    }

    [System.Serializable]
    public class Material {
        public int id;
        public ObjectState data;
    }

    [System.Serializable]
    public class ExportedCity {
        public DictMesh[] meshDict;
        public Material[] materialDict;
        public Road[] roads;
        public Intersection[] intersections;
        public TerrainPatch[] terrainPatches;
        public BuildingLine[] buildingLines;
        public MeshInstance[] meshInstances;
    }
}
