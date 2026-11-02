using System.Collections;
using System.Collections.Generic;
using System.IO;

namespace CityElements.Types {
    //A control point of a custom element, or a group of them (with children), optionally repeated (array)
    public class CustomElementPointNode {
        public string name;
        public string label; //string key shown in the editor
        public CustomElementPointNode[] children; //a group of points if set, otherwise a single point
        public CustomElementArraySettings array; //repeated, as a list of points or of groups
        public bool rotatable; //the gizmo can rotate the point (its rotation is given to the scripts)
        public bool scalable; //the gizmo can scale the point (its scale is given to the scripts)
        public float[] defaultScale; //a new element: the scale of the point (1, 1, 1 if not set)
        public bool anchorable = true; //the point can be attached to anchors and shared with other elements
        public float[] defaultPosition; //a new element: the offset of the point from where it is created
    }

    public class CustomElementArraySettings {
        public int min;
        public int max; //0: no limit
        public bool editable = true; //the user can add and remove elements (otherwise only the converters and the API)
        public bool uniform; //in an element of another array: the same number of elements in every element of that array
        public bool closed; //a loop: the element after the last one is the first (e.g. a perimeter)
        public int defaultCount; //a new element: the number of elements (at least min)
        public float[] defaultStep; //a new element: the offset between two consecutive elements
    }

    public class CustomElementMeshSettings {
        public string uvs = "none"; //"none", "editable" (stored with the element) or "script" (given by the geometry script)
        public float[][] defaultVertices; //a new element: the offsets of the vertices from where it is created
        public int[] defaultTriangles;
    }

    public class CustomElementContainerSettings {
        public string[] contains; //the custom element types that can be put in elements of this type
    }

    //the control points computed by a script from the other custom elements replacing the same procedural element (its
    //siblings, e.g. the BAI road of the PSDL attributes of a road), while the bool parameter flag is on
    public class CustomElementDerivedSettings {
        public string flag;
        public string function; //"<module>.<function>": returns {points = the whole tree}, or nil to keep them
    }

    //A mesh instance element: a single mesh placed with a transform (no control points or geometry)
    public class CustomElementMeshInstanceSettings {
        public bool projectionSurface; //instances of this type also receive the physics-only projection target bit
        public MeshInstancePlacementRules placementRules;
        public bool massPlacement; //the placer can scatter and erase instances in a radius
        public bool randomRotation = true; //the placer can give the new instances a random yaw
        public string namePrefix = "Mesh"; //the names of the new instances: "<prefix> N (<mesh>)"
        public string[] compatibleTypes; //the mesh instance types an existing instance can be switched to
    }

    public class CustomElementSettings {
        public bool presets; //optional per-type presets, in category customElement/<type>
        public ObjectState defaultPreset; //initial Default when an older city has no category yet
        public CustomElementSplineSettings spline;
        public PropPlacerSettings propPlacer;
        public CustomElementPointSettings point; //null: not a point element
        public CustomElementMeshInstanceSettings meshInstance; //null: not a mesh instance element
        public CustomElementCreateMenu createMenu;
        public string propertyContainerCategory;
        public bool requiresPropertyContainer;
        public string placementObject; //first click positions this objectInstance; subsequent clicks draw the spline
        public int geometryLayer = 7;
        public CustomElementPointNode[] controlPoints;
        public CustomElementMeshSettings mesh; //null: no mesh
        public CustomElementContainerSettings container; //null: not a container
        public string geometry; //"<module>.<function>" of the lua folder of the core: the displayed geometry and lines
        public string constraints; //"<module>.<function>": moves points after others were moved (e.g. keeps them aligned)
        public CustomElementDerivedSettings derivedPoints; //null: the points are only edited by the user
        public bool creatable = true; //can be created from the Create menu
        //the surface rule of the points moved by the user (the projectionMask, and whether the surfaces are searched as the
        //projected sections are), null if they stay where they are put
        public (int mask, bool auto)? PointProjection => spline?.projectPoints == true ? (spline.projectionMask, spline.projectSections)
            : point?.project == true ? (point.projectionMask, true) : ((int, bool)?)null;
        public string[] propsContainers; //the props containers of the prop rule ("propRule" of the state), placed along the propLines of the geometry
        public string propsPresetType; //the prop rule presets listed: the ones of this type (e.g. a road type with the same containers), all if not set
    }

    //A single point (named center), placed with one click and edited as a mesh instance is: the gizmo is on the element
    //itself (the point has no handle) and its panel has the transform fields. The mesh is given by the geometry script
    public class CustomElementPointSettings {
        public bool rotatable = true;
        public bool scalable;
        public bool presetRotation; //include the point rotation in property presets (opt-in)
        public bool presetScale; //include the point scale in property presets (opt-in)
        public bool[] positionAxes; //the axes of the position the gizmo and the fields can change (X, Y, Z), all if not set
        public bool[] rotationAxes; //the same for the rotation (X, Y, Z: pitch, yaw, roll), all if not set
        public bool[] scaleAxes; //the same for the scale, all if not set
        public float[] defaultScale;
        public float[] defaultPosition;
        public bool project; //the point is put on the city surfaces (its height can't be changed), as the points of the projected splines
        public int projectionMask = (1 << 3) | (1 << 7);

        static bool[] Axes(bool[] axes, bool allowed) {
            return !allowed ? new[] { false, false, false } : axes != null && axes.Length == 3 ? axes : new[] { true, true, true };
        }

        public bool[] PositionAxes() {
            var axes = (bool[])Axes(positionAxes, true).Clone();
            if (project) axes[1] = false;
            return axes;
        }

        public bool[] RotationAxes() { return Axes(rotationAxes, rotatable); }
        public bool[] ScaleAxes() { return Axes(scaleAxes, scalable); }
    }

