using System.IO;
using Godot;
using GameObject = Godot.Node3D;

namespace MeshDecoder {
    interface MeshDecoder {
        GameObject DecodeMesh(BufferedBinaryReader r, string filename);
    }
}