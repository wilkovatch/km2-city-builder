using System.Collections;
using System.Collections.Generic;
using Godot;

namespace EditorPanelElements {
    public class Image : EditorPanelElement {
        TextureRect image;

        protected override string TemplateName() {
            return "Image";
        }

        public Image(string title, Control parent, Vector2 pos, string imagePath, float widthFactor = 1.0f, string tooltip = null, string tag = null, float heightFactor = 1.0f)
        : base(title, parent, pos, widthFactor, tooltip, tag, heightFactor) {
            image = GetControl<TextureRect>();
            if (imagePath != null) image.Texture = UILayout.LoadPreview(imagePath) ?? UILayout.WhiteTexture();
        }

        public override void SetValue(object value) {
            // An image without sprite is a white rect.
            image.Texture = (value != null ? UILayout.LoadPreview(value.ToString()) : null) ?? UILayout.WhiteTexture();
        }
    }
}
