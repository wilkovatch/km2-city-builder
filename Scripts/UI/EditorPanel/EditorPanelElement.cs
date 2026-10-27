using System.Collections;
using System.Collections.Generic;
using Godot;
using GameObject = Godot.Control;

public abstract class EditorPanelElement {
    protected GameObject obj;
    protected GameObject body;
    protected GameObject titleLabel;
    protected GameObject fieldControl;
    protected float width;
    float rootHeight; //of the element, the row it is in can be taller
    protected Vector2 pos;
    protected string tag;
    public EditorPanel parentPanel;
    public EditorPanelPage page; //the page laying it out, once added to it
    public bool actionEnabled = true;

    protected abstract string TemplateName();

    protected virtual string TitlePath() {
        return "Title";
    }

    // Root size of each element type.
    static Vector2 TemplateSize(string template) => template switch {
        "TitleInputField" => new Vector2(160, 30),
        "Image" => new Vector2(160, 150),
        "ScrollList" => new Vector2(160, 250),
        _ => new Vector2(160, 50),
    };

    public EditorPanelElement(string title, GameObject parent, Vector2 pos, float widthFactor = 1.0f, string tooltip = null, string tag = null, float heightFactor = 1.0f) {
        this.pos = pos;
        this.tag = tag;
        var template = TemplateName();
        var size = TemplateSize(template);
        width = size.X * widthFactor;
        rootHeight = size.Y * heightFactor;

        // anchor top-right of the panel, anchoredPosition = (-pos.x - w/2 - 10, -pos.y - h/2 - 10), pivot centre.
        obj = new Control { Name = template, MouseFilter = Control.MouseFilterEnum.Pass };
        UILayout.SetRect(obj, 1, 1, 1, 1, -pos.X - width / 2 - 10, -pos.Y - rootHeight / 2 - 10, width, rootHeight);
        parent.AddChild(obj);
        body = obj;

        switch (template) {
            case "Button": {
                var button = new Godot.Button { Name = "Button", Text = title };
                UILayout.SetRect(button, 0, 0.5f, 1, 0.5f, 0, 0, 0, 30);
                UILayout.StyleButton(button);
                obj.AddChild(button);
                TextFit.Apply(button);
                fieldControl = button;
                break;
            }
            case "Checkbox": {
                // 20x20 box vertically centred on the left; title (50px tall, may wrap) centred in the remaining width.
                var checkBox = new CheckBox { Name = "Toggle", Text = title, Alignment = HorizontalAlignment.Center };
                UILayout.Stretch(checkBox);
                UILayout.StyleCheckBox(checkBox);
                obj.AddChild(checkBox);
                TextFit.Apply(checkBox);
                fieldControl = checkBox;
                break;
            }
            case "Label": {
                var label = AddTitle(title);
                UILayout.Stretch(label);
                fieldControl = label;
                break;
            }
            case "InputField": {
                AddTitle(title);
                var lineEdit = new LineEdit { Name = "InputField" };
                UILayout.SetRect(lineEdit, 0, 0.5f, 1, 0.5f, 0, -10, 0, 30);
                UILayout.StyleLineEdit(lineEdit);
                obj.AddChild(lineEdit);
                fieldControl = lineEdit;
                break;
            }
            case "TitleInputField": {
                var lineEdit = new LineEdit { Name = "InputField" };
                UILayout.SetRect(lineEdit, 0, 0.5f, 1, 0.5f, 0, 0, 0, 30);
                UILayout.StyleLineEdit(lineEdit, HorizontalAlignment.Center);
                lineEdit.AddThemeFontOverride("font", UILayout.BoldFont());
                obj.AddChild(lineEdit);
                fieldControl = lineEdit;
                break;
            }
            case "Dropdown": {
                var label = AddTitle(title);
                UILayout.SetRect(label, 0, 0.5f, 1, 0.5f, 0, 15, -10, 20);
                var optionButton = new OptionButton { Name = "Dropdown", ClipText = true };
                UILayout.SetRect(optionButton, 0, 0.5f, 1, 0.5f, 0, -10, 0, 30);
                UILayout.StyleOptionButton(optionButton);
                obj.AddChild(optionButton);
                fieldControl = optionButton;
                break;
            }
            case "Slider": {
                AddTitle(title);
                var slider = new HSlider { Name = "Slider", Step = 0 }; // sliders are continuous, Godot defaults to step 1
                UILayout.SetRect(slider, 0, 0.5f, 1, 0.5f, 0, -10, 0, 20);
                UILayout.StyleSlider(slider);
                obj.AddChild(slider);
                fieldControl = slider;
                break;
            }
            case "SliderWithInputField": {
                AddTitle(title);
                var container = MakeFieldArea();
                var slider = new HSlider { Name = "Slider", Step = 0 }; // sliders are continuous, Godot defaults to step 1
                UILayout.SetRect(slider, 0, 0.5f, 0.75f, 0.5f, 0, -10, 0, 20);
                UILayout.StyleSlider(slider);
                container.AddChild(slider);
                var lineEdit = new LineEdit { Name = "InputField" };
                UILayout.SetRect(lineEdit, 0.75f, 0.5f, 1, 0.5f, 0, -10, 0, 30);
                UILayout.StyleLineEdit(lineEdit);
                container.AddChild(lineEdit);
                fieldControl = container;
                break;
            }
            case "MeshField": {
                AddTitle(title);
                var container = MakeFieldArea();
                var lineEdit = new LineEdit { Name = "InputField" };
                UILayout.SetRect(lineEdit, 0, 0.5f, 1, 0.5f, -32.5f, -10, -65, 30);
                UILayout.StyleLineEdit(lineEdit);
                container.AddChild(lineEdit);
                var button = new Godot.Button { Name = "Browse", Text = "Change" };
                UILayout.SetRect(button, 1, 0.5f, 1, 0.5f, -32.5f, -10, 65, 30);
                UILayout.StyleButton(button);
                container.AddChild(button);
                TextFit.Apply(button, TextFit.DefaultFontSize, 1);
                fieldControl = container;
                break;
            }
            case "ReferenceField": {
                AddTitle(title);
                var container = MakeFieldArea();
                // read-only name of the element, then the pick and clear buttons on the right
                var lineEdit = new LineEdit { Name = "InputField", Editable = false };
                UILayout.SetRect(lineEdit, 0, 0.5f, 1, 0.5f, -47.5f, -10, -95, 30);
                UILayout.StyleLineEdit(lineEdit);
                container.AddChild(lineEdit);
                var pick = new Godot.Button { Name = "Pick", Text = "Pick" };
                UILayout.SetRect(pick, 1, 0.5f, 1, 0.5f, -62.5f, -10, 65, 30);
                UILayout.StyleButton(pick);
                container.AddChild(pick);
                TextFit.Apply(pick, TextFit.DefaultFontSize, 1);
                var clear = new Godot.Button { Name = "Clear", Text = "X" };
                UILayout.SetRect(clear, 1, 0.5f, 1, 0.5f, -15, -10, 30, 30);
                UILayout.StyleButton(clear);
                container.AddChild(clear);
                fieldControl = container;
                break;
            }
            case "TextureField": {
                AddTitle(title);
                var container = MakeFieldArea();
                // 30x30 black-framed preview button at the bottom-left, input field to its right.
                var button = new Godot.Button { Name = "Browse", Text = "" };
                UILayout.SetRect(button, 0, 1, 0, 1, 0, -20, 30, 30, 0, 1);
                var black = UILayout.Flat(Colors.Black);
                button.AddThemeStyleboxOverride("normal", black);
                button.AddThemeStyleboxOverride("hover", UILayout.Flat(new Color(0.25f, 0.25f, 0.25f)));
                button.AddThemeStyleboxOverride("pressed", UILayout.Flat(new Color(0.4f, 0.4f, 0.4f)));
                button.AddThemeStyleboxOverride("disabled", black);
                button.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
                container.AddChild(button);
                var checker = new TextureRect { Name = "Checkerboard", Texture = UILayout.Checkerboard(),
                    ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.Scale,
                    MouseFilter = Control.MouseFilterEnum.Ignore };
                UILayout.SetRect(checker, 0, 1, 0, 1, 1, -1, 28, 28, 0, 1);
                button.AddChild(checker);
                // No texture yet: a white rect over the checkerboard.
                // Loaded textures are stored bottom row first (see GameEngine.Texture.CreateFromFile), flipped to be shown upright.
                var image = new TextureRect { Name = "Image", Texture = UILayout.WhiteTexture(), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                    StretchMode = TextureRect.StretchModeEnum.Scale, MouseFilter = Control.MouseFilterEnum.Ignore, FlipV = true };
                UILayout.SetRect(image, 0, 1, 0, 1, 1, -1, 28, 28, 0, 1);
                button.AddChild(image);
                var lineEdit = new LineEdit { Name = "InputField" };
                UILayout.SetRect(lineEdit, 0, 0.5f, 1, 0.5f, 15, -10, -30, 30);
                UILayout.StyleLineEdit(lineEdit);
                container.AddChild(lineEdit);
                fieldControl = container;
                break;
            }
            case "VectorInputField": {
                AddTitle(title);
                // HorizontalLayoutGroup (spacing 0) with three equal cells: 10px axis label + field inset 5px on the right.
                var row = new HBoxContainer { Name = "VectorInputField" };
                row.AddThemeConstantOverride("separation", 0);
                UILayout.SetRect(row, 0, 0.5f, 1, 0.5f, 0, -10, 0, 30);
                foreach (var axis in new[] { "X", "Y", "Z" }) {
                    var cell = new Control { Name = axis + "Cell", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, MouseFilter = Control.MouseFilterEnum.Pass };
                    var axisLabel = UILayout.MakeLabel(axis, HorizontalAlignment.Left);
                    UILayout.SetRect(axisLabel, 0, 0, 0, 1, 0, 0, 10, 0, 0, 0.5f);
                    cell.AddChild(axisLabel);
                    var field = new LineEdit { Name = axis, PlaceholderText = axis };
                    UILayout.SetRect(field, 0, 0, 1, 1, 2.5f, 0, -15, 0);
                    UILayout.StyleLineEdit(field);
                    cell.AddChild(field);
                    row.AddChild(cell);
                }
                obj.AddChild(row);
                fieldControl = row;
                break;
            }
            case "Image": {
                var label = AddTitle(title);
                UILayout.SetRect(label, 0, 1, 1, 1, 0, 0, 0, 20, 0.5f, 1);
                var checker = new TextureRect { Name = "Checkerboard", Texture = UILayout.Checkerboard(),
                    ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.Scale };
                UILayout.SetRect(checker, 0, 0, 1, 1, 0, 0, 0, -20, 0.5f, 0);
                obj.AddChild(checker);
                // Loaded textures are stored bottom row first (see GameEngine.Texture.CreateFromFile), flipped to be shown upright.
                var image = new TextureRect { Name = "Image", Texture = UILayout.WhiteTexture(), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                    StretchMode = TextureRect.StretchModeEnum.Scale, FlipV = true };
                UILayout.SetRect(image, 0, 0, 1, 1, 0, 0, 0, -20, 0.5f, 0);
                obj.AddChild(image);
                fieldControl = image;
                break;
            }
            case "ScrollList": {
                var label = AddTitle(title);
                UILayout.SetRect(label, 0, 1, 1, 1, 0, -12.5f, 0, 20);
                // "Scroll View" area; the ScrollList element fills it with its own ScrollContainer.
                body = new Control { Name = "ScrollView", MouseFilter = Control.MouseFilterEnum.Pass };
                UILayout.SetRect(body, 0, 0.5f, 1, 0.5f, 0, -10, 0, 225);
                obj.AddChild(body);
                break;
            }
        }

        if (!string.IsNullOrEmpty(tooltip)) UILayout.SetTooltipRecursive(obj, tooltip);
    }

