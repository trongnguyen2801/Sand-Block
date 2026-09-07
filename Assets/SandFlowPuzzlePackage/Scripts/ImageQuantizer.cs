using UnityEngine;
using System.Collections.Generic;

namespace SandFlowPuzzle
{
    public static class ImageQuantizer
    {
        /// <summary>
        /// Quantize a source image into a targetSize x targetSize grid with colorCount dominant colors.
        /// Returns (grid, palette) where grid values are 1-based palette indices (0 = empty/transparent).
        /// </summary>
        public static (byte[] grid, Color[] palette) Quantize(Texture2D source, int targetSize, int colorCount, int seed)
        {
            // Read source pixels
            Color[] sourcePixels = GetReadablePixels(source);
            int srcW = source.width;
            int srcH = source.height;

            // Resize to targetSize x targetSize via bilinear sampling
            Color[] resized = new Color[targetSize * targetSize];
            for (int y = 0; y < targetSize; y++)
            {
                for (int x = 0; x < targetSize; x++)
                {
                    float u = (float)x / (targetSize - 1);
                    float v = 1f - (float)y / (targetSize - 1); // flip V: grid Y=0 is top, texture Y=0 is bottom
                    resized[y * targetSize + x] = SampleBilinear(sourcePixels, srcW, srcH, u, v);
                }
            }

            // Determine fill color for transparent/near-black pixels:
            // Compute average brightness of visible pixels, then pick the opposite for contrast.
            float brightnessSum = 0f;
            int visibleCount = 0;
            for (int i = 0; i < resized.Length; i++)
            {
                Color c = resized[i];
                if (c.a < 0.1f) continue;
                float br = c.r * 0.299f + c.g * 0.587f + c.b * 0.114f;
                if (br < 0.02f) continue;
                brightnessSum += br;
                visibleCount++;
            }
            // If average brightness >= 0.5 → fill with black for contrast, otherwise white
            float fillValue = (visibleCount > 0 && brightnessSum / visibleCount >= 0.5f) ? 0f : 1f;

            // Collect pixels for clustering; replace transparent/near-black with the chosen fill color
            List<Vector3> pixelColors = new List<Vector3>();
            List<int> pixelIndices = new List<int>();

            for (int i = 0; i < resized.Length; i++)
            {
                Color c = resized[i];
                if (c.a < 0.1f)
                {
                    c = new Color(fillValue, fillValue, fillValue, 1f);
                    resized[i] = c;
                }
                float brightness = c.r * 0.299f + c.g * 0.587f + c.b * 0.114f;
                if (brightness < 0.02f)
                {
                    c = new Color(fillValue, fillValue, fillValue, 1f);
                    resized[i] = c;
                }

                pixelColors.Add(new Vector3(c.r, c.g, c.b));
                pixelIndices.Add(i);
            }

            if (pixelColors.Count == 0)
            {
                // All transparent/black — return empty grid
                return (new byte[targetSize * targetSize], new Color[0]);
            }

            // K-means clustering
            int k = Mathf.Min(colorCount, pixelColors.Count);
            Vector3[] centroids = InitializeCentroids(pixelColors, k, seed);
            int[] assignments = new int[pixelColors.Count];

            for (int iter = 0; iter < 15; iter++)
            {
                // Assign pixels to nearest centroid
                for (int i = 0; i < pixelColors.Count; i++)
                {
                    float bestDist = float.MaxValue;
                    int bestIdx = 0;
                    for (int c = 0; c < k; c++)
                    {
                        float dist = (pixelColors[i] - centroids[c]).sqrMagnitude;
                        if (dist < bestDist)
                        {
                            bestDist = dist;
                            bestIdx = c;
                        }
                    }
                    assignments[i] = bestIdx;
                }

                // Recalculate centroids
                Vector3[] sums = new Vector3[k];
                int[] counts = new int[k];
                for (int i = 0; i < pixelColors.Count; i++)
                {
                    sums[assignments[i]] += pixelColors[i];
                    counts[assignments[i]]++;
                }
                for (int c = 0; c < k; c++)
                {
                    if (counts[c] > 0)
                        centroids[c] = sums[c] / counts[c];
                }
            }

            // Build palette from final centroids
            Color[] palette = new Color[k];
            for (int c = 0; c < k; c++)
                palette[c] = new Color(centroids[c].x, centroids[c].y, centroids[c].z, 1f);

            // Map each pixel to nearest centroid index (1-based, 0 = empty for transparent)
            byte[] grid = new byte[targetSize * targetSize];
            for (int i = 0; i < pixelColors.Count; i++)
            {
                // assignments[i] is 0-based centroid index, grid stores 1-based
                grid[pixelIndices[i]] = (byte)(assignments[i] + 1);
            }

            return (grid, palette);
        }

