/*
 * Copyright (c) 2019 Dummiesman
 *
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to deal
 * in the Software without restriction, including without limitation the rights
 * to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the Software is
 * furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in
 * all copies or substantial portions of the Software.
*/

using System.Globalization;
using Godot;
using Material = Godot.ShaderMaterial;

// Godot port of the (customized) Dummiesman's OBJ Importer
// (see res://CustomPackages/OBJImport/LICENSE.txt).
// The loader produces program coordinates (X axis flipped compared to the OBJ file), like all the logic code,
// GameEngine.Mesh converts them to Godot's when the mesh data is assigned.
namespace Dummiesman {
    public static class OBJLoaderHelper {
        /// <summary>
        /// Enables transparency mode on standard materials
        /// </summary>
        public static void EnableMaterialTransparency(Material mtl) {
            MaterialManager.SetBlendMode(mtl, 2); //fade
        }

        /// <summary>
        /// Modified from https://codereview.stackexchange.com/a/76891. Faster than float.Parse
        /// </summary>
        public static float FastFloatParse(string input) {
            if (input.Contains('e') || input.Contains('E'))
                return float.Parse(input, CultureInfo.InvariantCulture);

            float result = 0;
            int pos = 0;
            int len = input.Length;

            if (len == 0) return float.NaN;
            char c = input[0];
            float sign = 1;
            if (c == '-') {
                sign = -1;
                ++pos;
                if (pos >= len) return float.NaN;
            }

            while (true) { // breaks inside on pos >= len or non-digit character
                if (pos >= len) return sign * result;
                c = input[pos++];
                if (c < '0' || c > '9') break;
                result = (result * 10.0f) + (c - '0');
            }

            if (c != '.' && c != ',') return SlowFloatParse(input);
            float exp = 0.1f;
            while (pos < len) {
                c = input[pos++];
                if (c < '0' || c > '9') return SlowFloatParse(input);
                result += (c - '0') * exp;
                exp *= 0.1f;
            }
            return sign * result;
        }

        static float SlowFloatParse(string input) {
            return float.TryParse(input, NumberStyles.Float, CultureInfo.InvariantCulture, out var res) ? res : float.NaN;
        }

        /// <summary>
        /// Modified from http://cc.davelozinski.com/c-sharp/fastest-way-to-convert-a-string-to-an-int. Faster than int.Parse
        /// </summary>
        public static int FastIntParse(string input) {
            int result = 0;
            bool isNegative = (input[0] == '-');

            for (int i = (isNegative) ? 1 : 0; i < input.Length; i++)
                result = result * 10 + (input[i] - '0');
            return (isNegative) ? -result : result;
        }

        public static Material CreateNullMaterial() {
            return GameEngine.Materials.CreateMaterial(GameEngine.Materials.GetShader("Standard (Specular setup)"), true);
        }

        // Positions and normals ("v x y z [w | r g b]", "vn x y z")
        public static Vector3 VectorFromStrArray(string[] cmps) {
            float x = FastFloatParse(cmps[1]);
            float y = cmps.Length > 2 ? FastFloatParse(cmps[2]) : 0f;
            float z = cmps.Length > 3 ? FastFloatParse(cmps[3]) : 0f;
            return new Vector3(-x, y, z); //CHANGED: FLIPPED X AXIS to avoid having to scale
        }

        // Texture coordinates ("vt u [v [w]]")
        public static Vector2 UVFromStrArray(string[] cmps) {
            float u = FastFloatParse(cmps[1]);
            float v = cmps.Length > 2 ? FastFloatParse(cmps[2]) : 0f;
            return new Vector2(u, v);
        }

        public static Color ColorFromStrArray(string[] cmps, float scalar = 1.0f) {
            float Kr = FastFloatParse(cmps[1]) * scalar;
            float Kg = FastFloatParse(cmps[2]) * scalar;
            float Kb = FastFloatParse(cmps[3]) * scalar;
            return new Color(Kr, Kg, Kb);
        }
    }
}