    // Standard "Title" text: 20px tall strip at the top of a 50px element, best fit 9-14.
    Godot.Label AddTitle(string title) {
        var label = UILayout.MakeLabel(title);
        label.Name = "Title";
        UILayout.SetRect(label, 0, 0.5f, 1, 0.5f, 0, 15, 0, 20);
        obj.AddChild(label);
        TextFit.Apply(label);
        titleLabel = label;
        return label;
    }

    Control MakeFieldArea() {
        var area = new Control { Name = "Field", MouseFilter = Control.MouseFilterEnum.Pass };
        UILayout.Stretch(area);
        obj.AddChild(area);
        return area;
    }

    public string GetTag() {
        return tag;
    }

    public virtual void SetInteractable(bool interactable) { }

    public virtual bool GetInteractable() { return true; }

    public virtual void SetValue(object value) { }

    public void SetTitle(string title) {
        if (titleLabel is Godot.Label label) {
            label.Text = title;
            TextFit.Refit(label);
        } else if (fieldControl is Godot.Button button) {
            button.Text = title;
            TextFit.Refit(button);
        }
    }

    public float GetNextPosition() {
        return width + pos.X;
    }

    //a row of the page whose elements are all hidden takes no space (see EditorPanelPage.Relayout)
    public void SetActive(bool active) {
        if (obj == null || obj.Visible == active) return;
        obj.Visible = active;
        page?.Relayout();
    }

    public bool IsActive() {
        return obj != null && obj.Visible;
    }

    //moves the element to another vertical position in its page (its row moved)
    public void SetY(float y) {
        if (obj == null || !GodotObject.IsInstanceValid(obj) || pos.Y == y) return;
        pos = new Vector2(pos.X, y);
        UILayout.SetRect(obj, 1, 1, 1, 1, -pos.X - width / 2 - 10, -pos.Y - rootHeight / 2 - 10, width, rootHeight);
    }

    protected T GetControl<T>(string childName = null) where T : GodotObject {
        if (fieldControl is T direct) return direct;
        if (childName != null) return fieldControl?.FindChild(childName, true, false) as T;
        return null;
    }
}
