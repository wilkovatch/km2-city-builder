using SVector3 = States.SerializableVector3;
using SQuaternion = States.SerializableQuaternion;

namespace IO.SavedCity {
    [System.Serializable]
    public struct MeshInstance {
        public int id;
        public string name;
        public string mesh;
        public ObjectState settings;
        public SVector3 position;
        public SVector3 scale;
        public SQuaternion rotation;
    }

    [System.Serializable]
    public struct Road {
        public int id;
        public string name;
        public ObjectState state;
        public ObjectState instanceState;
        public SVector3[] points;
        public int startIntersectionId; //reference to an intersection
        public int endIntersectionId; //reference to an intersection
    }

    [System.Serializable]
    public struct Intersection {
        public int id;
        public string name;
        public int[] roads; //references to roads
        public SVector3 center;
        public ObjectState state;
        public ObjectState instanceState;
    }

    [System.Serializable]
    public enum LinkType {
        None, Point, Line
    }

    [System.Serializable]
    public enum LinkElementType {
        None, Road, Intersection, TerrainPoint
    }

    [System.Serializable]
    public struct TerrainPoint {
        public int id;
        public SVector3 position;
        public bool dividing;
        public LinkType linkType;
        public LinkElementType elementType; //type of the entity the anchor is in (if the point is on an anchor)
        public int elementId; //reference to a road, intersection or previous terrain point
        public int anchorIndex; //index of the anchor (or link start point) in the road/intersection (or link end point id if element type is terrain point)
        public float percent; //used if linktype is line
    }

    [System.Serializable]
    public struct TerrainBorderMesh {
        public ObjectState state;
        public ObjectState instanceState;
        public int[] segmentPointsIds; //references to terrain points
    }

    [System.Serializable]
    public struct TerrainPatch {
        public int id;
        public string name;
        public ObjectState state;
        public int[] perimeterPointsIds; //references to terrain points
        public int[] internalPointsIds; //references to terrain points
        public TerrainBorderMesh[] borderMeshes;
    }

    [System.Serializable]
    public struct BuildingLine {
        public int id;
        public string name;
        public ObjectState state;
        public ObjectState instanceState;
        public int[] linePoints; //references to terrain points
        public ObjectState[] buildings;
        public ObjectState[] sides;
    }

    public struct GenericObject { //TODO: remove (the same can be done with multiple types of mesh instances)
        public string type;
        public SVector3 position;
        public SVector3 scale;
        public SQuaternion rotation;
        public ObjectState state;
    }

    [System.Serializable]
    public struct SavedCity {
        public int heightmapResolution;
        public float maxHeight;
        public int terrainSize;
        public string heightMap;
        public MeshInstance[] meshes;
        public Road[] roads;
        public Intersection[] intersections;
        public TerrainPoint[] terrainPoints;
        public TerrainPatch[] terrainPatches;
        public BuildingLine[] buildingLines;
        public GenericObject[] genericObjects;
    }
}
