using System.Collections.Generic;
using System.IO;
using Godot;

public static class TextureImporter {
    static readonly List<string> baseSupportedFormats = new List<string>() { "tga", "dds", "bmp", "jpg", "jpeg", "png" };
    static List<string> fullSupportedFormats, customFormats;

    public static void ReloadTextureFormats() {
        fullSupportedFormats = new List<string>(baseSupportedFormats);
        customFormats = CoreManager.GetList("customTextureFormats", new List<string>());
        fullSupportedFormats.AddRange(customFormats);
    }

    public static List<string> GetSupportedFormats() {
        if (fullSupportedFormats == null) ReloadTextureFormats();
        return fullSupportedFormats;
    }

    public static Texture2D LoadTexture(string name) {
        Texture2D tex = null;
        name = PathHelper.ResolveFile(name);
        if (File.Exists(name)) {
            var ext = PathHelper.GetExtension(name);
            if (customFormats == null) ReloadTextureFormats();
            if (customFormats.Contains(ext)) {
                var bytes = PythonManager.SendRequest("texture|" + name);
                if (bytes == null || bytes.Length < 16) {
                    GameEngine.LogError("Python server returned no valid texture data for: " + name);
                    return null;
                }
                var width = System.BitConverter.ToInt32(bytes, 0);
                var height = System.BitConverter.ToInt32(bytes, 4);
                var fmt = System.BitConverter.ToInt32(bytes, 8);
                var mips = System.BitConverter.ToInt32(bytes, 12);
                if (width <= 0 || height <= 0 || mips < 0) {
                    GameEngine.LogError("Python server returned invalid texture dimensions for: " + name);
                    return null;
                }
                tex = GameEngine.Texture.CreateFromPtrData(width, height, fmt, mips, bytes);
            } else {
                switch (ext) {
                    case "tga":
                        tex = ReadTexture(new TextureDecoder.Formats.TGA(), name);
                        break;
                    case "dds": //only dxt1, dxt3 and dxt5, loaded flipped (top row at v = 0, like the textures of the cores)
                        tex = ReadTexture(new TextureDecoder.Formats.DDS(), name, 16384);
                        break;
                    case "bmp": //uncompressed or bitfields bmp
                        tex = ReadTexture(new TextureDecoder.Formats.BMP(), name);
                        break;
                    default: //jpg, png
                        tex = GameEngine.Texture.CreateFromFile(name, ext);
                        break;
                }
            }
        }
        return tex;
    }

    /*public static Sprite GetSprite(string name) { //TODO: in GameEngine
        Sprite res = null;
        var tex = LoadTexture(name);
        if (tex != null) {
            res = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), GameEngine.Vector2.zero);
        }
        return res;
    }*/

    public static string FindTexture(string nameWithoutExtension, bool absolute = false) {
        if (!absolute) nameWithoutExtension = PathHelper.FindInFolders(nameWithoutExtension);
        if (fullSupportedFormats == null) ReloadTextureFormats();
        foreach (var format in fullSupportedFormats) {
            var name = PathHelper.ResolveFile(nameWithoutExtension + "." + format);
            if (File.Exists(name)) {
                return name;
            }
        }
        return nameWithoutExtension;
    }

    static Texture2D ReadTexture(TextureDecoder.TextureDecoder decoder, string filename, int buffer = 4096) {
        try {
            //read only access, so that read only files can be loaded too
            using var r = new BufferedBinaryReader(File.Open(filename, FileMode.Open, System.IO.FileAccess.Read, FileShare.Read), buffer);
            var res = decoder.DecodeTexture(r);
            if (res == null) GameEngine.LogError("Unable to load texture: " + filename);
            return res;
        } catch (System.Exception e) {
            GameEngine.LogError("Unable to load texture " + filename + ": " + e.Message);
            return null;
        }
    }
}
