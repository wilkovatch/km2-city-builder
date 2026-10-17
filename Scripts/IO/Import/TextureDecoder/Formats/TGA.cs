using System.IO;
using Godot;
using Color32 = GameEngine.Color32;

namespace TextureDecoder.Formats {
    public class TGA : TextureDecoder {
        void ReadPixels(BufferedBinaryReader r, Color32[] res, int iStart, int length, int depth, bool grayscale, bool alpha16) {
            byte blue, green, red, alpha;
            short val;
            switch (depth) {
                case 8:
                    for (int i = 0; i < length; i++) {
                        red = r.ReadByte();
                        res[iStart + i] = new Color32(red, red, red, 255);
                    }
                    break;
                case 15:
                case 16:
                    if (grayscale) { //gray and alpha
                        for (int i = 0; i < length; i++) {
                            red = r.ReadByte();
                            alpha = r.ReadByte();
                            res[iStart + i] = new Color32(red, red, red, alpha);
                        }
                        break;
                    }
                    float mult = 255.0f / 31.0f;
                    for (int i = 0; i < length; i++) {
                        val = r.ReadInt16();
                        blue = (byte)(mult * (val & 0x1F));
                        red = (byte)(mult * ((val >> 10) & 0x1F));
                        green = (byte)(mult * ((val >> 5) & 0x1F));
                        alpha = alpha16 && ((val >> 15) & 1) == 0 ? (byte)0 : (byte)255;
                        res[iStart + i] = new Color32(red, green, blue, alpha);
                    }
                    break;
                case 24:
                    for (int i = 0; i < length; i++) {
                        blue = r.ReadByte();
                        green = r.ReadByte();
                        red = r.ReadByte();
                        res[iStart + i] = new Color32(red, green, blue, 255);
                    }
                    break;
                case 32:
                    for (int i = 0; i < length; i++) {
                        blue = r.ReadByte();
                        green = r.ReadByte();
                        red = r.ReadByte();
                        alpha = r.ReadByte();
                        res[iStart + i] = new Color32(red, green, blue, alpha);
                    }
                    break;
                default:
                    throw new InvalidDataException("Unsupported TGA bit depth: " + depth);
            }
        }

        Color32[] ReadPixels(BufferedBinaryReader r, int length, int depth, bool grayscale, bool alpha16) {
            Color32[] res = new Color32[length];
            ReadPixels(r, res, 0, length, depth, grayscale, alpha16);
            return res;
        }

        Color32[] ReadPixelsRLE(BufferedBinaryReader r, int length, int depth, bool grayscale, bool alpha16) {
            Color32[] res = new Color32[length];
            Color32[] resS = new Color32[1];
            int count = 0;
            while (count < length) {
                byte header = r.ReadByte();
                int type = header & 0x80;
                int number = System.Math.Min(header & 0x7F, length - count - 1); //packets running past the image are cut
                if (type == 0) {
                    ReadPixels(r, res, count, number + 1, depth, grayscale, alpha16);
                } else {
                    ReadPixels(r, resS, 0, 1, depth, grayscale, alpha16);
                    for (int i = 0; i <= number; i++) {
                        res[count + i] = resS[0];
                    }
                }
                count += number + 1;
            }
            return res;
        }

        Color32[] ReadMappedPixelsRLE(BufferedBinaryReader r, int length, int colorMapOrigin, int bitsPerPixel, Color32[] colorMap) {
            Color32[] res = new Color32[length];
            Color32[] resS = new Color32[1];
            int count = 0;
            while (count < length) {
                byte header = r.ReadByte();
                int type = header & 0x80;
                int number = System.Math.Min(header & 0x7F, length - count - 1); //packets running past the image are cut
                if (type == 0) {
                    ReadMappedPixels(r, res, count, number + 1, colorMapOrigin, bitsPerPixel, colorMap);
                } else {
                    ReadMappedPixels(r, resS, 0, 1, colorMapOrigin, bitsPerPixel, colorMap);
                    for (int i = 0; i <= number; i++) {
                        res[count + i] = resS[0];
                    }
                }
                count += number + 1;
            }
            return res;
        }

        //the color map holds the entries starting from colorMapOrigin, other indices are black
        static Color32 GetMapEntry(Color32[] colorMap, int index) {
            return index >= 0 && index < colorMap.Length ? colorMap[index] : new Color32(0, 0, 0, 255);
        }

        void ReadMappedPixels(BufferedBinaryReader r, Color32[] res, int iStart, int length, int colorMapOrigin, int bitsPerPixel, Color32[] colorMap) {
            if (bitsPerPixel == 16) {
                for (int i = 0; i < length; i++) {
                    ushort val = r.ReadUInt16();
                    res[iStart + i] = GetMapEntry(colorMap, val - colorMapOrigin);
                }
            } else {
                for (int i = 0; i < length; i++) {
                    byte val = r.ReadByte();
                    res[iStart + i] = GetMapEntry(colorMap, val - colorMapOrigin);
                }
            }
        }