    public class CustomElementCreateMenu {
        public bool direct;
        public string label; //string key: the text of the direct entry in the Create menu (the label of the type if not set)
        public string group = "CREATE_CUSTOM_ELEMENT";
        public string submenu; //string key: the entry (direct or group) is listed in the Create submenu with that name
    }

    //Road component geometry without road connections or traffic. Curve ids match GeometryHelper.CurveType.
    public class CustomElementSplineSettings : RoadSettings {
        public CustomElementSplineSettings() { curveTypes = new[] { 0, 1, 2 }; defaultCurveType = 2; }
        public CustomElementSplineLine line, controlLine;
        public float previewTolerance;
        public int previewMaxDepth = 8;
        public int segments = 20;
        public bool segmentsPer100m;
        public bool closed; //a loop: the last control point connects back to the first (curve types: hermite, low poly or core curves)
        public bool projectPoints;
        public bool projectSections;
        public int projectionMask = (1 << 3) | (1 << 7);
        public float maxSectionLength;
        public float maxMiter = 2;
        public bool independentSegments;
        //Optional expressions to subdivide pieces (e.g. a texture's quantized tile length).
        public string splitCondition;
        public int maxSplits = 3;
    }

    public class CustomElementSplineLine {
        public string arrowDirection;
        public string color = "yellow";
        public bool always;
        public float heightOffset; //world-space Y offset of the displayed line and its picking collider
        public int trimStart, trimEnd;
    }

    public class CustomElementType: ITypeWithUI {
        public Panel ui;
        public ParameterContainer parameters;
        public CustomElementSettings settings;
        public RoadComponent[] splineComponents;

        public CustomElementType() { }

        public CustomElementType(string folder) {
            ui = Parsers.TypeInheritance.Load<Panel>(folder, "ui.json");
            parameters = Parsers.TypeInheritance.Load<ParameterContainer>(folder, "parameters.json");
            settings = Parsers.TypeInheritance.Load<CustomElementSettings>(folder, "settings.json");
            settings.propPlacer?.Initialize(this);
            if (settings.meshInstance != null) {
                if (settings.spline != null || settings.point != null || (settings.controlPoints?.Length ?? 0) > 0 || settings.mesh != null || settings.geometry != null
                    || settings.constraints != null || settings.derivedPoints != null || settings.container != null
                    || (settings.propsContainers?.Length ?? 0) > 0 || settings.presets) {
                    throw new System.Exception("Mesh instance custom element types cannot also define spline, point, controlPoints, mesh, geometry, constraints, derivedPoints, container, propsContainers or presets");
                }
                if (settings.meshInstance.placementRules == null) throw new System.Exception("Mesh instance custom element types need placementRules");
            }
            if (settings.spline != null) {
                var builtins = new[] {
                    new Parameter { name = "curveType", type = "int" },
                    new Parameter { name = "segments", type = "int" },
                    new Parameter { name = "segmentsPer100m", type = "bool" },
                    new Parameter { name = "hermiteTension", type = "float" },
                    new Parameter { name = "subdivideEqually", type = "bool" }
                };
                var parametersList = new List<Parameter>(parameters.parameters ?? new Parameter[0]);
                foreach (var builtin in builtins) {
                    if ((builtin.name == "hermiteTension" || builtin.name == "subdivideEqually") && !System.Array.Exists(settings.spline.curveTypes, i => i == 1)) continue;
                    if (!parametersList.Exists(p => p.fullName() == builtin.name)) parametersList.Add(builtin);
                }
                parameters.parameters = parametersList.ToArray();
                if (settings.mesh != null || (settings.controlPoints?.Length ?? 0) > 0 || settings.geometry != null) {
                    throw new System.Exception("Spline custom element types cannot also define mesh, controlPoints or geometry");
                }
                if (settings.spline.closed && System.Array.Exists(settings.spline.curveTypes, i => i == 0)) {
                    throw new System.Exception("Closed spline custom element types cannot allow the Bézier curve type");
                }
                settings.controlPoints = SplinePoints(settings.spline.closed);
                var components = settings.spline.components ?? new ComponentInfo[0];
                splineComponents = new RoadComponent[components.Length];
                var common = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(folder)), "sharedComponents");
                for (int i = 0; i < components.Length; i++) {
                    splineComponents[i] = Parsers.TypeInheritance.LoadComponent<RoadComponent>(folder, components[i].name, common);
                }
            }
            if (settings.point != null) {
                if (settings.spline != null || settings.mesh != null || (settings.controlPoints?.Length ?? 0) > 0 || settings.derivedPoints != null || settings.container != null) {
                    throw new System.Exception("Point custom element types cannot also define spline, mesh, controlPoints, derivedPoints or container");
                }
                if (settings.geometry == null) throw new System.Exception("Point custom element types need a geometry script");
                settings.controlPoints = PointNodes(settings.point);
            }
            if (settings.controlPoints == null) settings.controlPoints = new CustomElementPointNode[0];
        }

        public static CustomElementPointNode[] PointNodes(CustomElementPointSettings point) {
            return new[] { new CustomElementPointNode { name = "center", label = "CE_POINT", rotatable = point.rotatable, scalable = point.scalable,
                anchorable = false, defaultScale = point.defaultScale, defaultPosition = point.defaultPosition } };
        }

        public static CustomElementPointNode[] SplinePoints(bool closed = false) {
            return new[] { new CustomElementPointNode { name = "spline", label = "CE_SPLINE_POINTS",
                array = new CustomElementArraySettings { min = 0, defaultCount = 0, closed = closed } } };
        }

        public ParameterContainer GetParameters() {
            return parameters;
        }

        public Panel GetUI() {
            return ui;
        }
    }
}
