using SVector3 = States.SerializableVector3;
using SQuaternion = States.SerializableQuaternion;
using SVector2 = States.SerializableVector2;

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
        //yaw and roll of each point (degrees), missing when the road has none
        [Newtonsoft.Json.JsonProperty(NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore)]
        public SVector2[] pointRotations;
        public int startIntersectionId; //reference to an intersection
        public int endIntersectionId; //reference to an intersection
        //replaced by low-level elements: hidden and not exported, kept for the elements connected to it
        [Newtonsoft.Json.JsonProperty(DefaultValueHandling = Newtonsoft.Json.DefaultValueHandling.Ignore)]
        public bool ghost;
    }

    [System.Serializable]
    public struct Intersection {
        public int id;
        public string name;
        public int[] roads; //references to roads
        public SVector3 center;
        public ObjectState state;
        public ObjectState instanceState;
        [Newtonsoft.Json.JsonProperty(DefaultValueHandling = Newtonsoft.Json.DefaultValueHandling.Ignore)]
        public bool ghost; //as for roads
    }

    [System.Serializable]
    public enum LinkType {
        None, Point, Line,
        Column //on the vertical line of a terrain point (elementId), see ColumnAnchor
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
        public int elementId; //reference to a road, intersection or previous terrain point (Point: fixed, Column: vertical)
        public int anchorIndex; //index of the anchor (or link start point) in the road/intersection (or link end point id if element type is terrain point)
        public float percent; //used if linktype is line
        //the rotation of the rotatable points of the low-level elements (euler angles in degrees), missing for the others
        [Newtonsoft.Json.JsonProperty(NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore)]
        public SVector3? rotation;
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

    //a reference to an element of the city (a parameter of a low-level element, or the element it replaces)
    [System.Serializable]
    public struct ElementReference {
        public string kind; //road, intersection, terrainPatch, buildingLine, lowLevel
        public int id;
    }

    [System.Serializable]
    public struct LowLevelElement {
        public int id;
        public string name;
        public ObjectState state; //the type and the parameters
        public Newtonsoft.Json.Linq.JToken points; //the control points, structured as defined by the type, as terrain point ids
        [Newtonsoft.Json.JsonProperty(NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore)]
        public int[] meshVertices; //terrain point ids
        [Newtonsoft.Json.JsonProperty(NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore)]
        public int[] triangles;
        [Newtonsoft.Json.JsonProperty(NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore)]
        public SVector2[] uvs;
        public int containerId; //reference to another low-level element, -1 if none
        [Newtonsoft.Json.JsonProperty(NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore)]
        public ElementReference? source; //the procedural element it replaces (a ghost)
    }

    [System.Serializable]
    public struct SavedCity {
        public int heightmapResolution;
        public float maxHeight;
        public int terrainSize;
        public string heightMap;
        public ObjectState cityProperties;
        public MeshInstance[] meshes;
        public Road[] roads;
        public Intersection[] intersections;
        public TerrainPoint[] terrainPoints;
        public TerrainPatch[] terrainPatches;
        public BuildingLine[] buildingLines;
        public LowLevelElement[] lowLevelElements;
        public LowLevelElement[] propPaths;
    }
}
