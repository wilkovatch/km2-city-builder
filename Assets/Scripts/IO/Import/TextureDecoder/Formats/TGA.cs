using UnityEngine;

namespace TextureDecoder.Formats {
    public class TGA : TextureDecoder {
        void ReadPixels(BufferedBinaryReader r, Color32[] res, int iStart, int length, int depth) {
            byte blue, green, red, alpha;
            short val;
            switch (depth) {
                case 8:
                    for (int i = 0; i < length; i++) {
                        red = r.ReadByte();
                        res[iStart + i] = new Color32(red, red, red, 255);
                    }
                    break;
                case 16:
                    float mult = 255.0f / 31.0f;
                    for (int i = 0; i < length; i++) {
                        val = r.ReadInt16();
                        blue = (byte)(mult * (val & 0x1F));
                        red = (byte)(mult * ((val >> 10) & 0x1F));
                        green = (byte)(mult * ((val >> 5) & 0x1F));
                        //alpha = (byte)(255 * ((val >> 15) & 0x1F)); //always 0 in test files...
                        res[iStart + i] = new Color32(red, green, blue, 255);
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
            }
        }

        Color32[] ReadPixels(BufferedBinaryReader r, int length, int depth) {
            Color32[] res = new Color32[length];
            ReadPixels(r, res, 0, length, depth);
            return res;
        }

        Color32[] ReadPixelsRLE(BufferedBinaryReader r, int length, int depth) {
            Color32[] res = new Color32[length];
            Color32[] resS = new Color32[1];
            int count = 0;
            while (count < length) {
                byte header = r.ReadByte();
                int type = header & 0x80;
                int number = header & 0x7F;
                if (type == 0) {
                    ReadPixels(r, res, count, number + 1, depth);
                } else {
                    ReadPixels(r, resS, 0, 1, depth);
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
                int number = header & 0x7F;
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

        void ReadMappedPixels(BufferedBinaryReader r, Color32[] res, int iStart, int length, int colorMapOrigin, int bitsPerPixel, Color32[] colorMap) {
            if (bitsPerPixel == 16) {
                for (int i = 0; i < length; i++) {
                    ushort val = r.ReadUInt16();
                    res[iStart + i] = colorMap[colorMapOrigin + val];
                }
            } else {
                for (int i = 0; i < length; i++) {
                    byte val = r.ReadByte();
                    res[iStart + i] = colorMap[colorMapOrigin + val];
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
            short colorMapOrigin = r.ReadInt16();
            short colorMapLength = r.ReadInt16();
            byte colorMapDepth = r.ReadByte();
            short xOrigin = r.ReadInt16();
            short yOrigin = r.ReadInt16();
            short width = r.ReadInt16();
            short height = r.ReadInt16();
            byte bitsPerPixel = r.ReadByte();
            byte imageDescriptor = r.ReadByte();
            int colorMapBytes = colorMapLength * colorMapDepth / 3;
            r.ReadBytes(idLength); //skip identification field
            bool withAlpha = bitsPerPixel == 32;
            TextureFormat format = withAlpha ? TextureFormat.RGBA32 : TextureFormat.RGB24;
            Texture2D tex = new Texture2D(width, height, format, true);
            Color32[] colorMap, pixels = new Color32[1];
            switch (imageType) {
                case 0: //no image data
                    break;
                case 1: //color mapped, uncompressed
                    colorMap = ReadPixels(r, colorMapLength, colorMapDepth);
                    pixels = ReadMappedPixels(r, width * height, colorMapOrigin, bitsPerPixel, colorMap);
                    break;
                case 2: //true color, uncompressed
                case 3: //grayscale, uncompressed
                    r.ReadBytes(colorMapBytes); //skip color map if present
                    pixels = ReadPixels(r, width * height, bitsPerPixel);
                    break;
                case 9: //color mapped, rle encoded
                    colorMap = ReadPixels(r, colorMapLength, colorMapDepth);
                    pixels = ReadMappedPixelsRLE(r, width * height, colorMapOrigin, bitsPerPixel, colorMap);
                    break;
                case 10: //true color, rle encoded
                case 11: //grayscale, rle encoded
                    r.ReadBytes(colorMapBytes); //skip color map if present
                    pixels = ReadPixelsRLE(r, width * height, bitsPerPixel);
                    break;
            }
            if (xOrigin > 0 || yOrigin > 0) pixels = FlipPixels(pixels, width, height, xOrigin > 0, yOrigin > 0);
            tex.SetPixels32(pixels);
            tex.Apply();
            return tex;
        }
    }
}