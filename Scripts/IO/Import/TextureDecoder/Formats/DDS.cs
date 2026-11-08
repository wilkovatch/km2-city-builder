using System.IO;
using Godot;

namespace TextureDecoder.Formats {
    class DDS : TextureDecoder //supports dxt1, dxt3 and dxt5 (bc1, bc2 and bc3 with the dx10 header) and uncompressed rgb(a)/luminance with bit masks
    {
        const int DDSD_PITCH = 0x8;
        const int DDPF_ALPHAPIXELS = 0x1;
        const int DDPF_FOURCC = 0x4;
        const int DDPF_RGB = 0x40;
        const int DDPF_LUMINANCE = 0x20000;
        const int DDSCAPS2_VOLUME = 0x200000;
        const int D3D10_RESOURCE_DIMENSION_TEXTURE2D = 3;

        public Texture2D DecodeTexture(BufferedBinaryReader r) {
            int dwMagic = r.ReadInt32();
            int dwSize = r.ReadInt32();
            if (dwMagic != 0x20534444 || dwSize != 124) throw new InvalidDataException("Not a DDS file");
            int dwFlags = r.ReadInt32();
            int dwHeight = r.ReadInt32();
            int dwWidth = r.ReadInt32();
            int dwPitchOrLinearSize = r.ReadInt32();
            int dwDepth = r.ReadInt32();
            int dwMipmapCount = r.ReadInt32();
            byte[] dwReserved1 = r.ReadBytes(4 * 11);
            int ddpfSize = r.ReadInt32();
            int ddpfFlags = r.ReadInt32();
            string fourCC = System.Text.Encoding.ASCII.GetString(r.ReadBytes(4));
            byte[] ddpfMasks = r.ReadBytes(20); //rgb bit count and bit masks
            int dwCaps = r.ReadInt32();
            int dwCaps2 = r.ReadInt32();
            byte[] ddsCapsB = r.ReadBytes(8);
            int dwReserved2 = r.ReadInt32();
            int headerLength = 128;

            if ((dwCaps2 & DDSCAPS2_VOLUME) != 0) throw new InvalidDataException("Volume DDS textures are not supported");
            if ((ddpfFlags & DDPF_FOURCC) == 0) {
                return DecodeUncompressed(r, dwFlags, dwWidth, dwHeight, dwPitchOrLinearSize, ddpfFlags, ddpfMasks);
            }
            GameEngine.Texture.Format dxtFormat;
            switch (fourCC) {
                case "DXT1": dxtFormat = GameEngine.Texture.Format.DXT1; break;
                case "DXT2": //premultiplied alpha, loaded as is
                case "DXT3": dxtFormat = GameEngine.Texture.Format.DXT3; break;
                case "DXT4": //premultiplied alpha, loaded as is
                case "DXT5": dxtFormat = GameEngine.Texture.Format.DXT5; break;
                case "DX10":
                    int dxgiFormat = r.ReadInt32();
                    int resourceDimension = r.ReadInt32();
                    r.ReadBytes(12); //misc flag, array size, misc flags 2
                    headerLength += 20;
                    if (resourceDimension != D3D10_RESOURCE_DIMENSION_TEXTURE2D) throw new InvalidDataException("Only 2D DDS textures are supported");
                    switch (dxgiFormat) {
                        case 70: case 71: case 72: dxtFormat = GameEngine.Texture.Format.DXT1; break; //BC1 typeless, unorm, unorm srgb
                        case 73: case 74: case 75: dxtFormat = GameEngine.Texture.Format.DXT3; break; //BC2
                        case 76: case 77: case 78: dxtFormat = GameEngine.Texture.Format.DXT5; break; //BC3
                        default: throw new InvalidDataException("Unsupported DDS DXGI format: " + dxgiFormat);
                    }
                    break;
                default:
                    throw new InvalidDataException("Unsupported DDS format: " + fourCC.TrimEnd('\0'));
            }

            //a count of 0 or 1 means no mipmaps, arrays and cube maps keep their first image (which comes first in the file)
            byte[] dxtBytes = r.ReadBytes(r.GetLength() - headerLength);
            return GameEngine.Texture.CreateFromRawData(dwWidth, dwHeight, dxtFormat, dwMipmapCount, dxtBytes);
        }

        //only the base level is read (the mipmaps are generated); the rows keep the file order like the dxt data
        static Texture2D DecodeUncompressed(BufferedBinaryReader r, int flags, int width, int height, int pitch, int pfFlags, byte[] masks) {
            int bitCount = System.BitConverter.ToInt32(masks, 0);
            if ((pfFlags & (DDPF_RGB | DDPF_LUMINANCE)) == 0) throw new InvalidDataException("Unsupported uncompressed DDS pixel format: " + pfFlags);
            if (bitCount != 8 && bitCount != 16 && bitCount != 24 && bitCount != 32) throw new InvalidDataException("Unsupported DDS bit count: " + bitCount);
            if (width <= 0 || height <= 0) throw new InvalidDataException("Invalid DDS size: " + width + "x" + height);
            bool luminance = (pfFlags & DDPF_LUMINANCE) != 0;
            var red = new MaskChannel(System.BitConverter.ToUInt32(masks, 4));
            var green = new MaskChannel(luminance ? 0 : System.BitConverter.ToUInt32(masks, 8));
            var blue = new MaskChannel(luminance ? 0 : System.BitConverter.ToUInt32(masks, 12));
            var alpha = new MaskChannel((pfFlags & DDPF_ALPHAPIXELS) != 0 ? System.BitConverter.ToUInt32(masks, 16) : 0);
            int bytesPerPixel = bitCount / 8;
            int stride = (flags & DDSD_PITCH) != 0 && pitch >= width * bytesPerPixel ? pitch : width * bytesPerPixel;
            if ((long)stride * height > r.GetLength() - 128) throw new InvalidDataException("DDS pixel data is truncated");

            int channels = alpha.Present ? 4 : 3;
            byte[] pixels = new byte[channels * width * height];
            for (int y = 0, o = 0; y < height; y++) {
                byte[] row = r.ReadBytes(stride);
                for (int x = 0; x < width; x++, o += channels) {
                    uint pixel = 0;
                    for (int b = 0; b < bytesPerPixel; b++) pixel |= (uint)row[x * bytesPerPixel + b] << (8 * b);
                    byte rv = red.Get(pixel);
                    pixels[o + 0] = rv;
                    pixels[o + 1] = luminance ? rv : green.Get(pixel);
                    pixels[o + 2] = luminance ? rv : blue.Get(pixel);
                    if (channels == 4) pixels[o + 3] = alpha.Get(pixel);
                }
            }
            var format = channels == 4 ? GameEngine.Texture.Format.RGBA8 : GameEngine.Texture.Format.RGB8;
            return GameEngine.Texture.CreateFromPixelData(width, height, format, true, pixels);
        }
    }
}
