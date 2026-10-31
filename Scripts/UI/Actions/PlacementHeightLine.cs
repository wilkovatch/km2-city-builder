using Godot;

//The vertical line from the ground to a point placed above it (PlacementHeight): facing the camera, thin at any distance,
//with ticks at the whole heights like the terrain grid (every 1, 10 and 100 units, longer for the larger steps; the steps
//closer than a few pixels on screen are left out)
public class PlacementHeightLine {
    const float lineWidth = 2; //pixels
    static readonly int[] tickSteps = { 1, 10, 100 };
    static readonly float[] tickLengths = { 6, 14, 24 }; //pixels, centered on the line
    const float minTickSpacing = 5; //pixels between the ticks of a step to draw them
    const int maxTicks = 1000;

    readonly MeshInstance3D node;
    readonly ImmediateMesh mesh = new ImmediateMesh();

    public PlacementHeightLine(Node3D parent, Material material) {
        //in world space, like the line renderers
        node = new MeshInstance3D {
            Name = "Placement height line",
            Mesh = mesh,
            MaterialOverride = material,
            TopLevel = true,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off
        };
        parent.AddChild(node);
    }

    public bool Valid => GodotObject.IsInstanceValid(node);
    public bool enabled => Valid && node.Visible;

    //from (on the ground) and to (the raised point) in program coordinates, on the same vertical
    public void Show(Vector3 from, Vector3 to, Camera3D cam) {
        var a = GameEngine.Vector3.DecodePosition(from);
        var b = GameEngine.Vector3.DecodePosition(to);
        var side = cam.GlobalBasis.X;
        side.Y = 0;
        side = side.LengthSquared() > 1e-8f ? side.Normalized() : Vector3.Right;
        //the pixels per unit at the raised point (under the cursor), across the line and along it
        var screen = cam.UnprojectPosition(b);
        var across = Mathf.Max(screen.DistanceTo(cam.UnprojectPosition(b + side)), 1e-6f);
        var along = Mathf.Max(screen.DistanceTo(cam.UnprojectPosition(b + Vector3.Up)), 1e-6f);
        mesh.ClearSurfaces();
        mesh.SurfaceBegin(Mesh.PrimitiveType.Triangles);
        var halfWidth = side * lineWidth * 0.5f / across;
        AddQuad(a - halfWidth, a + halfWidth, b + halfWidth, b - halfWidth);
        AddTicks(a, Mathf.Min(a.Y, b.Y), Mathf.Max(a.Y, b.Y), side, across, along);
        mesh.SurfaceEnd();
        node.Visible = true;
    }

    public void Hide() {
        node.Visible = false;
    }

    void AddTicks(Vector3 basePoint, float minY, float maxY, Vector3 side, float across, float along) {
        //the smallest step far enough apart on screen, and not too many
        var first = -1;
        for (int i = 0; i < tickSteps.Length && first < 0; i++) {
            var count = Mathf.Floor(maxY / tickSteps[i]) - Mathf.Ceil(minY / tickSteps[i]) + 1;
            if (tickSteps[i] * along >= minTickSpacing && count <= maxTicks) first = i;
        }
        if (first < 0) return;
        var step = tickSteps[first];
        var halfThickness = Vector3.Up * lineWidth * 0.5f / along;
        for (var k = (long)Mathf.Ceil(minY / step); k * step <= maxY; k++) {
            var height = k * step;
            var level = first;
            for (int i = tickSteps.Length - 1; i > first; i--) {
                if (height % tickSteps[i] == 0) {
                    level = i;
                    break;
                }
            }
            var center = new Vector3(basePoint.X, height, basePoint.Z);
            var halfLength = side * tickLengths[level] * 0.5f / across;
            AddQuad(center - halfLength - halfThickness, center + halfLength - halfThickness,
                center + halfLength + halfThickness, center - halfLength + halfThickness);
        }
    }

    //the material draws both faces
    void AddQuad(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3) {
        foreach (var p in new[] { p0, p1, p2, p0, p2, p3 }) {
            mesh.SurfaceSetNormal(Vector3.Up);
            mesh.SurfaceAddVertex(p);
        }
    }
}
