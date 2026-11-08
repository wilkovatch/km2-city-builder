using System.IO;
using Godot;

namespace TextureDecoder {
    interface TextureDecoder {
        Texture2D DecodeTexture(BufferedBinaryReader r);
    }

    //a colour channel of a pixel stored with bit masks (bmp bit fields, uncompressed dds), scaled to 0-255
    struct MaskChannel {
        readonly uint mask, max;
        readonly int shift;

        public MaskChannel(uint mask) {
            this.mask = mask;
            shift = 0;
            if (mask != 0) while (((mask >> shift) & 1) == 0) shift++;
            max = mask >> shift;
        }

        public bool Present => mask != 0;

        public byte Get(uint pixel) {
            if (mask == 0) return 0;
            return (byte)(((ulong)((pixel & mask) >> shift) * 255 + max / 2) / max);
        }
    }
}