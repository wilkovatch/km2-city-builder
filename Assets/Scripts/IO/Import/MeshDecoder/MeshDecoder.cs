using System.IO;
using UnityEngine;

namespace MeshDecoder {
    interface MeshDecoder {
        GameObject DecodeMesh(BufferedBinaryReader r, string filename);
    }
}