using System.Collections;
using System.Collections.Generic;
using Godot;

namespace EditorPanelElements {
    public class MeshField : EditorPanelElement {
        System.Action<string> action;
        System.Action buttonAction;
        Godot.LineEdit inputField;
        Godot.Button button;
        TextureRect image;
        Rendering.MeshPreviewRenderer renderer;
        string previewPath;
        bool previewVisible;
        int previewVersion;

        protected override string TemplateName() {
            return "MeshField";
        }

        public MeshField(string title, string placeholder, string defaultValue, System.Action<string> action,
            System.Action buttonAction, Control parent, Vector2 pos, float widthFactor = 1.0f, string tooltip = null, string tag = null)
        : base(title, parent, pos, widthFactor, tooltip, tag) {
            this.action = action;
            this.buttonAction = buttonAction;
            button = GetControl<Godot.Button>("Browse");
            button.Text = StringManager.Get("MESH_LIST");
            inputField = GetControl<Godot.LineEdit>("InputField");
            inputField.MaxLength = 255;
            inputField.Text = defaultValue;
            inputField.PlaceholderText = placeholder;
            image = GetControl<TextureRect>("Image");
            renderer = new Rendering.MeshPreviewRenderer { Name = "MeshFieldThumbnail", display = image };
            obj.AddChild(renderer);
            image.VisibilityChanged += () => {
                if (GodotObject.IsInstanceValid(inputField)) UpdatePreview(inputField.Text);
            };
            UpdatePreview(inputField.Text);
            TextInputFocus.OnEndEdit(inputField, delegate(string _) {
                ChangeValue();
                parentPanel.CheckFieldsInteractabilityDelayed();
            });
            button.Pressed += delegate {
                Select();
            };
        }

        void Select() {
            if (actionEnabled) buttonAction.Invoke();
        }

        void ChangeValue() {
            UpdatePreview(inputField.Text);
            action.Invoke(inputField.Text);
        }

        public string GetValue() => inputField.Text;

        public override void SetValue(object value) {
            if (value == null) value = "";
            inputField.Text = value.ToString();
            UpdatePreview(inputField.Text);
        }

        async void UpdatePreview(string name) {
            if (!GodotObject.IsInstanceValid(image) || !GodotObject.IsInstanceValid(renderer)) return;
            var visible = image.IsVisibleInTree();
            if (previewPath == name && previewVisible == visible) return;
            previewPath = name;
            previewVisible = visible;
            var version = ++previewVersion;
            renderer.Stop();
            image.Texture = null;
            if (!visible || string.IsNullOrEmpty(name)) return;
            try {
                var texture = await renderer.GetThumbnail(PathHelper.ResolveAssetPath(name));
                if (version == previewVersion && GodotObject.IsInstanceValid(image) && image.IsVisibleInTree()) image.Texture = texture;
            } catch (System.Exception e) {
                GameEngine.LogWarning("Unable to preview mesh field " + name + ": " + e.Message);
            }
        }

        public override void SetInteractable(bool interactable) {
            button.Disabled = !interactable;
            inputField.Editable = interactable;
        }

        public override bool GetInteractable() {
            return !button.Disabled;
        }
    }
}
