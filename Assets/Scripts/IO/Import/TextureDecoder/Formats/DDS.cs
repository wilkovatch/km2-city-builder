using UnityEngine;

namespace TextureDecoder.Formats {
    class DDS : TextureDecoder //only supports dxt1 and dxt5
    {
        public Texture2D DecodeTexture(BufferedBinaryReader r) {
            int dwMagic = r.ReadInt32();
            int dwSize = r.ReadInt32();
            int dwFlags = r.ReadInt32();
            int dwHeight = r.ReadInt32();
            int dwWidth = r.ReadInt32();
            int dwPitchOrLinearSize = r.ReadInt32();
            int dwDepth = r.ReadInt32();
            int dwMipmapCount = r.ReadInt32();
            byte[] dwReserved1 = r.ReadBytes(4 * 11);
            byte[] ddpfPixelFormatA = r.ReadBytes(11);
            byte dwFourCC = r.ReadByte();
            int dwRGBBitCount = r.ReadInt32();
            byte[] ddpfPixelFormatB = r.ReadBytes(16);
            byte[] ddsCaps = r.ReadBytes(16);
            int dwReserved2 = r.ReadInt32();

            var dxtFormat = dwFourCC == 0x31 ? TextureFormat.DXT1 : dwFourCC == 0x35 ? TextureFormat.DXT5 : 0;
            byte[] dxtBytes = r.ReadBytes(r.GetLength() - 128);
            Texture2D tex = new Texture2D(dwWidth, dwHeight, dxtFormat, dwMipmapCount > 0);
            tex.LoadRawTextureData(dxtBytes);
            tex.Apply();
            return tex;
        }
    }
}