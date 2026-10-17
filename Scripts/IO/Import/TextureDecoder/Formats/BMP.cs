using System.IO;
using Godot;

namespace TextureDecoder.Formats {
    public class BMP : TextureDecoder //supports uncompressed 1, 4, 8, 16, 24 and 32 bit bitmaps (also with bit fields), no rle
    {
        const int BI_RGB = 0, BI_BITFIELDS = 3, BI_ALPHABITFIELDS = 6;

        struct Channel {
            readonly uint mask, max;
            readonly int shift;

            public Channel(uint mask) {
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

        public Texture2D DecodeTexture(BufferedBinaryReader r) {
            if (r.ReadByte() != 'B' || r.ReadByte() != 'M') throw new InvalidDataException("Not a BMP file");
            r.ReadBytes(8); //file size, reserved
            int offset = r.ReadInt32();
            int dibLength = r.ReadInt32();
            int width, height, bitsPerPixel, compression = BI_RGB, colorsUsed = 0;
            uint redMask = 0, greenMask = 0, blueMask = 0, alphaMask = 0;
            if (dibLength == 12) { //BITMAPCOREHEADER
                width = r.ReadUInt16();
                height = r.ReadUInt16();
                r.ReadUInt16(); //planes
                bitsPerPixel = r.ReadUInt16();
            } else if (dibLength >= 40) { //BITMAPINFOHEADER and its extensions (V2 to V5)
                width = r.ReadInt32();
                height = r.ReadInt32();
                r.ReadUInt16(); //planes
                bitsPerPixel = r.ReadUInt16();
                compression = r.ReadInt32();
                r.ReadBytes(12); //image size, resolution
                colorsUsed = r.ReadInt32();
                r.ReadInt32(); //important colors
                int headerRead = 40;
                if (dibLength >= 52) {
                    redMask = r.ReadUInt32();
                    greenMask = r.ReadUInt32();
                    blueMask = r.ReadUInt32();
                    headerRead = 52;
                }
                if (dibLength >= 56) {
                    alphaMask = r.ReadUInt32();
                    headerRead = 56;
                }
                r.ReadBytes(dibLength - headerRead); //color space data
            } else {
                throw new InvalidDataException("Unsupported BMP header size: " + dibLength);
            }
            int read = 14 + dibLength;

            bool topDown = height < 0;
            if (topDown) height = -height;
            if (width <= 0 || height <= 0) throw new InvalidDataException("Invalid BMP size: " + width + "x" + height);
            if (bitsPerPixel != 1 && bitsPerPixel != 4 && bitsPerPixel != 8 && bitsPerPixel != 16 && bitsPerPixel != 24 && bitsPerPixel != 32) {
                throw new InvalidDataException("Unsupported BMP bit depth: " + bitsPerPixel);
            }

            if (compression == BI_BITFIELDS || compression == BI_ALPHABITFIELDS) {
                if (bitsPerPixel != 16 && bitsPerPixel != 32) throw new InvalidDataException("Invalid BMP bit fields for bit depth " + bitsPerPixel);
                if (dibLength == 40) { //the masks follow the header
                    redMask = r.ReadUInt32();
                    greenMask = r.ReadUInt32();
                    blueMask = r.ReadUInt32();
                    read += 12;
                    if (compression == BI_ALPHABITFIELDS) {
                        alphaMask = r.ReadUInt32();
                        read += 4;
                    }
                }
            } else if (compression == BI_RGB) {
                if (bitsPerPixel == 16) {
                    redMask = 0x7C00; greenMask = 0x3E0; blueMask = 0x1F; alphaMask = 0;
                } else {
                    //for 32 bit the 4th byte is officially unused, but often holds the alpha
                    redMask = 0xFF0000; greenMask = 0xFF00; blueMask = 0xFF; alphaMask = bitsPerPixel == 32 ? 0xFF000000 : 0;
                }
            } else {
                throw new InvalidDataException("Compressed BMP files are not supported (compression " + compression + ")");
            }
            var red = new Channel(redMask);
            var green = new Channel(greenMask);
            var blue = new Channel(blueMask);
            var alpha = new Channel(alphaMask);

            byte[] palette = null; //rgb triplets
            int paletteLength = 0;
            if (bitsPerPixel <= 8) {
                paletteLength = colorsUsed > 0 && colorsUsed < (1 << bitsPerPixel) ? colorsUsed : 1 << bitsPerPixel;
                int entryLength = dibLength == 12 ? 3 : 4;
                if (offset > read) paletteLength = System.Math.Min(paletteLength, (offset - read) / entryLength); //short palettes
                palette = new byte[paletteLength * 3];
                for (int i = 0; i < paletteLength; i++) {
                    var entry = r.ReadBytes(entryLength);
                    palette[i * 3 + 0] = entry[2];
                    palette[i * 3 + 1] = entry[1];
                    palette[i * 3 + 2] = entry[0];
                }
                read += paletteLength * entryLength;
            }

            if (offset < read) throw new InvalidDataException("Invalid BMP pixel data offset: " + offset);
            int stride = (int)(((long)width * bitsPerPixel + 31) / 32 * 4); //rows are padded to 4 bytes
            if ((long)stride * height > r.GetLength() - offset) throw new InvalidDataException("BMP pixel data is truncated");
            r.ReadBytes(offset - read);

            //the first row is the bottom one (v = 0), like in the file unless it's stored top down
            byte[] rgba = new byte[4 * width * height];
            bool anyAlphaNonZero = false, anyAlphaNotOpaque = false;
            for (int fileRow = 0; fileRow < height; fileRow++) {
                byte[] row = r.ReadBytes(stride);
                int y = topDown ? height - 1 - fileRow : fileRow;
                int o = 4 * width * y;
                for (int x = 0; x < width; x++, o += 4) {
                    if (bitsPerPixel <= 8) {
                        int bit = x * bitsPerPixel;
                        int index = (row[bit >> 3] >> (8 - bitsPerPixel - (bit & 7))) & ((1 << bitsPerPixel) - 1);
                        if (index < paletteLength) {
                            rgba[o + 0] = palette[index * 3 + 0];
                            rgba[o + 1] = palette[index * 3 + 1];
                            rgba[o + 2] = palette[index * 3 + 2];
                        }
                        rgba[o + 3] = 255;
                    } else if (bitsPerPixel == 24) {
                        rgba[o + 0] = row[3 * x + 2];
                        rgba[o + 1] = row[3 * x + 1];
                        rgba[o + 2] = row[3 * x + 0];
                        rgba[o + 3] = 255;
                    } else {
                        uint pixel = bitsPerPixel == 16
                            ? (uint)(row[2 * x] | row[2 * x + 1] << 8)
                            : (uint)(row[4 * x] | row[4 * x + 1] << 8 | row[4 * x + 2] << 16 | row[4 * x + 3] << 24);
                        rgba[o + 0] = red.Get(pixel);
                        rgba[o + 1] = green.Get(pixel);
                        rgba[o + 2] = blue.Get(pixel);
                        byte a = alpha.Present ? alpha.Get(pixel) : (byte)255;
                        rgba[o + 3] = a;
                        if (a != 0) anyAlphaNonZero = true;
                        if (a != 255) anyAlphaNotOpaque = true;
                    }
                }
            }

            //opaque if the alpha is all 255 or all 0 (unused)
            if (alpha.Present && anyAlphaNotOpaque && anyAlphaNonZero) {
                return GameEngine.Texture.CreateFromPixelData(width, height, GameEngine.Texture.Format.RGBA8, true, rgba);
            }
            byte[] rgb = new byte[3 * width * height];
            for (int i = 0, j = 0; i < rgb.Length; i += 3, j += 4) {
                rgb[i + 0] = rgba[j + 0];
                rgb[i + 1] = rgba[j + 1];
                rgb[i + 2] = rgba[j + 2];
            }
            return GameEngine.Texture.CreateFromPixelData(width, height, GameEngine.Texture.Format.RGB8, true, rgb);
        }
    }
}
