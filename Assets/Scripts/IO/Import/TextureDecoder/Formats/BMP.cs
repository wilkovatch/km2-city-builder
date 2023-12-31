using UnityEngine;

namespace TextureDecoder.Formats {
    public class BMP : TextureDecoder //only supports 24 bit bitamps
    {
        public Texture2D DecodeTexture(BufferedBinaryReader r) {
            r.ReadBytes(10);
            int offset = r.ReadInt32();
            int dibLength = r.ReadInt32();
            int width = 0, height = 0;
            switch (dibLength) {
                case 40:
                    width = r.ReadInt32();
                    height = r.ReadInt32();
                    break;
            }
            if (offset > 26) r.ReadBytes(offset - 26);
            byte[] bytes = r.ReadBytes(3 * width * height);
            for (int i = 0; i < bytes.Length; i += 3) //swap blue/red
            {
                byte red = bytes[i];
                byte blue = bytes[i + 2];
                bytes[i] = blue;
                bytes[i + 2] = red;
            }
            Texture2D tex = new Texture2D(width, height, TextureFormat.RGB24, false);
            tex.LoadRawTextureData(bytes);
            tex.Apply();
            return tex;
        }
    }
}