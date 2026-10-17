using System.IO;
using Godot;

namespace TextureDecoder {
    interface TextureDecoder {
        Texture2D DecodeTexture(BufferedBinaryReader r);
    }
}