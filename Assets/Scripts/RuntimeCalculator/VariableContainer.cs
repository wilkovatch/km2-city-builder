using System;
using System.Collections.Generic;
using UnityEngine;

namespace RuntimeCalculator {
    public class VariableContainer {
        public float[] floats;
        public Vector3[] vector3s;
        public Vector2[] vector2s;
        public Dictionary<string, int> floatIndex;
        public Dictionary<string, int> vec3Index;
        public Dictionary<string, int> vec2Index;

        public VariableContainer(List<(string, Type)> definitions) {
            //count
            int floatCount = 0;
            int vector3Count = 0;
            int vector2Count = 0;
            foreach (var (k, v) in definitions) {
                if (v == typeof(float)) floatCount++;
                else if (v == typeof(Vector3)) vector3Count++;
                else if (v == typeof(Vector2)) vector2Count++;
            }

            //create
            floats = new float[floatCount];
            vector3s = new Vector3[vector3Count];
            vector2s = new Vector2[vector2Count];
            floatIndex = new Dictionary<string, int>();
            vec3Index = new Dictionary<string, int>();
            vec2Index = new Dictionary<string, int>();
            int curF = 0;
            int curV3 = 0;
            int curV2 = 0;
            foreach (var (k, v) in definitions) {
                if (v == typeof(float)) {
                    floatIndex[k] = curF;
                    curF++;
                } else if (v == typeof(Vector3)) {
                    vec3Index[k] = curV3;
                    curV3++;
                } else if (v == typeof(Vector2)) {
                    vec2Index[k] = curV2;
                    curV2++;
                }
            }
        }

        public VariableContainer GetClone() {
            return new VariableContainer(this);
        }

        //Creates a new container with the same structure as the original
        public VariableContainer(VariableContainer original) {
            floats = new float[original.floats.Length];
            vector3s = new Vector3[original.vector3s.Length];
            vector2s = new Vector2[original.vector2s.Length];
            floatIndex = new Dictionary<string, int>(original.floatIndex);
            vec3Index = new Dictionary<string, int>(original.vec3Index);
            vec2Index = new Dictionary<string, int>(original.vec2Index);
        }

        public void SetFloat(string name, float value) {
            floats[floatIndex[name]] = value;
        }

        public void SetVector3(string name, Vector3 value) {
            vector3s[vec3Index[name]] = value;
        }

        public void SetVector2(string name, Vector2 value) {
            vector2s[vec2Index[name]] = value;
        }

        public void SetFloat(int index, float value) {
            floats[index] = value;
        }

        public void SetVector3(int index, Vector3 value) {
            vector3s[index] = value;
        }

        public void SetVector2(int index, Vector2 value) {
            vector2s[index] = value;
        }
    }
}
