using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

namespace SandFlowPuzzle
{
    // Expand only the rendering mesh: gameplay bounds and cell positions stay unchanged.
    public class SandGrainImage : RawImage
    {
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            base.OnPopulateMesh(vh);
            const float paddingCells = 0.5f;
            float scale = 1f + 2f * paddingCells / (texture != null ? texture.width : SandSimulator.GRID_SIZE);
            Vector2 center = GetPixelAdjustedRect().center;
            Vector2 uvCenter = uvRect.center;
            UIVertex vertex = default(UIVertex);
            for (int i = 0; i < vh.currentVertCount; i++)
            {
                vh.PopulateUIVertex(ref vertex, i);
                vertex.position.x = center.x + (vertex.position.x - center.x) * scale;
                vertex.position.y = center.y + (vertex.position.y - center.y) * scale;
                vertex.uv0.x = uvCenter.x + (vertex.uv0.x - uvCenter.x) * scale;
                vertex.uv0.y = uvCenter.y + (vertex.uv0.y - uvCenter.y) * scale;
                vh.SetUIVertex(vertex, i);
            }
        }
    }

    public sealed class SandPictureRuntime
    {
        public SandSimulator simulator;
        public Vector3 min;
        public Vector3 max;
        public bool[] mask;
    }

    public struct FlyingParticle
    {
        public float x, y;
        public float tx, ty;
        public float vx, vy;
        public byte colorId;
    }

    public class SandSimulator : MonoBehaviour
    {
        public const int GRID_SIZE = 35;
        public int GridSize { get; private set; } = GRID_SIZE;
        public const int SUB_STEPS = 3;

        // Color IDs
        public const byte EMPTY = 0;
        public const byte BLUE = 1;
        public const byte WHITE = 2;
        public const byte RED = 3;
        public const byte ORANGE = 4;

        // Default palette (used when no LevelData is provided)
        private static readonly Color32[] DefaultPalette = new Color32[]
        {
            new Color32(85, 85, 85, 255),       // 0: Empty/Gray
            new Color32(33, 150, 243, 255),      // 1: Blue
            new Color32(253, 251, 247, 255),     // 2: White
            new Color32(244, 67, 54, 255),       // 3: Red
            new Color32(255, 202, 40, 255)       // 4: Orange
        };

        private static readonly Color[] DefaultPaletteColors = new Color[]
        {
            new Color(85f/255f, 85f/255f, 85f/255f, 1f),
            new Color(33f/255f, 150f/255f, 243f/255f, 1f),
            new Color(253f/255f, 251f/255f, 247f/255f, 1f),
            new Color(244f/255f, 67f/255f, 54f/255f, 1f),
            new Color(255f/255f, 202f/255f, 40f/255f, 1f)
        };

        // Runtime palette (set from LevelData or defaults)
        public static Color32[] Palette;
        public static Color[] PaletteColors;

        public static readonly string[] HexColors = new string[]
        {
            "#333333", "#2196F3", "#FDFBF7", "#F44336", "#FFCA28"
        };

        static SandSimulator()
        {
            Palette = (Color32[])DefaultPalette.Clone();
            PaletteColors = (Color[])DefaultPaletteColors.Clone();
        }

        public byte[] grid;
        public int[] noiseGrid;
        public List<FlyingParticle> particles = new List<FlyingParticle>();
        public Dictionary<int, int> colorCounts = new Dictionary<int, int>();

        private Texture2D texture;
        private Texture2D shapeTexture;
        private bool[] shapeMask;
        private Color32[] pixelBuffer;
        private Color32[] flippedPixelBuffer;
        private Material circularGrainMaterial;
        private RawImage rawImage;

        public void Initialize(RawImage displayImage, int runtimeSize = GRID_SIZE)
        {
            GridSize = Mathf.Max(1, runtimeSize);
            rawImage = displayImage;

            // Reset palette to defaults
            Palette = (Color32[])DefaultPalette.Clone();
            PaletteColors = (Color[])DefaultPaletteColors.Clone();

            grid = new byte[GridSize * GridSize];
            noiseGrid = new int[GridSize * GridSize];
            pixelBuffer = new Color32[GridSize * GridSize];
            flippedPixelBuffer = new Color32[GridSize * GridSize];

            // Generate noise
            for (int i = 0; i < noiseGrid.Length; i++)
                noiseGrid[i] = Random.Range(-8, 8);

            // Generate lighthouse image
            GenerateLighthouseImage();

            CreateDisplayTexture();
        }

        /// <summary>
        /// Initialize with pre-built grid data and palette from LevelData.
        /// Falls back to GenerateLighthouseImage() if gridData is null/empty.
        /// </summary>
        public void Initialize(RawImage displayImage, List<byte> gridData, List<SerializableColor> palette, int runtimeSize = GRID_SIZE)
        {
            GridSize = Mathf.Max(1, runtimeSize);
            rawImage = displayImage;

            grid = new byte[GridSize * GridSize];
            noiseGrid = new int[GridSize * GridSize];
            pixelBuffer = new Color32[GridSize * GridSize];
            flippedPixelBuffer = new Color32[GridSize * GridSize];

            for (int i = 0; i < noiseGrid.Length; i++)
                noiseGrid[i] = Random.Range(-8, 8);

            int sourceGridSize = gridData != null
                ? Mathf.RoundToInt(Mathf.Sqrt(gridData.Count))
                : 0;
            bool hasValidGrid = sourceGridSize > 0
                && sourceGridSize * sourceGridSize == gridData.Count;

            if (hasValidGrid && palette != null && palette.Count > 0)
            {
                // Build palette arrays from LevelData
                int paletteSize = palette.Count + 1; // +1 for empty slot at index 0
                Palette = new Color32[paletteSize];
                PaletteColors = new Color[paletteSize];

                Palette[0] = new Color32(85, 85, 85, 255); // empty
                PaletteColors[0] = new Color(85f / 255f, 85f / 255f, 85f / 255f, 1f);

                for (int i = 0; i < palette.Count; i++)
                {
                    SerializableColor sc = palette[i];
                    byte r = (byte)(sc.r * 255f);
                    byte g = (byte)(sc.g * 255f);
                    byte b = (byte)(sc.b * 255f);
                    Palette[i + 1] = new Color32(r, g, b, 255);
                    PaletteColors[i + 1] = new Color(sc.r, sc.g, sc.b, 1f);
                }

                // Authored board pictures retain 11 pixels per cell; legacy callers use the default size.
                for (int y = 0; y < GridSize; y++)
                {
                    int sourceY = Mathf.Min(
                        Mathf.FloorToInt((y + 0.5f) * sourceGridSize / GridSize),
                        sourceGridSize - 1);

                    for (int x = 0; x < GridSize; x++)
                    {
                        int sourceX = Mathf.Min(
                            Mathf.FloorToInt((x + 0.5f) * sourceGridSize / GridSize),
                            sourceGridSize - 1);
                        grid[y * GridSize + x] = gridData[sourceY * sourceGridSize + sourceX];
                    }
                }

                // Count colors
                colorCounts.Clear();
                for (int c = 1; c < paletteSize; c++) colorCounts[c] = 0;
                for (int i = 0; i < grid.Length; i++)
                {
                    byte cId = grid[i];
                    if (cId > 0 && cId < paletteSize)
                        colorCounts[cId]++;
                }
            }
            else
            {
                // Fallback to default lighthouse
                Palette = (Color32[])DefaultPalette.Clone();
                PaletteColors = (Color[])DefaultPaletteColors.Clone();
                GenerateLighthouseImage();
            }

            CreateDisplayTexture();

            int particleCount = 0;
            foreach (int count in colorCounts.Values)
                particleCount += count;
            Debug.Log(
                $"[SandFlowPuzzle] Runtime sand: {GridSize}x{GridSize} = {grid.Length} cells, " +
                $"{particleCount} particles (source: {sourceGridSize}x{sourceGridSize}).");
        }

        private void CreateDisplayTexture()
        {
            texture = new Texture2D(GridSize, GridSize, TextureFormat.RGBA32, false);
            texture.filterMode = FilterMode.Point;
            texture.wrapMode = TextureWrapMode.Clamp;
            rawImage.texture = texture;

            Shader shader = Resources.Load<Shader>("SandCircularGrains");
            if (shader == null)
                shader = Shader.Find("SandFlowPuzzle/CircularGrains");

            if (shader == null)
            {
                Debug.LogError("[SandFlowPuzzle] SandCircularGrains shader could not be loaded.");
                return;
            }

            circularGrainMaterial = new Material(shader)
            {
                name = "SandCircularGrains (Runtime)"
            };
            circularGrainMaterial.SetFloat("_GridSize", GridSize);
            circularGrainMaterial.SetColor("_BackgroundColor", Palette[0]);
            rawImage.material = circularGrainMaterial;
        }

        private void GenerateLighthouseImage()
        {
            const int designSize = 80;
            colorCounts.Clear();
            for (int c = 1; c <= 4; c++) colorCounts[c] = 0;

            for (int y = 0; y < GridSize; y++)
            {
                int designY = Mathf.FloorToInt(y * designSize / (float)GridSize);
                for (int x = 0; x < GridSize; x++)
                {
                    int designX = Mathf.FloorToInt(x * designSize / (float)GridSize);
                    byte color = BLUE; // default sky

                    if (designY >= 65)
                    {
                        color = ORANGE; // beach
                    }
                    else if (designY >= 60 && designX >= 26 && designX <= 54)
                    {
                        color = WHITE; // base platform
                    }
                    else if (designX >= 32 && designX <= 48 && designY >= 25 && designY < 60)
                    {
                        // Lighthouse body stripes
                        if (designY >= 50) color = RED;
                        else if (designY >= 40) color = WHITE;
                        else if (designY >= 30) color = RED;
                        else color = WHITE;
                    }
                    else if (designX >= 30 && designX <= 50 && designY >= 18 && designY < 25)
                    {
                        color = ORANGE; // lantern room
                        if (designY >= 20 && designY <= 23 && ((designX >= 32 && designX <= 36) || (designX >= 44 && designX <= 48)))
                            color = WHITE; // windows
                    }
                    else if (designY >= 6 && designY < 18)
                    {
                        // Roof triangle
                        int w = Mathf.FloorToInt((designY - 6) * 1.5f);
                        if (Mathf.Abs(designX - 40) <= w) color = RED;
                    }
                    else
                    {
                        // Clouds
                        if (designY >= 15 && designY <= 45 && designX < 28)
                        {
                            if (Mathf.Sin(designY * 0.4f) * 6f + designX < 18) color = WHITE;
                            if (designY > 20 && designY < 35 && designX < 24) color = WHITE;
                        }
                        if (designY >= 25 && designY <= 55 && designX > 52)
                        {
                            if (Mathf.Cos(designY * 0.3f) * 6f + (designSize - designX) < 20) color = WHITE;
                            if (designY > 35 && designY < 45 && designX > 56) color = WHITE;
                        }
                    }

                    grid[y * GridSize + x] = color;
                    colorCounts[color]++;
                }
            }
        }

        public void SetShapeMask(bool[] mask)
        {
            if (mask != null && mask.Length != grid.Length)
                throw new System.ArgumentException("Shape mask must match the simulator grid.");
            shapeMask = mask;
            if (mask == null) return;
            var pixels = new Color32[grid.Length];
            colorCounts.Clear();
            for (int y = 0; y < GridSize; y++) for (int x = 0; x < GridSize; x++)
            {
                int i = y * GridSize + x;
                if (!mask[i]) grid[i] = EMPTY;
                if (grid[i] != EMPTY) colorCounts[grid[i]] = colorCounts.TryGetValue(grid[i], out int count) ? count + 1 : 1;
                pixels[(GridSize - 1 - y) * GridSize + x] = mask[i] ? new Color32(255,255,255,255) : new Color32(0,0,0,0);
            }
            shapeTexture = new Texture2D(GridSize, GridSize, TextureFormat.RGBA32, false);
            shapeTexture.filterMode = FilterMode.Point;
            shapeTexture.wrapMode = TextureWrapMode.Clamp;
            shapeTexture.SetPixels32(pixels);
            shapeTexture.Apply(false);
            if (circularGrainMaterial != null) circularGrainMaterial.SetTexture("_ShapeTex", shapeTexture);
        }

        // Start at each contacted footprint edge and scan inward by at most maxDepth pixels.
        // Empty pixels consume depth; another color or a shape boundary stops suction.
        public int ExtractFromEdge(int x, int y, int dx, int dy, byte colorId,
            int maxDepth, int budget, List<Vector2Int> extracted)
        {
            return SandFlowPuzzle.BlockAuthoring.SandBoardUtility.ExtractFromEdge(
                grid, shapeMask, x, y, dx, dy, colorId, maxDepth, budget, extracted);
        }

        public void SimulateGravity()
        {
            for (int step = 0; step < SUB_STEPS; step++)
            {
                for (int y = GridSize - 2; y >= 0; y--)
                {
                    int dir = Random.value < 0.5f ? 1 : -1;
                    int startX = dir == 1 ? 0 : GridSize - 1;
                    int endX = dir == 1 ? GridSize : -1;

                    for (int x = startX; x != endX; x += dir)
                    {
                        int idx = y * GridSize + x;
                        byte p = grid[idx];
                        if (p == 0) continue;

                        int idxDown = (y + 1) * GridSize + x;
                        if (grid[idxDown] == 0 && (shapeMask == null || shapeMask[idxDown]))
                        {
                            grid[idxDown] = p;
                            grid[idx] = 0;
                        }
                        else
                        {
                            bool fallLeft = x > 0 && grid[idxDown - 1] == 0 && (shapeMask == null || shapeMask[idxDown - 1]);
                            bool fallRight = x < GridSize - 1 && grid[idxDown + 1] == 0 && (shapeMask == null || shapeMask[idxDown + 1]);

                            if (fallLeft && fallRight)
                            {
                                if (Random.value < 0.5f) { grid[idxDown - 1] = p; grid[idx] = 0; }
                                else { grid[idxDown + 1] = p; grid[idx] = 0; }
                            }
                            else if (fallLeft) { grid[idxDown - 1] = p; grid[idx] = 0; }
                            else if (fallRight) { grid[idxDown + 1] = p; grid[idx] = 0; }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Extract exposed pixels of a given color near centerGridX.
        /// Returns the number of pixels extracted.
        /// </summary>
        /// <summary>
        /// Extract pixels and return their grid positions via the extractedPositions list.
        /// </summary>
        public int ExtractExposedPixels(int centerGridX, byte colorId, int maxCount, List<Vector2Int> extractedPositions = null)
        {
            int extracted = 0;

            int minY = Mathf.Max(0, GridSize - 4); // only extract from bottom 4 rows
            for (int y = GridSize - 1; y >= minY && extracted < maxCount; y--)
            {
                for (int dx = -8; dx <= 8 && extracted < maxCount; dx++)
                {
                    int x = centerGridX + dx;
                    if (x < 0 || x >= GridSize) continue;

                    int idx = y * GridSize + x;
                    if (grid[idx] != colorId) continue;

                    grid[idx] = 0;

                    if (extractedPositions != null)
                        extractedPositions.Add(new Vector2Int(x, y));

                    FlyingParticle fp;
                    fp.x = x;
                    fp.y = y;
                    fp.tx = centerGridX;
                    fp.ty = 85;
                    fp.vx = (centerGridX - x) * 0.05f + (Random.value - 0.5f) * 2f;
                    fp.vy = -Random.value * 2f - 1f;
                    fp.colorId = colorId;
                    particles.Add(fp);

                    extracted++;
                }
            }

            return extracted;
        }

        /// <summary>
        /// Extracts exposed pixels only from columns covered by the part of a
        /// collector block that is physically touching the bottom of the picture.
        /// Columns are visited from the collector center outward for a focused
        /// suction effect while preserving the original sand simulation grid.
        /// </summary>
        public int ExtractExposedPixelsInColumns(
            int centerGridX,
            byte colorId,
            int maxCount,
            List<Vector2Int> extractedPositions,
            bool[] allowedColumns)
        {
            if (maxCount <= 0 || allowedColumns == null || allowedColumns.Length < GridSize)
                return 0;

            int extracted = 0;
            int minY = Mathf.Max(0, GridSize - 4);

            for (int y = GridSize - 1; y >= minY && extracted < maxCount; y--)
            {
                for (int distance = 0; distance < GridSize && extracted < maxCount; distance++)
                {
                    int leftX = centerGridX - distance;
                    if (TryExtractPixel(leftX, y, colorId, centerGridX, extractedPositions, allowedColumns))
                        extracted++;

                    if (distance == 0 || extracted >= maxCount) continue;

                    int rightX = centerGridX + distance;
                    if (TryExtractPixel(rightX, y, colorId, centerGridX, extractedPositions, allowedColumns))
                        extracted++;
                }
            }

            return extracted;
        }

        private bool TryExtractPixel(
            int x,
            int y,
            byte colorId,
            int targetGridX,
            List<Vector2Int> extractedPositions,
            bool[] allowedColumns)
        {
            if (x < 0 || x >= GridSize || !allowedColumns[x]) return false;

            int index = y * GridSize + x;
            if (grid[index] != colorId) return false;

            grid[index] = EMPTY;
            extractedPositions?.Add(new Vector2Int(x, y));

            particles.Add(new FlyingParticle
            {
                x = x,
                y = y,
                tx = targetGridX,
                ty = GridSize + 5,
                vx = (targetGridX - x) * 0.05f + (Random.value - 0.5f) * 2f,
                vy = -Random.value * 2f - 1f,
                colorId = colorId
            });

            return true;
        }

        public void UpdateParticles()
        {
            for (int i = particles.Count - 1; i >= 0; i--)
            {
                FlyingParticle p = particles[i];
                p.x += p.vx;
                p.y += p.vy;

                float dx = p.tx - p.x;
                float dy = p.ty - p.y;

                p.vx += dx * 0.1f;
                p.vy += dy * 0.1f;
                p.vx *= 0.75f;
                p.vy *= 0.75f;

                if (Mathf.Abs(dx) < 3f && Mathf.Abs(dy) < 3f)
                {
                    particles.RemoveAt(i);
                    continue;
                }

                particles[i] = p;
            }
        }

        public void RenderToTexture()
        {
            int paletteLen = Palette.Length;
            Color32 backgroundColor = Palette[0];

            // Alpha stores occupancy for the circular-grain shader:
            // 0 = empty cell, 255 = visible grain.
            for (int i = 0; i < GridSize * GridSize; i++)
            {
                byte colorId = grid[i];
                bool occupied = colorId != EMPTY && colorId < paletteLen;
                Color32 rgb = occupied ? Palette[colorId] : backgroundColor;
                int noise = occupied ? noiseGrid[i] : 0;
                pixelBuffer[i] = ApplyNoise(rgb, noise, occupied ? (byte)255 : (byte)0);
            }

            // Render particles on top
            for (int i = 0; i < particles.Count; i++)
            {
                FlyingParticle p = particles[i];
                int px = Mathf.FloorToInt(p.x);
                int py = Mathf.FloorToInt(p.y);
                if (px >= 0 && px < GridSize && py >= 0 && py < GridSize)
                {
                    int gridIndex = py * GridSize + px;
                    Color32 rgb = p.colorId < paletteLen ? Palette[p.colorId] : Palette[0];
                    pixelBuffer[gridIndex] = ApplyNoise(rgb, noiseGrid[gridIndex], 255);
                }
            }

            // Unity texture Y is flipped (0 = bottom), but our grid Y=0 = top
            // We need to flip vertically when writing to texture
            for (int y = 0; y < GridSize; y++)
            {
                System.Array.Copy(
                    pixelBuffer,
                    y * GridSize,
                    flippedPixelBuffer,
                    (GridSize - 1 - y) * GridSize,
                    GridSize);
            }

            texture.SetPixels32(flippedPixelBuffer);
            texture.Apply();
        }

        private static Color32 ApplyNoise(Color32 color, int noise, byte alpha)
        {
            return new Color32(
                (byte)Mathf.Clamp(color.r + noise, 0, 255),
                (byte)Mathf.Clamp(color.g + noise, 0, 255),
                (byte)Mathf.Clamp(color.b + noise, 0, 255),
                alpha);
        }

        public bool IsAllEmpty()
        {
            for (int i = 0; i < grid.Length; i++)
            {
                if (grid[i] != 0) return false;
            }
            return true;
        }

        private void OnDestroy()
        {
            if (texture != null) Destroy(texture);
            if (shapeTexture != null) Destroy(shapeTexture);
            if (circularGrainMaterial != null) Destroy(circularGrainMaterial);
        }
    }
}