        Color32[] ReadMappedPixels(BufferedBinaryReader r, int length, int colorMapOrigin, int bitsPerPixel, Color32[] colorMap) {
            Color32[] res = new Color32[length];
            ReadMappedPixels(r, res, 0, length, colorMapOrigin, bitsPerPixel, colorMap);
            return res;
        }

        Color32[] FlipPixels(Color32[] pixels, int width, int height, bool horizontal, bool vertical) {
            Color32[] res = new Color32[pixels.Length];
            for (int y = 0; y < height; y++) {
                int y2 = vertical ? (height - 1) - y : y;
                for (int x = 0; x < width; x++) {
                    int x2 = horizontal ? (width - 1) - x : x;
                    res[y2 * width + x2] = pixels[y * width + x];
                }
            }
            return res;
        }

        public Texture2D DecodeTexture(BufferedBinaryReader r) {
            byte idLength = r.ReadByte();
            byte colorMapType = r.ReadByte();
            byte imageType = r.ReadByte();
            ushort colorMapOrigin = r.ReadUInt16();
            ushort colorMapLength = r.ReadUInt16();
            byte colorMapDepth = r.ReadByte();
            short xOrigin = r.ReadInt16();
            short yOrigin = r.ReadInt16();
            ushort width = r.ReadUInt16();
            ushort height = r.ReadUInt16();
            byte bitsPerPixel = r.ReadByte();
            byte imageDescriptor = r.ReadByte();
            int colorMapBytes = colorMapType == 1 ? colorMapLength * ((colorMapDepth + 7) / 8) : 0;
            r.ReadBytes(idLength); //skip identification field
            if (width == 0 || height == 0) throw new InvalidDataException("Invalid TGA size: " + width + "x" + height);
            bool grayscale = imageType == 3 || imageType == 11;
            bool alpha16 = (imageDescriptor & 0x0F) > 0; //alpha bits count, 16 bit images use 1
            bool withAlpha;
            Color32[] colorMap, pixels;
            switch (imageType) {
                case 0: //no image data
                    throw new InvalidDataException("TGA file without image data");
                case 1: //color mapped, uncompressed
                case 9: //color mapped, rle encoded
                    if (colorMapType != 1) throw new InvalidDataException("Color mapped TGA without color map");
                    if (bitsPerPixel != 8 && bitsPerPixel != 16) throw new InvalidDataException("Unsupported TGA color map index size: " + bitsPerPixel);
                    colorMap = ReadPixels(r, colorMapLength, colorMapDepth, false, false);
                    pixels = imageType == 1
                        ? ReadMappedPixels(r, width * height, colorMapOrigin, bitsPerPixel, colorMap)
                        : ReadMappedPixelsRLE(r, width * height, colorMapOrigin, bitsPerPixel, colorMap);
                    withAlpha = colorMapDepth == 32;
                    break;
                case 2: //true color, uncompressed
                case 3: //grayscale, uncompressed
                case 10: //true color, rle encoded
                case 11: //grayscale, rle encoded
                    if (grayscale ? bitsPerPixel != 8 && bitsPerPixel != 16 : bitsPerPixel == 8) throw new InvalidDataException("Unsupported TGA bit depth: " + bitsPerPixel);
                    r.ReadBytes(colorMapBytes); //skip color map if present
                    pixels = imageType == 2 || imageType == 3
                        ? ReadPixels(r, width * height, bitsPerPixel, grayscale, alpha16)
                        : ReadPixelsRLE(r, width * height, bitsPerPixel, grayscale, alpha16);
                    withAlpha = bitsPerPixel == 32 || (grayscale && bitsPerPixel == 16);
                    if (!grayscale && bitsPerPixel <= 16 && alpha16) { //alpha bit used only if set somewhere (often always 0)
                        foreach (var p in pixels) if (p.a != 0) { withAlpha = true; break; }
                    }
                    break;
                default:
                    throw new InvalidDataException("Unsupported TGA image type: " + imageType);
            }
            //the pixel order is given by the image descriptor (bit 4: right to left, bit 5: top to bottom), not by the
            //origin fields, textures are stored left to right, bottom to top
            bool rightToLeft = (imageDescriptor & 0x10) != 0;
            bool topToBottom = (imageDescriptor & 0x20) != 0;
            if (rightToLeft || topToBottom) pixels = FlipPixels(pixels, width, height, rightToLeft, topToBottom);
            var format = withAlpha ? GameEngine.Texture.Format.RGBA8 : GameEngine.Texture.Format.RGB8;
            var tex = GameEngine.Texture.CreateFromData(width, height, format, true, pixels);
            return tex;
        }
    }
}
