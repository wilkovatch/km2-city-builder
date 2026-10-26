using System.Collections.Generic;
using Godot;

namespace Rendering {
    public enum CameraMode { Top, Orbit, Custom, Editor }

    //Something drawn over the image (after the 3D): the points are in Godot coordinates, projected with the camera
    public class Overlay {
        public enum Kind { Label, Marker, Polyline }

        public Kind kind;
        public Vector3[] points;
        public string text;
        public Color color = Colors.White;
        //font size of the labels, radius of the markers, width of the lines (pixels)
        public float size = 0;
        public bool closed = false;
        //given back with the pixel position (e.g. the element of a label)
        public object tag;
    }

    //What an offscreen render shows and how. Everything is in Godot coordinates (the API converts from the program ones).
    public class RenderRequest {
        public int width = 1024;
        public int height = 1024;

        public CameraMode mode = CameraMode.Top;
        //the area to show (top and orbit); null: the whole scene shown
        public Aabb? frame;
        //space added around the frame (1: none)
        public float margin = 1.1f;
        //orbit: degrees below the horizon and around the vertical axis, distance (null: the one fitting the frame)
        public float pitch = 50;
        public float yaw = 30;
        public float? distance;
        //custom
        public Vector3 position;
        public Vector3 target;
        public float fov = 50;

        //the render layers shown
        public uint cullMask = RenderLayers.City;
        //null: the background of the environment
        public Color? background;
        public bool transparent = false;
        //every LOD group at its most detailed level, and no culling distances (otherwise: as seen by the render camera)
        public bool bestLod = true;
        //the meshes drawn as wireframe, on GameEngine.Camera.WireframeBackground unless background or transparent
        public bool wireframe = false;

        //the meshes of these nodes are tinted with the color, over everything
        public List<Node> highlights = new List<Node>();
        public Color highlightColor = new Color(1, 0.2f, 0.1f, 0.45f);

        public List<Overlay> overlays = new List<Overlay>();
        //lines every gridSpacing meters at the height gridHeight (0: none)
        public float gridSpacing = 0;
        public float gridHeight = 0;
        public bool scaleBar = false;
        public float labelSize = 14;

        //rendered in a world of its own instead of the city (lit by a default light), framed on it
        public Node3D isolated;
    }

    public class PlacedOverlay {
        public Overlay overlay;
        //position of its first point in the image (pixels from the top left), null if behind the camera
        public Vector2? pixel;
        public bool inView;
    }

    public class RenderResult {
        public Image image;
        public CameraMode mode;
        public bool orthogonal;
        public Transform3D cameraTransform;
        public float fov;
        //orthogonal: meters per pixel, and the Godot position of the center of the image
        public float metersPerPixel;
        public Vector3 center;
        public List<PlacedOverlay> overlays = new List<PlacedOverlay>();
    }
}