        /// <summary>
        /// Re-quantize with a new random seed (for re-rolling colors).
        /// </summary>
        public static (byte[] grid, Color[] palette) ReQuantize(Texture2D source, int targetSize, int colorCount, int newSeed)
        {
            return Quantize(source, targetSize, colorCount, newSeed);
        }

        private static Vector3[] InitializeCentroids(List<Vector3> pixels, int k, int seed)
        {
            // K-means++ initialization with seed-based random
            System.Random rng = new System.Random(seed);
            Vector3[] centroids = new Vector3[k];

            // First centroid: random pixel
            centroids[0] = pixels[rng.Next(pixels.Count)];

            for (int c = 1; c < k; c++)
            {
                // Calculate distance to nearest existing centroid for each pixel
                float[] distances = new float[pixels.Count];
                float totalDist = 0f;
                for (int i = 0; i < pixels.Count; i++)
                {
                    float minDist = float.MaxValue;
                    for (int j = 0; j < c; j++)
                    {
                        float d = (pixels[i] - centroids[j]).sqrMagnitude;
                        if (d < minDist) minDist = d;
                    }
                    distances[i] = minDist;
                    totalDist += minDist;
                }

                // Weighted random selection
                float target = (float)(rng.NextDouble() * totalDist);
                float cumulative = 0f;
                int selected = 0;
                for (int i = 0; i < pixels.Count; i++)
                {
                    cumulative += distances[i];
                    if (cumulative >= target)
                    {
                        selected = i;
                        break;
                    }
                }
                centroids[c] = pixels[selected];
            }

            return centroids;
        }

        private static Color[] GetReadablePixels(Texture2D source)
        {
            // Try direct read first
            try
            {
                return source.GetPixels();
            }
            catch
            {
                // Texture not readable — copy via RenderTexture
                RenderTexture rt = RenderTexture.GetTemporary(source.width, source.height, 0);
                Graphics.Blit(source, rt);
                RenderTexture prev = RenderTexture.active;
                RenderTexture.active = rt;

                Texture2D readable = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false);
                readable.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0);
                readable.Apply();

                RenderTexture.active = prev;
                RenderTexture.ReleaseTemporary(rt);

                Color[] pixels = readable.GetPixels();
                Object.DestroyImmediate(readable);
                return pixels;
            }
        }

        private static Color SampleBilinear(Color[] pixels, int width, int height, float u, float v)
        {
            float x = u * (width - 1);
            float y = v * (height - 1);

            int x0 = Mathf.FloorToInt(x);
            int y0 = Mathf.FloorToInt(y);
            int x1 = Mathf.Min(x0 + 1, width - 1);
            int y1 = Mathf.Min(y0 + 1, height - 1);

            float fx = x - x0;
            float fy = y - y0;

            Color c00 = pixels[y0 * width + x0];
            Color c10 = pixels[y0 * width + x1];
            Color c01 = pixels[y1 * width + x0];
            Color c11 = pixels[y1 * width + x1];

            Color top = Color.Lerp(c00, c10, fx);
            Color bottom = Color.Lerp(c01, c11, fx);
            return Color.Lerp(top, bottom, fy);
        }
    }
}
