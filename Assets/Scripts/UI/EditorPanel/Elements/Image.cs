using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.UIElements;

namespace EditorPanelElements {
    public class Image : EditorPanelElement {
        UnityEngine.UI.Image image;

        protected override string TemplateName() {
            return "Image";
        }

        public Image(string title, GameObject parent, Vector2 pos, string imagePath, float widthFactor = 1.0f, string tooltip = null, string tag = null, float heightFactor = 1.0f)
        : base(title, parent, pos, widthFactor, tooltip, tag, heightFactor) {
            image = obj.transform.Find("Image").GetComponent<UnityEngine.UI.Image>();
            if (imagePath != null) image.sprite = TextureImporter.GetSprite(imagePath);
            obj.transform.Find("Checkerboard").GetComponent<UnityEngine.UI.Image>().sprite = MaterialManager.GetInstance().GetCheckerboard();
        }

        public override void SetValue(object value) {
            if (image.sprite != null) {
                Object.Destroy(image.sprite.texture);
                Object.Destroy(image.sprite);
                image.sprite = null;
            }
            if (value != null) {
                image.sprite = TextureImporter.GetSprite(value.ToString());
            }
        }
    }
}