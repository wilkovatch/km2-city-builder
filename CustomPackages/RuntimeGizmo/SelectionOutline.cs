using System.Collections.Generic;
using Godot;

// Screen-space outline of the objects selected with the transform gizmo (other instances outline other objects, with a
// layer and colour of their own, set before adding it to the tree).
// The selected renderers get an extra render layer that only a mask camera sees; it renders their silhouette (alpha) into a
// SubViewport, and a fullscreen quad draws a fixed-pixel-width outline around it on top of the scene.
namespace RuntimeGizmos {
    public partial class SelectionOutline : Node3D {
        // Render layer 20 (of the gizmo), unused by the city elements (GO.SetLayer only uses layers 0-12)
        public const uint MaskLayer = 1u << 19;

        public uint maskLayer = MaskLayer;
        public Color outlineColor = new Color(1.0f, 0.5f, 0.0f);

        SubViewport maskViewport;
        Camera3D maskCamera;
        MeshInstance3D quad;
        bool active;

        public override void _Ready() {
            maskViewport = new SubViewport {
                Name = "OutlineMask",
                OwnWorld3D = false, //renders the main world
                TransparentBg = true,
                DebugDraw = Viewport.DebugDrawEnum.Unshaded, //only the coverage (alpha) is used
                Msaa3D = Viewport.Msaa.Disabled,
                ScreenSpaceAA = Viewport.ScreenSpaceAAEnum.Disabled,
                PositionalShadowAtlasSize = 0,
                RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled
            };
            AddChild(maskViewport);

            maskCamera = new Camera3D {
                Name = "OutlineMaskCamera",
                CullMask = maskLayer,
                Environment = new Environment { BackgroundMode = Environment.BGMode.ClearColor }
            };
            maskViewport.AddChild(maskCamera);
            maskCamera.MakeCurrent();

            var material = GameEngine.Resources.LoadMaterial("Materials/SelectionOutline")?.Duplicate() as ShaderMaterial; //one per instance
            material?.SetShaderParameter("mask_texture", maskViewport.GetTexture());
            material?.SetShaderParameter("outline_color", outlineColor);
            quad = new MeshInstance3D {
                Name = "OutlineQuad",
                Mesh = new QuadMesh { Size = new Vector2(2, 2) },
                MaterialOverride = material,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                ExtraCullMargin = 16384, //the vertex shader places it over the whole screen
                Layers = GameEngine.Camera.OverlayLayer,
                Visible = false
            };
            AddChild(quad);
        }

        // Called every frame by the gizmo with the renderers of the selected objects (an empty list hides the outline)
        public void UpdateOutline(List<GeometryInstance3D> renderers) {
            var mainCamera = GetViewport()?.GetCamera3D();
            //in the wireframe view the fullscreen quad would be drawn as its edges
            SetActive(renderers.Count > 0 && mainCamera != null && GetViewport().DebugDraw != Viewport.DebugDrawEnum.Wireframe);
            if (!active) return;

            //GO.SetLayer overwrites the layers, so the mask layer is reapplied every frame
            foreach (var render in renderers) {
                if (GodotObject.IsInstanceValid(render)) render.Layers |= maskLayer;
            }

            var size = (Vector2I)GetViewport().GetVisibleRect().Size;
            if (maskViewport.Size != size) maskViewport.Size = size;
            maskCamera.GlobalTransform = mainCamera.GlobalTransform;
            maskCamera.Projection = mainCamera.Projection;
            maskCamera.KeepAspect = mainCamera.KeepAspect;
            maskCamera.Fov = mainCamera.Fov;
            maskCamera.Size = mainCamera.Size;
            maskCamera.FrustumOffset = mainCamera.FrustumOffset;
            maskCamera.HOffset = mainCamera.HOffset;
            maskCamera.VOffset = mainCamera.VOffset;
            maskCamera.Near = mainCamera.Near;
            maskCamera.Far = mainCamera.Far;
        }

        public static void RemoveFromMask(GeometryInstance3D render, uint layer = MaskLayer) {
            if (GodotObject.IsInstanceValid(render)) render.Layers &= ~layer;
        }

        // The renderers of an object and of its children
        public static List<GeometryInstance3D> GetRenderers(Node target) {
            var renderers = new List<GeometryInstance3D>();
            if (target == null || !GodotObject.IsInstanceValid(target)) return renderers;
            if (target is GeometryInstance3D render) renderers.Add(render);
            foreach (var child in target.GetChildren()) {
                renderers.AddRange(GetRenderers(child));
            }
            return renderers;
        }

        void SetActive(bool value) {
            if (active == value) return;
            active = value;
            maskViewport.RenderTargetUpdateMode = value ? SubViewport.UpdateMode.Always : SubViewport.UpdateMode.Disabled;
            quad.Visible = value;
        }
    }
}
