using System.IO;
using UnityEngine;

namespace TextureDecoder {
    interface TextureDecoder {
        Texture2D DecodeTexture(BufferedBinaryReader r);
    }
}