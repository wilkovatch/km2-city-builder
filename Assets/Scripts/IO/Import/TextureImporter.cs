using System.Collections.Generic;
using System.IO;
using UnityEngine;

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
        if (File.Exists(name)) {
            var ext = PathHelper.GetExtension(name);
            if (customFormats == null) ReloadTextureFormats();
            if (customFormats.Contains(ext)) {
                var bytes = PythonManager.SendRequest("texture|" + name);
                var width = System.BitConverter.ToInt32(bytes, 0);
                var height = System.BitConverter.ToInt32(bytes, 4);
                var fmt = System.BitConverter.ToInt32(bytes, 8);
                var mips = System.BitConverter.ToInt32(bytes, 12);
                tex = new Texture2D(width, height, (TextureFormat)fmt, mips, false);
                unsafe {
                    fixed (byte* p = bytes) {
                        System.IntPtr ptr = (System.IntPtr)p;
                        var ptr2 = System.IntPtr.Add(ptr, 16);
                        tex.LoadRawTextureData(ptr2, bytes.Length - 16);
                        tex.Apply();
                    }
                }
            } else {
                switch (ext) {
                    case "tga":
                        tex = ReadTexture(new TextureDecoder.Formats.TGA(), name);
                        break;
                    case "dds": //only dxt1 and dxt5, loaded flipped
                        tex = ReadTexture(new TextureDecoder.Formats.DDS(), name, 16384);
                        break;
                    case "bmp": //only 24 bit bmp
                        tex = ReadTexture(new TextureDecoder.Formats.BMP(), name);
                        break;
                    default: //jpg, png
                        byte[] fileData = File.ReadAllBytes(name);
                        tex = new Texture2D(2, 2);
                        tex.LoadImage(fileData);
                        break;
                }
            }
            tex.anisoLevel = 6;
        }
        return tex;
    }

    public static Sprite GetSprite(string name) {
        Sprite res = null;
        var tex = LoadTexture(name);
        if (tex != null) {
            res = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), Vector2.zero);
        }
        return res;
    }

    public static string FindTexture(string nameWithoutExtension, bool absolute = false) {
        if (!absolute) nameWithoutExtension = PathHelper.FindInFolders(nameWithoutExtension);
        if (fullSupportedFormats == null) ReloadTextureFormats();
        foreach (var format in fullSupportedFormats) {
            var name = nameWithoutExtension + "." + format;
            if (File.Exists(name)) {
                return name;
            }
        }
        return nameWithoutExtension;
    }

    static Texture2D ReadTexture(TextureDecoder.TextureDecoder decoder, string filename, int buffer = 4096) {
        var r = new BufferedBinaryReader(File.Open(filename, FileMode.Open), buffer);
        var res = decoder.DecodeTexture(r);
        r.Dispose();
        return res;
    }
}