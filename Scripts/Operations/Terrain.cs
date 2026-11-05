using Godot;

namespace Operations {
    //Editing of the terrain heights
    public static class Terrain {
        //the values match the modifiers of the terrain editor
        public enum BrushMode {
            None = 0,
            Raise = 1,
            Lower = 2,
            Level = 3
        }

        //the brush of the terrain editor. levelHeight: the height to level to, null for the height under the brush center
        public static TerrainModifier.TerrainModifier CreateBrush(CityGroundHelper helper, BrushMode mode, float range, float intensity, bool singleStep, float? levelHeight) {
            switch (mode) {
                case BrushMode.Raise:
                    return new TerrainModifier.RaiseFlat(helper, range, intensity, singleStep);
                case BrushMode.Lower:
                    return new TerrainModifier.RaiseFlat(helper, range, -intensity, singleStep);
                case BrushMode.Level:
                    return new TerrainModifier.Level(helper, range, intensity * intensity, singleStep, levelHeight.HasValue, levelHeight ?? 0);
                default:
                    return null;
            }
        }

        //one step of the brush (the same as a click with the continuous mode off)
        public static bool ApplyBrush(CityGroundHelper helper, BrushMode mode, Vector3 position, float range, float intensity, float? levelHeight) {
            if (range < 0) range = 0;
            var brush = CreateBrush(helper, mode, range, intensity, true, levelHeight);
            if (brush == null) return false;
            brush.ApplyAt(position, brush.intensity);
            return true;
        }

        //the terrain height under the point (program coordinates), from above
        public static float GetHeight(Vector3 position) {
            return GeometryHelper.ProjectPoint(position).Y;
        }

        // The heightmap grid: the sample (x, z) is at (x * pixelSize - size / 2, z * pixelSize - size / 2) in the city

        public static int GetGridSize(CityGroundHelper helper) {
            return GameEngine.Terrain.GetHeightmapResolution(helper.terrainObj);
        }

        public static float GetPixelSize(CityGroundHelper helper) {
            return GameEngine.Terrain.GetPixelSize(helper.terrainObj);
        }

        //the heightmap stores 0-1 values, 0.5 being the height 0
        static float ToMeters(float value) {
            return (value - 0.5f) * 2 * CityGroundHelper.maxHeight;
        }

        static float ToValue(float meters) {
            return 0.5f + meters / (CityGroundHelper.maxHeight * 2);
        }

        //Float32 normalized samples between 0.5 and 1 are 2^-24 apart. Below zero the spacing is finer.
        public static float GetHeightStep(float maxHeight) {
            return maxHeight / 8388608f;
        }

        //Changes the vertical range. Keeping the normalized samples scales elevations around zero; otherwise
        //re-encode them in the new range, rejecting a range that would clip the terrain before changing anything.
        public static void RescaleHeightmap(CityGroundHelper helper, float maxHeight, bool rescaleHeights) {
            if (!float.IsFinite(maxHeight * 2) || maxHeight <= 0) throw new System.ArgumentException("maxHeight must be positive and its full range finite");
            var previousMaxHeight = CityGroundHelper.maxHeight;
            if (maxHeight == previousMaxHeight) return;
            var size = GetGridSize(helper);
            var heights = GameEngine.Terrain.GetHeights(helper.terrainObj, 0, 0, size, size);
            for (int z = 0; z < size; z++) {
                for (int x = 0; x < size; x++) {
                    var value = heights[z, x];
                    if (!float.IsFinite(value)) throw new System.ArgumentException("the heightmap contains non-finite samples");
                    //Imported maps can contain values outside 0-1; use the same clamping as the terrain renderer.
                    value = Mathf.Clamp(value, 0, 1);
                    if (!rescaleHeights) {
                        var meters = (value - 0.5) * 2 * previousMaxHeight;
                        if (System.Math.Abs(meters) > maxHeight) {
                            throw new System.ArgumentException("the new range would clip existing terrain; choose a larger maximum height or rescale the heights");
                        }
                        value = (float)(0.5 + meters / (2.0 * maxHeight));
                    }
                    heights[z, x] = value;
                }
            }
            History.UndoManager.TouchWholeHeightmap();
            CityGroundHelper.maxHeight = maxHeight;
            GameEngine.Terrain.SetHeightmap(helper.terrainObj, maxHeight, heights);
            helper.elementManager.worldChanged = true;
        }

        //the heights (in meters) of an area of the grid, indexed [z, x]
        public static float[,] GetHeights(CityGroundHelper helper, int x, int z, int width, int depth) {
            var res = GameEngine.Terrain.GetHeights(helper.terrainObj, x, z, width, depth);
            for (int i = 0; i < depth; i++) {
                for (int j = 0; j < width; j++) res[i, j] = ToMeters(res[i, j]);
            }
            return res;
        }

        //sets the heights (in meters) of an area of the grid, indexed [z, x], as a terrain edit; returns how many were
        //out of the range of the terrain (-maxHeight to maxHeight) and clamped
        public static int SetHeights(CityGroundHelper helper, int x, int z, float[,] heights) {
            var depth = heights.GetLength(0);
            var width = heights.GetLength(1);
            var values = new float[depth, width];
            var clamped = 0;
            for (int i = 0; i < depth; i++) {
                for (int j = 0; j < width; j++) {
                    var v = ToValue(heights[i, j]);
                    if (v < 0 || v > 1) clamped++;
                    values[i, j] = Mathf.Clamp(v, 0, 1);
                }
            }
            History.UndoManager.TouchHeights(x, z, width, depth);
            helper.elementManager.worldChanged = true;
            GameEngine.Terrain.SetHeightsDelayLOD(helper.terrainObj, x, z, values);
            return clamped;
        }
    }
}
