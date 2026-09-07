using System.Collections.Generic;
using TMPro;
using UnityEngine;
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

        private SandSimulator simulator;
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

        public void Initialize(SandSimulator sandSimulator, LevelData levelData, Vector3 sandMin, Vector3 sandMax)
        {
            simulator = sandSimulator;
            sandWorldMin = Vector3.Min(sandMin, sandMax);
            sandWorldMax = Vector3.Max(sandMin, sandMax);
            mainCamera = Camera.main;
            gameplayEnabled = true;

            // Scale the complete DogJam board from the picture width. Grid,
            // visual meshes, colliders and snapping all share this cell size.
            float pictureWidth = sandWorldMax.x - sandWorldMin.x;
            blockCellSize = Mathf.Max(0.05f, pictureWidth / placementGridColumnCount);

            PrepareDogJamVisualAssets();
            BuildCollectorBlocks();
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

        private void BuildCollectorBlocks()
        {
            blocks.Clear();
            suctionCooldowns.Clear();

            List<int> activeColors = new List<int>();
            for (int colorId = 1; colorId < SandSimulator.PaletteColors.Length; colorId++)
            {
                if (simulator.colorCounts.TryGetValue(colorId, out int count) && count > 0)
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
                int colorTotal = simulator.colorCounts[colorId];
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
                    quota,
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
                    float load = (float)simulator.colorCounts[colorId] / (assigned[colorId] + 1);
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

        private void CreateCollectorBlock(int index, int colorId, int quota, Vector2Int[] shape, Vector3 position)
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
            block.Initialize(this, colorId, quota, colliders, counter);
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

            placementGridTopZ = sandWorldMin.z - suctionGap;
            placementGridRows = Mathf.Max(
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
                bool isSuctionRow = row == placementGridRows - 1;
                for (int column = 0; column < placementGridColumns; column++)
                {
                    Color color = isSuctionRow
                        ? suctionGridColor
                        : ((row + column) & 1) == 0 ? gridColorA : gridColorB;
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

            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            material = new Material(shader);
            Color color = SandSimulator.PaletteColors[colorId];
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            else material.color = color;
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0.45f);
            colorMaterials[colorId] = material;
            return material;
        }

        private void Update()
        {
            if (simulator == null) return;

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

                if (!BuildContactColumnMask(block, contactGridColumns)) continue;
                if (!block.TryGetBounds(out Bounds blockBounds)) continue;

                int centerGridX = WorldXToGridX(blockBounds.center.x);
                extractedPositions.Clear();
                int extracted = simulator.ExtractExposedPixelsInColumns(
                    centerGridX,
                    (byte)block.ColorId,
                    Mathf.Min(grainsPerSuction, block.Remaining),
                    extractedPositions,
                    contactGridColumns);
                if (extracted <= 0)
                {
                    suctionCooldowns[block] = suctionInterval;
                    continue;
                }

                SpawnFlyingGrains(block, extractedPositions);
                block.Absorb(extracted);
                suctionCooldowns[block] = suctionInterval;
                if (HypercasualGameEngine.SoundManager.Instance != null)
                    HypercasualGameEngine.SoundManager.Instance.PlaySandFlowSandPour();
            }
        }

        private void SpawnFlyingGrains(SandCollectorBlock block, List<Vector2Int> pixelPositions)
        {
            if (block == null || pixelPositions == null) return;

            Material material = GetColorMaterial(block.ColorId);
            float spawnWorldY = Mathf.Max(sandWorldMin.y, sandWorldMax.y) + 0.05f;

            for (int i = 0; i < pixelPositions.Count; i++)
            {
                Vector2Int gridPosition = pixelPositions[i];
                float normalizedX = (float)gridPosition.x / (SandSimulator.GRID_SIZE - 1);
                float normalizedY = (float)gridPosition.y / (SandSimulator.GRID_SIZE - 1);
                Vector3 startPosition = new Vector3(
                    Mathf.Lerp(sandWorldMin.x, sandWorldMax.x, normalizedX),
                    spawnWorldY,
                    Mathf.Lerp(sandWorldMax.z, sandWorldMin.z, normalizedY));

                GameObject grain = AcquireGrain();
                grain.transform.position = startPosition;
                grain.transform.localScale = Vector3.one * 0.055f;
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
                    grain.transform.localScale = Vector3.one * Mathf.Lerp(0.055f, 0.015f, t);
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

        private bool BuildContactColumnMask(SandCollectorBlock block, bool[] columnMask)
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

            float pictureBottomZ = sandWorldMin.z;
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

                MarkContactColumns(cell.bounds.min.x, cell.bounds.max.x, columnMask);
                foundContactCell = true;
            }

            if (!foundContactCell && block.TryGetBounds(out Bounds bounds))
            {
                MarkContactColumns(bounds.min.x, bounds.max.x, columnMask);
                foundContactCell = true;
            }

            return foundContactCell;
        }

        private void MarkContactColumns(float worldMinX, float worldMaxX, bool[] columnMask)
        {
            float clippedMinX = Mathf.Max(worldMinX, sandWorldMin.x);
            float clippedMaxX = Mathf.Min(worldMaxX, sandWorldMax.x);
            if (clippedMinX > clippedMaxX) return;

            float maxGridIndex = SandSimulator.GRID_SIZE - 1;
            int minGridX = Mathf.Clamp(
                Mathf.CeilToInt(Mathf.InverseLerp(sandWorldMin.x, sandWorldMax.x, clippedMinX) * maxGridIndex),
                0,
                SandSimulator.GRID_SIZE - 1);
            int maxGridX = Mathf.Clamp(
                Mathf.FloorToInt(Mathf.InverseLerp(sandWorldMin.x, sandWorldMax.x, clippedMaxX) * maxGridIndex),
                0,
                SandSimulator.GRID_SIZE - 1);

            if (minGridX > maxGridX)
            {
                int nearest = WorldXToGridX((clippedMinX + clippedMaxX) * 0.5f);
                columnMask[nearest] = true;
                return;
            }

            for (int x = minGridX; x <= maxGridX; x++)
                columnMask[x] = true;
        }

        private int WorldXToGridX(float worldX)
        {
            float normalizedX = Mathf.InverseLerp(sandWorldMin.x, sandWorldMax.x, worldX);
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
            float maxContactZ = sandWorldMin.z - suctionGap;
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
