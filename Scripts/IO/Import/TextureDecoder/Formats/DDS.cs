using System.IO;
using Godot;

namespace TextureDecoder.Formats {
    class DDS : TextureDecoder //only supports dxt1, dxt3 and dxt5 (bc1, bc2 and bc3 with the dx10 header)
    {
        const int DDPF_FOURCC = 0x4;
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

            if ((ddpfFlags & DDPF_FOURCC) == 0) throw new InvalidDataException("Uncompressed DDS files are not supported");
            if ((dwCaps2 & DDSCAPS2_VOLUME) != 0) throw new InvalidDataException("Volume DDS textures are not supported");
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
    }
}
