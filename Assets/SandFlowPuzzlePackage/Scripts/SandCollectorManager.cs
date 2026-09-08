using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using SandFlowPuzzle.BlockAuthoring;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using HypercasualGameEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace SandFlowPuzzle
{
    /// <summary>
    /// Builds and controls DogJam-style draggable sand collectors.
    /// Only the leading row of per-cell colliders can collect, and its world-X
    /// footprint is converted into an exact mask of sand-grid columns.
    /// </summary>
    public sealed class SandCollectorManager : MonoBehaviour
    {
        // Preserve the original grain size at grid 70; lower resolutions use larger grains.
        private const float FlyingGrainScaleMultiplier = 70f / SandSimulator.GRID_SIZE;
        private const float FlyingGrainStartScale = 0.055f * FlyingGrainScaleMultiplier;
        private const float FlyingGrainEndScale = 0.015f * FlyingGrainScaleMultiplier;

        private struct FlyingGrain
        {
            public Transform transform;
            public SandCollectorBlock targetBlock;
            public Vector3 startPosition;
            public float elapsed;
            public float duration;
            public float spreadX;
            public float spreadZ;
        }

        private static readonly Vector2Int[][] DogJamShapes =
        {
            new[] { new Vector2Int(0, 0), new Vector2Int(0, 1), new Vector2Int(1, 0) },
            new[] { new Vector2Int(0, 0), new Vector2Int(0, 1), new Vector2Int(0, 2) },
            new[] { new Vector2Int(0, 1), new Vector2Int(1, 1), new Vector2Int(2, 1), new Vector2Int(2, 0) },
            new[] { new Vector2Int(0, 0), new Vector2Int(1, 0) },
            new[] { new Vector2Int(0, 0) },
            new[] { new Vector2Int(0, 0), new Vector2Int(1, 0), new Vector2Int(0, 1), new Vector2Int(1, 1) }
        };

        [Header("Drag")]
        [SerializeField] private float blockCellSize = 0.26f;
        [SerializeField] private float dragHeight = 0f;
        [SerializeField] private float dragForce = 150f;
        [SerializeField] private float maxDragSpeed = 10f;

        [Header("Suction")]
        [SerializeField] private int grainsPerSuction = 4;
        [SerializeField] private float suctionInterval = 0.04f;
        [SerializeField] private float suctionGap = 0.16f;
        [SerializeField] private float suctionReach = 0.48f;
        [SerializeField] private float contactTolerance = 0.03f;

        [Header("Placement Grid")]
        [SerializeField, Range(6, 10)] private int placementGridColumnCount = 6;
        [SerializeField, Range(0.02f, 0.2f)] private float gridCellGap = 0.07f;
        [SerializeField] private float gridVerticalOffset = 0.018f;
        [SerializeField] private Color gridColorA = new Color(0.25f, 0.29f, 0.36f, 0.34f);
        [SerializeField] private Color gridColorB = new Color(0.32f, 0.36f, 0.43f, 0.34f);
        [SerializeField] private Color suctionGridColor = new Color(0.34f, 0.58f, 0.75f, 0.42f);

        private readonly List<SandCollectorBlock> blocks = new List<SandCollectorBlock>();
        private readonly List<Vector2Int> extractedPositions = new List<Vector2Int>(8);
        private readonly bool[] contactGridColumns = new bool[SandSimulator.GRID_SIZE];
        private readonly Dictionary<int, Material> colorMaterials = new Dictionary<int, Material>();
        private readonly Dictionary<SandCollectorBlock, float> suctionCooldowns = new Dictionary<SandCollectorBlock, float>();
        private readonly List<FlyingGrain> flyingGrains = new List<FlyingGrain>(64);
        private readonly Stack<GameObject> grainPool = new Stack<GameObject>(64);
        private readonly List<Material> dogJamVisualMaterials = new List<Material>(8);
        private readonly List<Texture2D> dogJamSdfTextures = new List<Texture2D>(8);
        private readonly List<Mesh> dogJamMeshes = new List<Mesh>(8);

        private IReadOnlyList<SandPictureRuntime> pictures;
        private readonly Dictionary<int, int> totalColorCounts = new Dictionary<int, int>();
        private readonly List<int> blockColorList = new List<int>();
        public bool HasFlyingGrains => flyingGrains.Count > 0;
        private BlockXLevelFile authoredBoard;
        private readonly HashSet<Collider> boardObstacles = new HashSet<Collider>();
        private Camera mainCamera;
        private SandCollectorBlock draggedBlock;
        private Plane dragPlane;
        private Vector3 sandWorldMin;
        private Vector3 sandWorldMax;
        private float playAreaMinZ;
        private bool gameplayEnabled;
        private Material dogJamMaterialTemplate;
        private Sprite dogJamCounterSprite;
        private Mesh placementGridMesh;
        private Material placementGridMaterial;
        private Color[] placementGridColors;
        private int placementGridColumns;
        private int placementGridRows;
        private float placementGridMinX;
        private float placementGridMinZ;
        private float placementGridTopZ;

        public float DragHeight => dragHeight;
        public float DragForce => dragForce;
        public float MaxDragSpeed => maxDragSpeed;
        public bool IsDragging => draggedBlock != null;

        public void Initialize(IReadOnlyList<SandPictureRuntime> sandPictures, LevelData levelData, Vector3 sandMin, Vector3 sandMax)
        {
            pictures = sandPictures;
            blockColorList.Clear();
            totalColorCounts.Clear();
            foreach (SandPictureRuntime picture in pictures)
                foreach (var entry in picture.simulator.colorCounts)
                {
                    if (!blockColorList.Contains(entry.Key)) blockColorList.Add(entry.Key);
                    totalColorCounts[entry.Key] = totalColorCounts.TryGetValue(entry.Key, out int count)
                        ? count + entry.Value : entry.Value;
                }
            sandWorldMin = Vector3.Min(sandMin, sandMax);
            sandWorldMax = Vector3.Max(sandMin, sandMax);
            mainCamera = Camera.main;
            if (levelData != null && !CollectorBoardUtility.TryResolve(levelData, out authoredBoard, out string layoutError))
            {
                Debug.LogError($"[SandFlowPuzzle] Invalid collector layout: {layoutError}");
                gameplayEnabled = false;
                return;
            }
            if (authoredBoard != null) placementGridColumnCount = authoredBoard.grid.columns;
            gameplayEnabled = true;

            // Scale the complete DogJam board from the picture width. Grid,
            // visual meshes, colliders and snapping all share this cell size.
            float pictureWidth = sandWorldMax.x - sandWorldMin.x;
            blockCellSize = pictureWidth / placementGridColumnCount;

            PrepareDogJamVisualAssets();
            if (authoredBoard != null) BuildAuthoredCollectorBlocks();
            else BuildCollectorBlocks();
        }

        public void SetGameplayEnabled(bool enabled)
        {
            gameplayEnabled = enabled;
            if (!enabled && draggedBlock != null)
            {
                draggedBlock.EndDrag();
                draggedBlock = null;
            }
        }

        private void BuildAuthoredCollectorBlocks()
        {
            playAreaMinZ = authoredBoard.sandInBoard ? sandWorldMin.z
                : sandWorldMin.z - suctionGap - authoredBoard.grid.rows * blockCellSize;
            BuildPlacementGrid();
            var perColor = new Dictionary<int, int>();
            foreach (var item in authoredBoard.blocks)
                perColor[item.colorId] = perColor.TryGetValue(item.colorId, out int n) ? n + 1 : 1;
            for (int i = 0; i < authoredBoard.blocks.Count; i++)
            {
                var item = authoredBoard.blocks[i];
                int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
                var shape = new Vector2Int[item.occupiedCells.Count];
                for (int j = 0; j < shape.Length; j++)
                {
                    var cell = item.occupiedCells[j];
                    shape[j] = new Vector2Int(cell.x, cell.y);
                    minX = Mathf.Min(minX, cell.x); maxX = Mathf.Max(maxX, cell.x);
                    minY = Mathf.Min(minY, cell.y); maxY = Mathf.Max(maxY, cell.y);
                }

                // Determine quotas for this block (manual per-color or legacy auto-split).
                Dictionary<int, int> quotas = CollectorBoardUtility.ResolveBlockQuotas(authoredBoard, i, totalColorCounts);
                if (quotas.Count == 0)
                {
                    int count = perColor[item.colorId];
                    int total = totalColorCounts[item.colorId];
                    int ordinal = 0;
                    for (int k = 0; k < i; k++)
                        if (authoredBoard.blocks[k] != null && authoredBoard.blocks[k].colorId == item.colorId) ordinal++;
                    int quota = total / count + (ordinal < total % count ? 1 : 0);
                    quotas[item.colorId] = quota;
                }

                // HoleGenerator centers its mesh on the shape bounds; translate that center
                // to the authored cells, retaining BlockX's bottom-left coordinate convention.
                Vector3 center = new Vector3(
                    placementGridMinX + (minX + maxX + 1) * 0.5f * blockCellSize,
                    transform.position.y,
                    placementGridMinZ + (minY + maxY + 1) * 0.5f * blockCellSize);
                CreateCollectorBlock(i, item.colorId, quotas, shape, center);
            }
        }

        private bool IsPlayableCell(int x, int y)
        {
            if (x < 0 || y < 0 || x >= placementGridColumns || y >= placementGridRows) return false;
            return authoredBoard == null || (!SandBoardUtility.IsSandCell(authoredBoard, x, y) && authoredBoard.grid.cells[
                LevelGridCoordinateUtility.ToIndex(new Vector2Int(x, y), placementGridRows, placementGridColumns)] == 1);
        }

        private void BuildCollectorBlocks()
        {
            blocks.Clear();
            suctionCooldowns.Clear();

            List<int> activeColors = new List<int>();
            for (int colorId = 1; colorId < SandSimulator.PaletteColors.Length; colorId++)
            {
                if (totalColorCounts.TryGetValue(colorId, out int count) && count > 0)
                    activeColors.Add(colorId);
            }

            if (activeColors.Count == 0) return;

            int blockCount = Mathf.Max(DogJamShapes.Length, activeColors.Count);
            List<int> blockColors = DistributeBlockColors(activeColors, blockCount);
            Dictionary<int, int> blocksPerColor = new Dictionary<int, int>();
            for (int i = 0; i < blockColors.Count; i++)
            {
                int colorId = blockColors[i];
                blocksPerColor[colorId] = blocksPerColor.TryGetValue(colorId, out int count) ? count + 1 : 1;
            }

            Dictionary<int, int> createdPerColor = new Dictionary<int, int>();
            const int columns = 3;
            float sandWidth = sandWorldMax.x - sandWorldMin.x;
            float columnSpacing = sandWidth / columns;
            float rowSpacing = blockCellSize * 3.2f;
            int rowCount = Mathf.CeilToInt((float)blockCount / columns);
            playAreaMinZ = sandWorldMin.z - Mathf.Max(2.4f, rowCount * rowSpacing + 0.45f);
            BuildPlacementGrid();

            for (int i = 0; i < blockCount; i++)
            {
                int colorId = blockColors[i];
                int colorTotal = totalColorCounts[colorId];
                int sameColorCount = blocksPerColor[colorId];
                int colorIndex = createdPerColor.TryGetValue(colorId, out int created) ? created : 0;
                createdPerColor[colorId] = colorIndex + 1;

                int quota = colorTotal / sameColorCount;
                if (colorIndex < colorTotal % sameColorCount) quota++;
                if (quota <= 0) continue;

                int column = i % columns;
                int row = i / columns;
                float worldX = (sandWorldMin.x + sandWorldMax.x) * 0.5f
                    + (column - (columns - 1) * 0.5f) * columnSpacing;
                // Start clearly below the picture. Even the deepest shape stays
                // outside the suction lane until the player drags it upward.
                float worldZ = sandWorldMin.z - 1.05f - row * rowSpacing;
                float worldY = transform.position.y;

                CreateCollectorBlock(
                    i,
                    colorId,
                    new Dictionary<int, int> { { colorId, quota } },
                    DogJamShapes[i % DogJamShapes.Length],
                    new Vector3(worldX, worldY, worldZ));
            }
        }

        private List<int> DistributeBlockColors(List<int> activeColors, int blockCount)
        {
            List<int> result = new List<int>(blockCount);
            Dictionary<int, int> assigned = new Dictionary<int, int>();

            for (int i = 0; i < activeColors.Count; i++)
            {
                result.Add(activeColors[i]);
                assigned[activeColors[i]] = 1;
            }

            while (result.Count < blockCount)
            {
                int bestColor = activeColors[0];
                float bestLoad = float.NegativeInfinity;
                for (int i = 0; i < activeColors.Count; i++)
                {
                    int colorId = activeColors[i];
                    float load = (float)totalColorCounts[colorId] / (assigned[colorId] + 1);
                    if (load > bestLoad)
                    {
                        bestLoad = load;
                        bestColor = colorId;
                    }
                }

                result.Add(bestColor);
                assigned[bestColor]++;
            }

            return result;
        }

        private void CreateCollectorBlock(int index, int colorId, Dictionary<int, int> colorQuotas, Vector2Int[] shape, Vector3 position)
        {
            GameObject root = new GameObject($"SandCollectorBlock_{index + 1}_Color_{colorId}");
            root.transform.SetParent(transform, true);
            root.transform.position = position;

            int minX = shape[0].x;
            int maxX = shape[0].x;
            int minY = shape[0].y;
            int maxY = shape[0].y;
            for (int i = 1; i < shape.Length; i++)
            {
                minX = Mathf.Min(minX, shape[i].x);
                maxX = Mathf.Max(maxX, shape[i].x);
                minY = Mathf.Min(minY, shape[i].y);
                maxY = Mathf.Max(maxY, shape[i].y);
            }

            int quota = 0;
            foreach (var kv in colorQuotas) quota += kv.Value;

            HoleDefinition hole = root.AddComponent<HoleDefinition>();
            hole.InitializeGrid(maxX - minX + 1, maxY - minY + 1);
            for (int i = 0; i < shape.Length; i++)
                hole.SetCell(shape[i].x - minX, shape[i].y - minY, true);

            hole.cellSize = blockCellSize;
            hole.blockHeight = 0.16f;
            hole.visualScale = 0.6f;
            hole.colliderScale = new Vector3(blockCellSize * 0.94f, 0.16f, blockCellSize * 0.94f);
            hole.bottomDarkness = 0f;
            hole.outlineWidth = 0.222f;
            hole.outlineBrightness = 1.3f;
            hole.materialTemplate = dogJamMaterialTemplate;
            hole.uiPosition = HoleDefinition.UIPosition.BottomRight;
            hole.targetCollectCount = quota;

            Color color = SandSimulator.PaletteColors[colorId];
            Color outlineColor = Color.Lerp(color, Color.white, 0.3f);
            CreateDogJamCounter(root.transform, outlineColor, out Transform counterBackground, out TextMeshPro counter);
            hole.collectCounterBg = counterBackground;
            hole.collectCounterText = counter;

            HoleGenerator.Generate(hole);
            ConfigureDogJamVisual(hole, color);

            List<Collider> colliders = new List<Collider>(hole.colliderObjects.Count);
            for (int i = 0; i < hole.colliderObjects.Count; i++)
            {
                GameObject colliderObject = hole.colliderObjects[i];
                if (colliderObject == null) continue;
                Collider cellCollider = colliderObject.GetComponent<Collider>();
                if (cellCollider != null) colliders.Add(cellCollider);
            }

            SandCollectorBlock block = root.AddComponent<SandCollectorBlock>();
            block.Initialize(this, colorQuotas, colliders, counter);
            root.transform.position = ClampToPlayArea(
                block,
                SnapToPlacementGrid(block, root.transform.position));

            blocks.Add(block);
            suctionCooldowns[block] = 0f;
        }

        private void BuildPlacementGrid()
        {
            float availableWidth = sandWorldMax.x - sandWorldMin.x;
            placementGridColumns = Mathf.Max(1, placementGridColumnCount);
            float gridWidth = placementGridColumns * blockCellSize;
            placementGridMinX = (sandWorldMin.x + sandWorldMax.x - gridWidth) * 0.5f;

            placementGridTopZ = authoredBoard != null && authoredBoard.sandInBoard ? sandWorldMax.z : sandWorldMin.z - suctionGap;
            placementGridRows = authoredBoard != null ? authoredBoard.grid.rows : Mathf.Max(
                1,
                Mathf.CeilToInt((placementGridTopZ - playAreaMinZ) / blockCellSize));
            placementGridMinZ = placementGridTopZ - placementGridRows * blockCellSize;
            playAreaMinZ = placementGridMinZ;

            GameObject gridObject = new GameObject("BlockPlacementGrid");
            gridObject.transform.SetParent(transform, false);
            gridObject.transform.position = new Vector3(0f, transform.position.y - gridVerticalOffset, 0f);

            MeshFilter filter = gridObject.AddComponent<MeshFilter>();
            MeshRenderer renderer = gridObject.AddComponent<MeshRenderer>();
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            int cellCount = placementGridColumns * placementGridRows;
            Vector3[] vertices = new Vector3[cellCount * 4];
            Vector2[] uvs = new Vector2[cellCount * 4];
            int[] triangles = new int[cellCount * 6];
            placementGridColors = new Color[cellCount * 4];

            float inset = blockCellSize * gridCellGap * 0.5f;
            for (int row = 0; row < placementGridRows; row++)
            {
                for (int column = 0; column < placementGridColumns; column++)
                {
                    int cellIndex = row * placementGridColumns + column;
                    int vertexIndex = cellIndex * 4;
                    int triangleIndex = cellIndex * 6;
                    if (!IsPlayableCell(column, row))
                    {
                        var obstacle = new GameObject($"DisabledBoardCell_{column}_{row}");
                        obstacle.transform.SetParent(transform, false);
                        obstacle.transform.position = new Vector3(
                            placementGridMinX + (column + 0.5f) * blockCellSize,
                            transform.position.y + 0.3f,
                            placementGridMinZ + (row + 0.5f) * blockCellSize);
                        var collider = obstacle.AddComponent<BoxCollider>();
                        collider.size = new Vector3(blockCellSize, 0.8f, blockCellSize);
                        boardObstacles.Add(collider);
                    }
                    float minX = placementGridMinX + column * blockCellSize + inset;
                    float maxX = placementGridMinX + (column + 1) * blockCellSize - inset;
                    float minZ = placementGridMinZ + row * blockCellSize + inset;
                    float maxZ = placementGridMinZ + (row + 1) * blockCellSize - inset;

                    vertices[vertexIndex] = new Vector3(minX, 0f, minZ);
                    vertices[vertexIndex + 1] = new Vector3(minX, 0f, maxZ);
                    vertices[vertexIndex + 2] = new Vector3(maxX, 0f, maxZ);
                    vertices[vertexIndex + 3] = new Vector3(maxX, 0f, minZ);

                    uvs[vertexIndex] = Vector2.zero;
                    uvs[vertexIndex + 1] = Vector2.up;
                    uvs[vertexIndex + 2] = Vector2.one;
                    uvs[vertexIndex + 3] = Vector2.right;

                    triangles[triangleIndex] = vertexIndex;
                    triangles[triangleIndex + 1] = vertexIndex + 1;
                    triangles[triangleIndex + 2] = vertexIndex + 2;
                    triangles[triangleIndex + 3] = vertexIndex;
                    triangles[triangleIndex + 4] = vertexIndex + 2;
                    triangles[triangleIndex + 5] = vertexIndex + 3;
                }
            }

            placementGridMesh = new Mesh { name = "Sand Collector Placement Grid" };
            placementGridMesh.vertices = vertices;
            placementGridMesh.uv = uvs;
            placementGridMesh.triangles = triangles;
            ResetPlacementGridColors();
            placementGridMesh.colors = placementGridColors;
            placementGridMesh.RecalculateBounds();
            filter.sharedMesh = placementGridMesh;

            Shader gridShader = Shader.Find("Sprites/Default");
            if (gridShader == null) gridShader = Shader.Find("Universal Render Pipeline/Unlit");
            if (gridShader != null)
            {
                placementGridMaterial = new Material(gridShader)
                {
                    name = "Sand Collector Placement Grid Material",
                    renderQueue = (int)RenderQueue.Transparent
                };
                if (placementGridMaterial.HasProperty("_Color"))
                    placementGridMaterial.SetColor("_Color", Color.white);
                renderer.sharedMaterial = placementGridMaterial;
            }
        }

        private void ResetPlacementGridColors()
        {
            if (placementGridColors == null) return;

            for (int row = 0; row < placementGridRows; row++)
            {
                bool isSuctionRow = (authoredBoard == null || !authoredBoard.sandInBoard) && row == placementGridRows - 1;
                for (int column = 0; column < placementGridColumns; column++)
                {
                    Color color = isSuctionRow
                        ? suctionGridColor
                        : ((row + column) & 1) == 0 ? gridColorA : gridColorB;
                    if (!IsPlayableCell(column, row)) color = new Color(0.08f, 0.09f, 0.11f, 0.85f);
                    SetPlacementGridCellColor(row * placementGridColumns + column, color);
                }
            }
        }

        private void UpdatePlacementGridHighlights()
        {
            if (placementGridMesh == null || placementGridColors == null) return;

            ResetPlacementGridColors();
            for (int blockIndex = 0; blockIndex < blocks.Count; blockIndex++)
            {
                SandCollectorBlock block = blocks[blockIndex];
                if (block == null || block.IsCompleting) continue;

                Color highlight = Color.Lerp(
                    SandSimulator.PaletteColors[block.ColorId],
                    Color.white,
                    block.IsDragging ? 0.45f : 0.25f);
                highlight.a = block.IsDragging ? 0.8f : 0.52f;

                IReadOnlyList<Collider> colliders = block.CellColliders;
                for (int i = 0; i < colliders.Count; i++)
                {
                    Collider cell = colliders[i];
                    if (cell == null || !cell.enabled || !cell.gameObject.activeInHierarchy) continue;

                    int column = Mathf.FloorToInt((cell.bounds.center.x - placementGridMinX) / blockCellSize);
                    int row = Mathf.FloorToInt((cell.bounds.center.z - placementGridMinZ) / blockCellSize);
                    if (column < 0 || column >= placementGridColumns || row < 0 || row >= placementGridRows)
                        continue;

                    SetPlacementGridCellColor(row * placementGridColumns + column, highlight);
                }
            }

            placementGridMesh.colors = placementGridColors;
        }

        private void SetPlacementGridCellColor(int cellIndex, Color color)
        {
            int vertexIndex = cellIndex * 4;
            placementGridColors[vertexIndex] = color;
            placementGridColors[vertexIndex + 1] = color;
            placementGridColors[vertexIndex + 2] = color;
            placementGridColors[vertexIndex + 3] = color;
        }

        private void PrepareDogJamVisualAssets()
        {
            Material sourceMaterial = Resources.Load<Material>("Collector/HoleMaterial");
            if (sourceMaterial == null)
            {
                Material[] loadedMaterials = Resources.FindObjectsOfTypeAll<Material>();
                for (int i = 0; i < loadedMaterials.Length; i++)
                {
                    Material material = loadedMaterials[i];
                    if (material == null || material.shader == null) continue;
                    if (material.name != "HoleMaterial" || material.shader.name != "Custom/HoleShader")
                        continue;

                    sourceMaterial = material;
                    break;
                }
            }

            if (sourceMaterial != null)
                dogJamMaterialTemplate = new Material(sourceMaterial);
            else
            {
                Shader holeShader = Shader.Find("Custom/HoleShader");
                if (holeShader != null)
                    dogJamMaterialTemplate = new Material(holeShader);
            }

            if (dogJamMaterialTemplate != null)
            {
                dogJamMaterialTemplate.name = "SandCollector DogJam Material Template";
                dogJamMaterialTemplate.SetFloat("_Height", 0.16f);
                dogJamMaterialTemplate.SetFloat("_BottomDarkness", 0f);
                dogJamMaterialTemplate.SetFloat("_OutlineWidth", 0.222f);
                dogJamMaterialTemplate.SetFloat("_OutlineBrightness", 1.3f);
                dogJamMaterialTemplate.SetFloat("_EdgeSmoothness", 0.01f);
                dogJamMaterialTemplate.SetFloat("_DarkGradientPower", 2.5f);
            }
            else
            {
                Debug.LogError("[SandFlowPuzzle] Game #5 HoleShader was not found.");
            }

            dogJamCounterSprite = Resources.Load<Sprite>("Collector/SandCollectorCounter");
            if (dogJamCounterSprite == null)
            {
                SpriteRenderer[] spriteRenderers = Resources.FindObjectsOfTypeAll<SpriteRenderer>();
                for (int i = 0; i < spriteRenderers.Length; i++)
                {
                    SpriteRenderer spriteRenderer = spriteRenderers[i];
                    if (spriteRenderer != null
                        && spriteRenderer.gameObject.name == "CollectCounterBg"
                        && spriteRenderer.sprite != null)
                    {
                        dogJamCounterSprite = spriteRenderer.sprite;
                        break;
                    }
                }
            }

            if (dogJamCounterSprite == null)
            {
                Sprite[] loadedSprites = Resources.FindObjectsOfTypeAll<Sprite>();
                for (int i = 0; i < loadedSprites.Length; i++)
                {
                    Sprite sprite = loadedSprites[i];
                    if (sprite == null || sprite.name != "Square") continue;
                    dogJamCounterSprite = sprite;
                    break;
                }
            }
        }

        private void CreateDogJamCounter(
            Transform blockRoot,
            Color backgroundColor,
            out Transform backgroundTransform,
            out TextMeshPro counter)
        {
            GameObject backgroundObject = new GameObject("CollectCounterBg");
            backgroundObject.transform.SetParent(blockRoot, false);
            backgroundObject.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            backgroundObject.transform.localScale = Vector3.one * 0.17333f;
            SpriteRenderer background = backgroundObject.AddComponent<SpriteRenderer>();
            background.sprite = dogJamCounterSprite;
            background.color = backgroundColor;
            background.sortingOrder = 1;
            backgroundTransform = backgroundObject.transform;

            GameObject textObject = new GameObject("CollectCounterText");
            textObject.transform.SetParent(backgroundObject.transform, false);
            counter = textObject.AddComponent<TextMeshPro>();
            // The background is sorting order 1. Keep the world-space TMP above
            // it and separate the planes slightly to avoid depth fighting.
            counter.sortingLayerID = background.sortingLayerID;
            counter.sortingOrder = background.sortingOrder + 1;
            counter.rectTransform.localPosition = new Vector3(0f, 0f, -0.02f);
            counter.rectTransform.sizeDelta = new Vector2(3.8f, 1.4f);
            counter.fontSize = 7.9f;
            counter.enableAutoSizing = true;
            counter.fontSizeMin = 1f;
            counter.fontSizeMax = 10f;
            counter.fontStyle = FontStyles.Bold;
            counter.alignment = TextAlignmentOptions.Center;
            counter.color = Color.white;
            counter.textWrappingMode = TextWrappingModes.NoWrap;
        }

        private void ConfigureDogJamVisual(HoleDefinition hole, Color color)
        {
            if (hole == null || hole.visualObject == null) return;

            MeshRenderer renderer = hole.visualObject.GetComponent<MeshRenderer>();
            MeshFilter filter = hole.visualObject.GetComponent<MeshFilter>();
            Material material = renderer != null ? renderer.sharedMaterial : null;
            if (material != null)
            {
                Color bottomColor = color * 0.5f;
                bottomColor.a = 1f;
                material.SetColor("_Color", color);
                material.SetColor("_BottomColor", bottomColor);
                material.SetColor("_OutlineColor", Color.Lerp(color, Color.white, 0.4f));
                material.SetFloat("_Height", hole.blockHeight);
                dogJamVisualMaterials.Add(material);
            }

            if (filter != null && filter.sharedMesh != null)
                dogJamMeshes.Add(filter.sharedMesh);
            if (hole.generatedSDF != null)
                dogJamSdfTextures.Add(hole.generatedSDF);
        }

        private Material GetColorMaterial(int colorId)
        {
            if (colorMaterials.TryGetValue(colorId, out Material material))
                return material;

            Shader shader = Resources.Load<Shader>("SandFlyingGrains");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            material = new Material(shader) { enableInstancing = true };
            Color color = SandSimulator.PaletteColors[colorId];
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            else material.color = color;
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0.45f);
            colorMaterials[colorId] = material;
            return material;
        }

        private void Update()
        {
            if (pictures == null) return;

            if (gameplayEnabled)
            {
                HandleDragInput();
                UpdateSuction();
            }
            UpdatePlacementGridHighlights();
            UpdateFlyingGrains();
        }

        private void HandleDragInput()
        {
            if (mainCamera == null) mainCamera = Camera.main;
            if (mainCamera == null || !TryGetPointerPosition(out Vector2 pointerPosition)) return;

            if (PointerPressedThisFrame() && draggedBlock == null)
            {
                if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
                    return;

                Ray ray = mainCamera.ScreenPointToRay(pointerPosition);
                if (Physics.Raycast(ray, out RaycastHit hit, 1000f))
                {
                    SandCollectorBlock block = hit.collider.GetComponentInParent<SandCollectorBlock>();
                    if (block != null && !block.IsCompleting)
                    {
                        dragPlane = new Plane(Vector3.up, block.transform.position);
                        if (dragPlane.Raycast(ray, out float enter))
                        {
                            draggedBlock = block;
                            draggedBlock.BeginDrag(ray.GetPoint(enter));
                        }
                    }
                }
            }

            if (draggedBlock != null && PointerIsPressed())
            {
                Ray ray = mainCamera.ScreenPointToRay(pointerPosition);
                if (dragPlane.Raycast(ray, out float enter))
                    draggedBlock.SetDragTarget(ray.GetPoint(enter));
            }

            if (draggedBlock != null && PointerReleasedThisFrame())
            {
                draggedBlock.EndDrag();
                draggedBlock = null;
            }
        }

        private void UpdateSuction()
        {
            for (int i = blocks.Count - 1; i >= 0; i--)
            {
                SandCollectorBlock block = blocks[i];
                if (block == null)
                {
                    blocks.RemoveAt(i);
                    continue;
                }
                if (block.IsCompleting || block.Remaining <= 0) continue;

                float cooldown = suctionCooldowns.TryGetValue(block, out float value) ? value : 0f;
                cooldown -= Time.deltaTime;
                suctionCooldowns[block] = cooldown;
                if (cooldown > 0f) continue;

                if (!block.TryGetBounds(out Bounds blockBounds)) continue;
                int budget = Mathf.Min(grainsPerSuction, block.Remaining);
                int totalExtracted = 0;

                // Determine which colors this block still needs and can collect.
                var pendingColors = new List<int>();
                if (block.IsMultiColor)
                {
                    foreach (int c in blockColorList)
                        if (block.RemainingForColor(c) > 0) pendingColors.Add(c);
                }
                else
                {
                    if (block.Remaining > 0) pendingColors.Add(block.ColorId);
                }

                foreach (int targetColor in pendingColors)
                {
                    if (budget <= 0 || block.IsCompleting) break;
                    foreach (SandPictureRuntime picture in pictures)
                    {
                        if (budget <= 0 || block.IsCompleting) break;
                        extractedPositions.Clear();
                        int extracted;
                        if (authoredBoard != null && authoredBoard.sandInBoard)
                            extracted = ExtractAtRegionContacts(block, picture, budget, (byte)targetColor);
                        else
                        {
                            if (!BuildContactColumnMask(block, contactGridColumns, picture)) continue;
                            int centerGridX = WorldXToGridX(blockBounds.center.x, picture);
                            extracted = picture.simulator.ExtractExposedPixelsInColumns(
                                centerGridX, (byte)targetColor, budget, extractedPositions, contactGridColumns);
                        }
                        if (extracted == 0) continue;
                        SpawnFlyingGrains(block, extractedPositions, picture);
                        block.Absorb(extracted, targetColor);
                        budget -= extracted;
                        totalExtracted += extracted;
                    }
                }
                suctionCooldowns[block] = suctionInterval;
                if (totalExtracted > 0 && HypercasualGameEngine.SoundManager.Instance != null)
                    HypercasualGameEngine.SoundManager.Instance.PlaySandFlowSandPour();
            }
        }

        private int ExtractAtRegionContacts(SandCollectorBlock block, SandPictureRuntime picture, int budget, byte targetColor)
        {
            if (picture.mask == null) return 0;
            int n = SandSimulator.GRID_SIZE;
            float pixel = (picture.max.x - picture.min.x) / n;
            int count = 0;
            for (int y = 0; y < n && count < budget; y++)
            for (int x = 0; x < n && count < budget; x++)
            {
                if (!picture.mask[y * n + x]) continue;
                float cx = picture.min.x + (x + 0.5f) * pixel;
                float cz = picture.max.z - (y + 0.5f) * pixel;
                for (int direction = 0; direction < 4 && count < budget; direction++)
                {
                    int dx = direction == 0 ? -1 : direction == 1 ? 1 : 0;
                    int dy = direction == 2 ? -1 : direction == 3 ? 1 : 0;
                    int nx = x + dx, ny = y + dy;
                    if (nx >= 0 && ny >= 0 && nx < n && ny < n && picture.mask[ny * n + nx]) continue;
                    float edgeX = cx + dx * pixel * 0.5f;
                    float edgeZ = cz - dy * pixel * 0.5f;
                    bool contact = false;
                    foreach (Collider collider in block.CellColliders)
                    {
                        if (collider == null || !collider.enabled) continue;
                        Bounds b = collider.bounds;
                        float tolerance = blockCellSize * 0.13f;
                        if (dx != 0)
                            contact = cz + pixel * 0.45f > b.min.z && cz - pixel * 0.45f < b.max.z
                                && (dx < 0 ? b.center.x < edgeX && Mathf.Abs(b.max.x - edgeX) <= tolerance
                                    : b.center.x > edgeX && Mathf.Abs(b.min.x - edgeX) <= tolerance);
                        else
                            contact = cx + pixel * 0.45f > b.min.x && cx - pixel * 0.45f < b.max.x
                                && (dy < 0 ? b.center.z > edgeZ && Mathf.Abs(b.min.z - edgeZ) <= tolerance
                                    : b.center.z < edgeZ && Mathf.Abs(b.max.z - edgeZ) <= tolerance);
                        if (contact) break;
                    }
                    if (contact) count += picture.simulator.ExtractFromEdge(x, y, -dx, -dy,
                        targetColor, budget - count, extractedPositions);
                }
            }
            return count;
        }

        private void SpawnFlyingGrains(SandCollectorBlock block, List<Vector2Int> pixelPositions, SandPictureRuntime picture)
        {
            if (block == null || pixelPositions == null) return;

            Material material = GetColorMaterial(block.ColorId);
            RawImage image = picture.simulator.GetComponent<RawImage>();
            Rect rect = image.rectTransform.rect;
            for (int i = 0; i < pixelPositions.Count; i++)
            {
                Vector2Int gridPosition = pixelPositions[i];
                float u = (gridPosition.x + 0.5f) / SandSimulator.GRID_SIZE;
                float v = 1f - (gridPosition.y + 0.5f) / SandSimulator.GRID_SIZE;
                float localX = (u - image.uvRect.x) / image.uvRect.width;
                float localY = (v - image.uvRect.y) / image.uvRect.height;
                Vector3 startPosition = image.rectTransform.TransformPoint(new Vector3(
                    Mathf.LerpUnclamped(rect.xMin, rect.xMax, localX),
                    Mathf.LerpUnclamped(rect.yMin, rect.yMax, localY), 0f));

                GameObject grain = AcquireGrain();
                grain.transform.position = startPosition;
                grain.transform.localScale = Vector3.one * FlyingGrainStartScale;
                grain.GetComponent<MeshRenderer>().sharedMaterial = material;

                flyingGrains.Add(new FlyingGrain
                {
                    transform = grain.transform,
                    targetBlock = block,
                    startPosition = startPosition,
                    elapsed = 0f,
                    duration = Random.Range(0.28f, 0.48f),
                    spreadX = Random.Range(-0.09f, 0.09f),
                    spreadZ = Random.Range(-0.04f, 0.04f)
                });
            }
        }

        private void UpdateFlyingGrains()
        {
            for (int i = flyingGrains.Count - 1; i >= 0; i--)
            {
                FlyingGrain grain = flyingGrains[i];
                grain.elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(grain.elapsed / grain.duration);

                Vector3 targetPosition = grain.targetBlock != null
                    ? grain.targetBlock.transform.position + Vector3.up * 0.1f
                    : grain.startPosition;
                Vector3 position = Vector3.Lerp(grain.startPosition, targetPosition, t);
                float arc = Mathf.Sin(t * Mathf.PI);
                position.x += grain.spreadX * arc;
                position.z += grain.spreadZ * arc;
                position.y += arc * 0.22f;

                if (grain.transform != null)
                {
                    grain.transform.position = position;
                    grain.transform.localScale = Vector3.one * Mathf.Lerp(FlyingGrainStartScale, FlyingGrainEndScale, t);
                }

                if (t >= 1f || grain.targetBlock == null)
                {
                    if (grain.transform != null) ReturnGrain(grain.transform.gameObject);
                    flyingGrains.RemoveAt(i);
                }
                else
                {
                    flyingGrains[i] = grain;
                }
            }
        }

        private GameObject AcquireGrain()
        {
            GameObject grain;
            if (grainPool.Count > 0)
            {
                grain = grainPool.Pop();
                grain.SetActive(true);
                return grain;
            }

            grain = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            grain.name = "FlyingSandGrain";
            grain.transform.SetParent(transform, true);
            Collider collider = grain.GetComponent<Collider>();
            if (collider != null)
            {
                collider.enabled = false;
                Destroy(collider);
            }
            return grain;
        }

        private void ReturnGrain(GameObject grain)
        {
            grain.SetActive(false);
            grain.transform.SetParent(transform, false);
            grainPool.Push(grain);
        }

        private bool BuildContactColumnMask(SandCollectorBlock block, bool[] columnMask, SandPictureRuntime picture)
        {
            System.Array.Clear(columnMask, 0, columnMask.Length);

            IReadOnlyList<Collider> colliders = block.CellColliders;
            float leadingEdgeZ = float.NegativeInfinity;
            for (int i = 0; i < colliders.Count; i++)
            {
                Collider cell = colliders[i];
                if (cell == null || !cell.enabled || !cell.gameObject.activeInHierarchy) continue;
                leadingEdgeZ = Mathf.Max(leadingEdgeZ, cell.bounds.max.z);
            }

            float pictureBottomZ = picture.min.z;
            float distanceBelowPicture = pictureBottomZ - leadingEdgeZ;
            if (float.IsNegativeInfinity(leadingEdgeZ)
                || distanceBelowPicture < suctionGap - contactTolerance
                || distanceBelowPicture > suctionReach + contactTolerance)
                return false;

            // Collection belongs exclusively to the first placement-grid row
            // below the picture. The old distance-only test also admitted row 2.
            int leadingGridRow = Mathf.FloorToInt(
                (leadingEdgeZ - placementGridMinZ - 0.001f) / blockCellSize);
            if (leadingGridRow != placementGridRows - 1)
                return false;

            bool foundContactCell = false;
            float rowTolerance = Mathf.Max(0.01f, blockCellSize * 0.2f);
            for (int i = 0; i < colliders.Count; i++)
            {
                Collider cell = colliders[i];
                if (cell == null || !cell.enabled || !cell.gameObject.activeInHierarchy) continue;
                if (leadingEdgeZ - cell.bounds.max.z > rowTolerance) continue;

                MarkContactColumns(cell.bounds.min.x, cell.bounds.max.x, columnMask, picture);
                foundContactCell = true;
            }

            if (!foundContactCell && block.TryGetBounds(out Bounds bounds))
            {
                MarkContactColumns(bounds.min.x, bounds.max.x, columnMask, picture);
                foundContactCell = true;
            }

            return foundContactCell;
        }

        private void MarkContactColumns(float worldMinX, float worldMaxX, bool[] columnMask, SandPictureRuntime picture)
        {
            float clippedMinX = Mathf.Max(worldMinX, picture.min.x);
            float clippedMaxX = Mathf.Min(worldMaxX, picture.max.x);
            if (clippedMinX > clippedMaxX) return;

            float maxGridIndex = SandSimulator.GRID_SIZE - 1;
            int minGridX = Mathf.Clamp(
                Mathf.CeilToInt(Mathf.InverseLerp(picture.min.x, picture.max.x, clippedMinX) * maxGridIndex),
                0,
                SandSimulator.GRID_SIZE - 1);
            int maxGridX = Mathf.Clamp(
                Mathf.FloorToInt(Mathf.InverseLerp(picture.min.x, picture.max.x, clippedMaxX) * maxGridIndex),
                0,
                SandSimulator.GRID_SIZE - 1);

            if (minGridX > maxGridX)
            {
                int nearest = WorldXToGridX((clippedMinX + clippedMaxX) * 0.5f, picture);
                columnMask[nearest] = true;
                return;
            }

            for (int x = minGridX; x <= maxGridX; x++)
                columnMask[x] = true;
        }

        private int WorldXToGridX(float worldX, SandPictureRuntime picture)
        {
            float normalizedX = Mathf.InverseLerp(picture.min.x, picture.max.x, worldX);
            return Mathf.Clamp(
                Mathf.RoundToInt(normalizedX * (SandSimulator.GRID_SIZE - 1)),
                0,
                SandSimulator.GRID_SIZE - 1);
        }

        public Vector3 ClampToPlayArea(SandCollectorBlock block, Vector3 requestedPosition)
        {
            if (block == null || !block.TryGetBounds(out Bounds currentBounds))
                return requestedPosition;

            Vector3 delta = requestedPosition - block.transform.position;
            Bounds requestedBounds = currentBounds;
            requestedBounds.center += delta;

            if (requestedBounds.min.x < sandWorldMin.x)
                requestedPosition.x += sandWorldMin.x - requestedBounds.min.x;
            if (requestedBounds.max.x > sandWorldMax.x)
                requestedPosition.x -= requestedBounds.max.x - sandWorldMax.x;
            if (requestedBounds.min.z < playAreaMinZ)
                requestedPosition.z += playAreaMinZ - requestedBounds.min.z;

            // Keep a deliberate visual gap below the picture. The block collects
            // anywhere inside the suction lane, so it never has to touch the frame.
            float maxContactZ = authoredBoard != null && authoredBoard.sandInBoard ? sandWorldMax.z : sandWorldMin.z - suctionGap;
            if (requestedBounds.max.z > maxContactZ)
                requestedPosition.z -= requestedBounds.max.z - maxContactZ;

            return requestedPosition;
        }

        public Vector3 SnapToPlacementGrid(SandCollectorBlock block, Vector3 requestedPosition)
        {
            if (block == null || placementGridColumns <= 0 || placementGridRows <= 0)
                return requestedPosition;

            IReadOnlyList<Collider> colliders = block.CellColliders;
            Collider referenceCell = null;
            for (int i = 0; i < colliders.Count; i++)
            {
                if (colliders[i] == null || !colliders[i].enabled) continue;
                referenceCell = colliders[i];
                break;
            }
            if (referenceCell == null) return requestedPosition;

            Vector3 requestedCellCenter = referenceCell.bounds.center
                + (requestedPosition - block.transform.position);
            float firstCenterX = placementGridMinX + blockCellSize * 0.5f;
            float firstCenterZ = placementGridMinZ + blockCellSize * 0.5f;
            int column = Mathf.RoundToInt((requestedCellCenter.x - firstCenterX) / blockCellSize);
            int row = Mathf.RoundToInt((requestedCellCenter.z - firstCenterZ) / blockCellSize);

            int minColumnOffset = 0;
            int maxColumnOffset = 0;
            int minRowOffset = 0;
            int maxRowOffset = 0;
            for (int i = 0; i < colliders.Count; i++)
            {
                Collider cell = colliders[i];
                if (cell == null || !cell.enabled) continue;

                int columnOffset = Mathf.RoundToInt(
                    (cell.bounds.center.x - referenceCell.bounds.center.x) / blockCellSize);
                int rowOffset = Mathf.RoundToInt(
                    (cell.bounds.center.z - referenceCell.bounds.center.z) / blockCellSize);
                minColumnOffset = Mathf.Min(minColumnOffset, columnOffset);
                maxColumnOffset = Mathf.Max(maxColumnOffset, columnOffset);
                minRowOffset = Mathf.Min(minRowOffset, rowOffset);
                maxRowOffset = Mathf.Max(maxRowOffset, rowOffset);
            }

            column = Mathf.Clamp(
                column,
                -minColumnOffset,
                placementGridColumns - 1 - maxColumnOffset);
            row = Mathf.Clamp(
                row,
                -minRowOffset,
                placementGridRows - 1 - maxRowOffset);

            requestedPosition.x += firstCenterX + column * blockCellSize - requestedCellCenter.x;
            requestedPosition.z += firstCenterZ + row * blockCellSize - requestedCellCenter.z;
            return requestedPosition;
        }

        public bool IsPlacementValid(SandCollectorBlock block, Vector3 targetPosition)
        {
            Vector3 currentPosition = block.transform.position;
            block.transform.position = targetPosition;
            Physics.SyncTransforms();

            IReadOnlyList<Collider> colliders = block.CellColliders;
            bool collisionFound = false;
            for (int i = 0; i < colliders.Count && !collisionFound; i++)
            {
                Collider cell = colliders[i];
                if (cell == null || !cell.enabled) continue;

                int cellX = Mathf.FloorToInt((cell.bounds.center.x - placementGridMinX) / blockCellSize);
                int cellY = Mathf.FloorToInt((cell.bounds.center.z - placementGridMinZ) / blockCellSize);
                if (!IsPlayableCell(cellX, cellY)) { collisionFound = true; break; }

                Collider[] overlaps = Physics.OverlapBox(
                    cell.bounds.center,
                    cell.bounds.extents * 0.92f,
                    cell.transform.rotation,
                    Physics.AllLayers,
                    QueryTriggerInteraction.Ignore);

                for (int j = 0; j < overlaps.Length; j++)
                {
                    Collider overlap = overlaps[j];
                    if (overlap == null || overlap.transform.IsChildOf(block.transform)) continue;

                    if (boardObstacles.Contains(overlap)) { collisionFound = true; break; }
                    SandCollectorBlock otherBlock = overlap.GetComponentInParent<SandCollectorBlock>();
                    if (otherBlock != null && otherBlock != block && !otherBlock.IsCompleting)
                    {
                        collisionFound = true;
                        break;
                    }
                }
            }

            block.transform.position = currentPosition;
            Physics.SyncTransforms();
            return !collisionFound;
        }

        public void NotifyBlockCompleting(SandCollectorBlock block)
        {
            if (draggedBlock == block)
                draggedBlock = null;
            suctionCooldowns.Remove(block);
        }

        public void ShuffleBlocks()
        {
            List<SandCollectorBlock> activeBlocks = blocks.FindAll(block => block != null && !block.IsCompleting);
            if (activeBlocks.Count < 2) return;

            if (draggedBlock != null)
            {
                draggedBlock.EndDrag();
                draggedBlock = null;
            }

            List<Vector3> positions = new List<Vector3>(activeBlocks.Count);
            for (int i = 0; i < activeBlocks.Count; i++)
                positions.Add(activeBlocks[i].transform.position);

            for (int i = positions.Count - 1; i > 0; i--)
            {
                int other = Random.Range(0, i + 1);
                (positions[i], positions[other]) = (positions[other], positions[i]);
            }

            for (int i = 0; i < activeBlocks.Count; i++)
                activeBlocks[i].transform.position = positions[i];
            Physics.SyncTransforms();
        }

        private static bool TryGetPointerPosition(out Vector2 position)
        {
#if ENABLE_INPUT_SYSTEM
            if (Touchscreen.current != null)
            {
                var touch = Touchscreen.current.primaryTouch;
                if (touch.press.isPressed || touch.press.wasPressedThisFrame || touch.press.wasReleasedThisFrame)
                {
                    position = touch.position.ReadValue();
                    return true;
                }
            }
            if (Mouse.current != null)
            {
                position = Mouse.current.position.ReadValue();
                return true;
            }
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            position = Input.mousePosition;
            return true;
#else
            position = Vector2.zero;
            return false;
#endif
        }

        private static bool PointerPressedThisFrame()
        {
#if ENABLE_INPUT_SYSTEM
            if (Touchscreen.current != null && Touchscreen.current.primaryTouch.press.wasPressedThisFrame) return true;
            if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame) return true;
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetMouseButtonDown(0);
#else
            return false;
#endif
        }

        private static bool PointerIsPressed()
        {
#if ENABLE_INPUT_SYSTEM
            if (Touchscreen.current != null && Touchscreen.current.primaryTouch.press.isPressed) return true;
            if (Mouse.current != null && Mouse.current.leftButton.isPressed) return true;
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetMouseButton(0);
#else
            return false;
#endif
        }

        private static bool PointerReleasedThisFrame()
        {
#if ENABLE_INPUT_SYSTEM
            if (Touchscreen.current != null && Touchscreen.current.primaryTouch.press.wasReleasedThisFrame) return true;
            if (Mouse.current != null && Mouse.current.leftButton.wasReleasedThisFrame) return true;
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetMouseButtonUp(0);
#else
            return false;
#endif
        }

        private void OnDestroy()
        {
            foreach (Material material in colorMaterials.Values)
            {
                if (material != null) Destroy(material);
            }

            for (int i = 0; i < dogJamVisualMaterials.Count; i++)
                if (dogJamVisualMaterials[i] != null) Destroy(dogJamVisualMaterials[i]);
            for (int i = 0; i < dogJamSdfTextures.Count; i++)
                if (dogJamSdfTextures[i] != null) Destroy(dogJamSdfTextures[i]);
            for (int i = 0; i < dogJamMeshes.Count; i++)
                if (dogJamMeshes[i] != null) Destroy(dogJamMeshes[i]);
            if (dogJamMaterialTemplate != null) Destroy(dogJamMaterialTemplate);
            if (placementGridMaterial != null) Destroy(placementGridMaterial);
            if (placementGridMesh != null) Destroy(placementGridMesh);
        }
    }
}
