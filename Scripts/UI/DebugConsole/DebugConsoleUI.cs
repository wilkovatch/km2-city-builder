using System.Collections.Generic;
using Godot;

namespace IngameDebugConsole {
    // Look of the debug console: sliced sprites tinted by their Image colour, buttons using the
    // default colour tint (highlighted x0.96, pressed x0.78), light grey 16px texts
    internal static class DebugConsoleUI {
        public static readonly Color TextColor = new Color(0.83823526f, 0.84439874f, 0.84439874f);
        public static readonly Color IconColor = new Color(0.9485294f, 0.9485294f, 0.9485294f);
        public static readonly Color HighlightedTint = new Color(0.9607843f, 0.9607843f, 0.9607843f);
        public static readonly Color PressedTint = new Color(0.78431374f, 0.78431374f, 0.78431374f);

        // Sliced sprites and their borders (spriteBorder in the .psd.meta files)
        public const string SlicedBackground = "SlicedBackground";
        public const string SlicedBackground2 = "SlicedBackground2";
        public const string SlicedBackground3 = "SlicedBackground3";
        static readonly Dictionary<string, int> spriteBorders = new Dictionary<string, int> {
            { SlicedBackground, 12 },
            { SlicedBackground2, 12 },
            { SlicedBackground3, 13 },
        };

        static readonly Dictionary<string, Texture2D> textures = new Dictionary<string, Texture2D>();

        public static Texture2D Texture(string sprite) {
            if (!textures.TryGetValue(sprite, out var texture)) {
                texture = GD.Load<Texture2D>("res://Textures/DebugConsole/" + sprite + ".png");
                textures[sprite] = texture;
            }
            return texture;
        }

        /// <summary>Image component: sliced if the sprite has borders, otherwise stretched over the rect.</summary>
        public static StyleBoxTexture Box(string sprite, Color color) {
            var box = new StyleBoxTexture { Texture = Texture(sprite), ModulateColor = color };
            box.SetTextureMarginAll(spriteBorders.TryGetValue(sprite, out var border) ? border : 0);
            box.SetContentMarginAll(0);
            return box;
        }

        public static Panel Image(string sprite, Color color, string name = null) {
            var panel = new Panel { MouseFilter = Control.MouseFilterEnum.Ignore };
            if (name != null) panel.Name = name;
            panel.AddThemeStyleboxOverride("panel", Box(sprite, color));
            return panel;
        }

        public static void SetImageColor(Panel panel, Color color) {
            ((StyleBoxTexture)panel.GetThemeStylebox("panel")).ModulateColor = color;
        }

        public static TextureRect Icon(string sprite, Color color, bool preserveAspect = true) {
            return new TextureRect {
                Name = sprite,
                Texture = Texture(sprite),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = preserveAspect ? TextureRect.StretchModeEnum.KeepAspectCentered : TextureRect.StretchModeEnum.Scale,
                SelfModulate = color,
                MouseFilter = Control.MouseFilterEnum.Ignore
            };
        }

        public static Label Text(string text, int fontSize, HorizontalAlignment alignment, Color? color = null) {
            var label = new Label {
                Text = text,
                HorizontalAlignment = alignment,
                VerticalAlignment = VerticalAlignment.Center,
                ClipText = true,
                MouseFilter = Control.MouseFilterEnum.Ignore
            };
            label.AddThemeFontSizeOverride("font_size", fontSize);
            label.AddThemeColorOverride("font_color", color ?? TextColor);
            label.AddThemeConstantOverride("line_spacing", 0);
            return label;
        }

        /// <summary>Button whose target graphic is a sprite of the given colour (ColorTint transition).</summary>
        public static Button Button(string name, string sprite, Color color) {
            var button = new Button { Name = name, FocusMode = Control.FocusModeEnum.None, MouseFilter = Control.MouseFilterEnum.Stop };
            SetButtonColor(button, sprite, color);
            button.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
            UILayout.SetFontColors(button, TextColor);
            button.AddThemeFontSizeOverride("font_size", 16);
            return button;
        }

        public static void SetButtonColor(Button button, string sprite, Color color) {
            button.AddThemeStyleboxOverride("normal", Box(sprite, color));
            button.AddThemeStyleboxOverride("hover", Box(sprite, color * HighlightedTint));
            button.AddThemeStyleboxOverride("pressed", Box(sprite, color * PressedTint));
            button.AddThemeStyleboxOverride("hover_pressed", Box(sprite, color * PressedTint));
        }
    }
}
