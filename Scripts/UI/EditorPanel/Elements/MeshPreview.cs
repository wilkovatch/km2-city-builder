using Godot;

namespace EditorPanelElements {
    public class MeshPreview : EditorPanelElement {
        public readonly Rendering.MeshPreviewRenderer renderer;
        protected override string TemplateName() => "Image";

        public MeshPreview(string title, Control parent, Vector2 pos, float widthFactor = 1.0f, float heightFactor = 1.0f)
            : base(title, parent, pos, widthFactor, null, null, heightFactor) {
            var image = GetControl<TextureRect>();
            image.FlipV = false;
            image.Texture = null;
            image.MouseFilter = Control.MouseFilterEnum.Ignore;
            var unavailable = UILayout.MakeLabel(StringManager.Get("MESH_SEL_PREVIEW_UNAVAILABLE"));
            UILayout.Stretch(unavailable);
            unavailable.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            unavailable.MouseFilter = Control.MouseFilterEnum.Ignore;
            unavailable.Visible = false;
            image.AddChild(unavailable);
            renderer = new Rendering.MeshPreviewRenderer { Name = "MeshPreviewRenderer", display = image };
            renderer.previewChanged = (texture, failed) => {
                image.Texture = texture;
                unavailable.Visible = failed;
            };
            obj.AddChild(renderer);
        }

        public override void SetValue(object value) => renderer.SetMesh(value?.ToString());
    }
}
