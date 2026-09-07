using UnityEngine;
using System.Collections.Generic;

namespace HypercasualGameEngine
{
    public static class MergedSDFGenerator
    {
        /// <summary>
        /// Generates an SDF texture that merges adjacent cells into smooth unified shapes
        /// Must match the coordinate system used by the mesh generation
        /// </summary>
        public static Texture2D GenerateMergedSDF(HoleDefinition def, List<Vector2Int> filledCells,
            int minX, int maxX, int minY, int maxY, int resolution = 512)
        {
            Texture2D tex = new Texture2D(resolution, resolution, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;

            // Use the SAME bounds calculation as the mesh generation
            float outlinePadding = def.cellSize * 0.25f;
            float boundsMinX = minX * def.cellSize - outlinePadding;
            float boundsMaxX = (maxX + 1) * def.cellSize + outlinePadding;
            float boundsMinZ = minY * def.cellSize - outlinePadding;
            float boundsMaxZ = (maxY + 1) * def.cellSize + outlinePadding;

            float width = boundsMaxX - boundsMinX;
            float height = boundsMaxZ - boundsMinZ;

            // Build a lookup set for filled cells
            HashSet<Vector2Int> filledSet = new HashSet<Vector2Int>(filledCells);

            Color[] pixels = new Color[resolution * resolution];

            for (int py = 0; py < resolution; py++)
            {
                for (int px = 0; px < resolution; px++)
                {
                    // UV coordinates (0-1)
                    float u = px / (float)(resolution - 1);
                    float v = py / (float)(resolution - 1);

                    // World space position (matching mesh UV mapping)
                    float worldX = boundsMinX + u * width;
                    float worldZ = boundsMinZ + v * height;
                    Vector2 point = new Vector2(worldX, worldZ);

                    // Calculate signed distance to the merged shape
                    float dist = CalculateDistanceToMergedShape(point, filledSet, def);

                    // Normalize distance to 0-1 range
                    // 0.5 = edge, >0.5 = inside, <0.5 = outside
                    float scale = def.cellSize * 0.6f;
                    float normalizedDist = 0.5f + (dist / scale);
                    normalizedDist = Mathf.Clamp01(normalizedDist);

                    pixels[py * resolution + px] = new Color(normalizedDist, normalizedDist, normalizedDist, 1);
                }
            }

            tex.SetPixels(pixels);
            tex.Apply();
            return tex;
        }

        /// <summary>
        /// Calculate signed distance to the merged shape formed by connected cells
        /// </summary>
        private static float CalculateDistanceToMergedShape(Vector2 point, HashSet<Vector2Int> filledCells, HoleDefinition def)
        {
            if (filledCells.Count == 0)
                return float.MaxValue; // Far outside

            // Determine which cell we're in
            int cellX = Mathf.FloorToInt(point.x / def.cellSize);
            int cellY = Mathf.FloorToInt(point.y / def.cellSize);
            Vector2Int currentCell = new Vector2Int(cellX, cellY);

            bool isInside = filledCells.Contains(currentCell);

            // Find minimum distance to any boundary edge
            float minDistance = float.MaxValue;

            // Check all filled cells and their edges
            foreach (var cell in filledCells)
            {
                // For each cell, check each of its 4 edges
                // Only consider edges where there's NO adjacent filled cell (i.e., boundary edges)

                float cellLeft = cell.x * def.cellSize;
                float cellRight = (cell.x + 1) * def.cellSize;
                float cellBottom = cell.y * def.cellSize;
                float cellTop = (cell.y + 1) * def.cellSize;

                // Left edge (x = cellLeft)
                if (!filledCells.Contains(new Vector2Int(cell.x - 1, cell.y)))
                {
                    float dist = DistanceToVerticalSegment(point, cellLeft, cellBottom, cellTop);
                    minDistance = Mathf.Min(minDistance, dist);
                }

                // Right edge (x = cellRight)
                if (!filledCells.Contains(new Vector2Int(cell.x + 1, cell.y)))
                {
                    float dist = DistanceToVerticalSegment(point, cellRight, cellBottom, cellTop);
                    minDistance = Mathf.Min(minDistance, dist);
                }

                // Bottom edge (y = cellBottom)
                if (!filledCells.Contains(new Vector2Int(cell.x, cell.y - 1)))
                {
                    float dist = DistanceToHorizontalSegment(point, cellBottom, cellLeft, cellRight);
                    minDistance = Mathf.Min(minDistance, dist);
                }

                // Top edge (y = cellTop)
                if (!filledCells.Contains(new Vector2Int(cell.x, cell.y + 1)))
                {
                    float dist = DistanceToHorizontalSegment(point, cellTop, cellLeft, cellRight);
                    minDistance = Mathf.Min(minDistance, dist);
                }
            }

            // Return signed distance: positive inside, negative outside
            return isInside ? minDistance : -minDistance;
        }

        /// <summary>
        /// Distance from point to a vertical line segment
        /// </summary>
        private static float DistanceToVerticalSegment(Vector2 point, float x, float yMin, float yMax)
        {
            float clampedY = Mathf.Clamp(point.y, yMin, yMax);
            Vector2 closestPoint = new Vector2(x, clampedY);
            return Vector2.Distance(point, closestPoint);
        }

        /// <summary>
        /// Distance from point to a horizontal line segment
        /// </summary>
        private static float DistanceToHorizontalSegment(Vector2 point, float y, float xMin, float xMax)
        {
            float clampedX = Mathf.Clamp(point.x, xMin, xMax);
            Vector2 closestPoint = new Vector2(clampedX, y);
            return Vector2.Distance(point, closestPoint);
        }
    }
}