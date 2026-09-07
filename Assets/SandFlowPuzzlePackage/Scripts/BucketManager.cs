using UnityEngine;
using System.Collections.Generic;

namespace SandFlowPuzzle
{
    public class BucketManager : MonoBehaviour
    {
        [Header("References")]
        public Transform bucketsContainer;
        public Transform gridCellBackgrounds;

        [Header("Layout (World Space units)")]
        public float cellWidth = 0.45f;
        public float cellHeight = 0.45f;
        public float cellSpacingX = 0f;
        public float cellSpacingY = 0f;
        public float gridOriginZ = 0.7f;

        // Layout definitions matching React prototype
        private struct LayoutDef
        {
            public string id;
            public int col, row;
            public string type; // "BL", "WH", "RE", "OR", "MY"
        }

        private static readonly LayoutDef[] LAYOUT_DEFS = new LayoutDef[]
        {
            new LayoutDef { id="p0",  col=0, row=0, type="OR" },
            new LayoutDef { id="p1",  col=1, row=0, type="MY" },
            new LayoutDef { id="p2",  col=2, row=0, type="OR" },
            new LayoutDef { id="p3",  col=3, row=0, type="MY" },
            new LayoutDef { id="p4",  col=4, row=0, type="OR" },

            new LayoutDef { id="p5",  col=0, row=1, type="BL" },
            new LayoutDef { id="p6",  col=1, row=1, type="RE" },
            new LayoutDef { id="p7",  col=2, row=1, type="BL" },
            new LayoutDef { id="p8",  col=3, row=1, type="RE" },
            new LayoutDef { id="p9",  col=4, row=1, type="BL" },

            new LayoutDef { id="p10", col=0, row=2, type="WH" },
            new LayoutDef { id="p11", col=1, row=2, type="MY" },
            new LayoutDef { id="p12", col=2, row=2, type="WH" },
            new LayoutDef { id="p13", col=3, row=2, type="MY" },
            new LayoutDef { id="p14", col=4, row=2, type="WH" },

            new LayoutDef { id="p15", col=0, row=3, type="RE" },
            new LayoutDef { id="p16", col=1, row=3, type="OR" },
            new LayoutDef { id="p17", col=2, row=3, type="RE" },
            new LayoutDef { id="p18", col=3, row=3, type="OR" },
            new LayoutDef { id="p19", col=4, row=3, type="RE" },

            new LayoutDef { id="p20", col=1, row=4, type="MY" },
            new LayoutDef { id="p21", col=3, row=4, type="MY" },
        };

        // Valid cell positions
        private static readonly HashSet<string> VALID_CELLS_SET = new HashSet<string>();

        static BucketManager()
        {
            for (int r = 0; r < 4; r++)
                for (int c = 0; c < 5; c++)
                    VALID_CELLS_SET.Add($"{c},{r}");
            VALID_CELLS_SET.Add("1,4");
            VALID_CELLS_SET.Add("3,4");
        }

        // Dynamic valid cells (populated from LevelData; if empty, falls back to VALID_CELLS_SET)
        private HashSet<string> dynamicValidCells = new HashSet<string>();

        private HashSet<string> ActiveValidCells =>
            dynamicValidCells.Count > 0 ? dynamicValidCells : VALID_CELLS_SET;

        public List<BucketData> allBuckets = new List<BucketData>();
        public System.Action<BucketData> onBucketClicked;

        private bool magicWandActive;
        private BucketData hoveredMagicBucket;
        private Dictionary<Collider, BucketData> colliderMap = new Dictionary<Collider, BucketData>();
        private Material outlineMat;
        private Material magicOutlineMat;
        private Mesh bucketMesh;
        private Mesh groundMesh;

        // Shuffle animation state — all buckets fly simultaneously
        private bool shuffleAnimActive;
        private float shuffleAnimT;
        private const float SHUFFLE_ANIM_DURATION = 0.4f;
        private Dictionary<BucketData, Vector3> shuffleFromPos = new Dictionary<BucketData, Vector3>();
        private Dictionary<BucketData, Vector3> shuffleToPos = new Dictionary<BucketData, Vector3>();

        // Dynamic grid config (set from LevelData, or null for default)
        private LevelData activeLevelData;

        public void Initialize(Dictionary<int, int> colorCounts)
        {
            Initialize(colorCounts, null);
        }

        public void Initialize(Dictionary<int, int> colorCounts, LevelData levelData)
        {
            activeLevelData = levelData;

            // Load custom bucket mesh from Resources
            var model = Resources.Load<GameObject>("cylinder-hole-new");
            if (model != null)
            {
                var mf = model.GetComponentInChildren<MeshFilter>();
                if (mf != null) bucketMesh = mf.sharedMesh;
            }
            if (bucketMesh == null)
                Debug.LogWarning("[BucketManager] cylinder-hole-new mesh not found, falling back to primitive");

            // Load ground mesh for tile backgrounds
            var groundModel = Resources.Load<GameObject>("ground");
            if (groundModel != null)
            {
                var gmf = groundModel.GetComponentInChildren<MeshFilter>();
                if (gmf != null) groundMesh = gmf.sharedMesh;
            }
            if (groundMesh == null)
                Debug.LogWarning("[BucketManager] ground mesh not found, falling back to Quad");

            // Create shared outline material (extra material slot technique)
            var outlineShader = Resources.Load<Shader>("BucketOutline") ?? Shader.Find("Custom/OutlineOnly");
            if (outlineShader != null)
            {
                outlineMat = new Material(outlineShader);
                outlineMat.SetColor("_OutlineColor", new Color(0.08f, 0.08f, 0.08f, 1f));
                outlineMat.SetFloat("_OutlineWidth", 0.1f);

                magicOutlineMat = new Material(outlineShader);
                magicOutlineMat.SetColor("_OutlineColor", new Color(0.2f, 0.7f, 1f, 1f)); // cyan
                magicOutlineMat.SetFloat("_OutlineWidth", 0.12f);
            }
            else
            {
                Debug.LogWarning("[BucketManager] Custom/OutlineOnly shader not found, outlines disabled");
            }

            allBuckets.Clear();
            colliderMap.Clear();

            if (levelData != null && levelData.buckets != null && levelData.buckets.Count > 0)
            {
                // Apply spacing from level data
                cellSpacingX = levelData.bucketSpacingX;
                cellSpacingY = levelData.bucketSpacingY;

                // Build valid cells set from level data
                dynamicValidCells.Clear();
                if (levelData.gridCellEnabled != null)
                {
                    for (int r = 0; r < levelData.bucketRows; r++)
                    {
                        for (int c = 0; c < levelData.bucketColumns; c++)
                        {
                            int idx = r * levelData.bucketColumns + c;
                            if (idx < levelData.gridCellEnabled.Count && levelData.gridCellEnabled[idx])
                                dynamicValidCells.Add($"{c},{r}");
                        }
                    }
                }

                // Determine how many palette colors exist
                int paletteCount = levelData.palette != null ? levelData.palette.Count : 4;

                // Create buckets from level data
                int mysteryCount = 0;
                foreach (var def in levelData.buckets)
                    if (def.isMystery) mysteryCount++;

                // Assign mystery bucket colors proportionally
                List<int> mysteryColorAssignments = new List<int>();
                if (mysteryCount > 0)
                {
                    int totalPixels = SandSimulator.GRID_SIZE * SandSimulator.GRID_SIZE;
                    int totalPots = levelData.buckets.Count;

                    // Count non-mystery buckets per color
                    Dictionary<int, int> assignedPerColor = new Dictionary<int, int>();
                    for (int c = 1; c <= paletteCount; c++) assignedPerColor[c] = 0;
                    foreach (var def in levelData.buckets)
                    {
                        if (!def.isMystery && def.colorId >= 1 && def.colorId <= paletteCount)
                            assignedPerColor[def.colorId]++;
                    }

                    for (int c = 1; c <= paletteCount; c++)
                    {
                        int count = colorCounts.ContainsKey(c) ? colorCounts[c] : 0;
                        int idealNum = totalPixels > 0 ? Mathf.RoundToInt((float)count / totalPixels * totalPots) : 1;
                        int needed = Mathf.Max(0, idealNum - assignedPerColor[c]);
                        for (int i = 0; i < needed; i++)
                            mysteryColorAssignments.Add(c);
                    }

                    while (mysteryColorAssignments.Count < mysteryCount)
                        mysteryColorAssignments.Add(1);
                    while (mysteryColorAssignments.Count > mysteryCount)
                        mysteryColorAssignments.RemoveAt(mysteryColorAssignments.Count - 1);

                    // Shuffle
                    for (int i = mysteryColorAssignments.Count - 1; i > 0; i--)
                    {
                        int j = Random.Range(0, i + 1);
                        (mysteryColorAssignments[i], mysteryColorAssignments[j]) = (mysteryColorAssignments[j], mysteryColorAssignments[i]);
                    }
                }

                int myIdx = 0;
                for (int i = 0; i < levelData.buckets.Count; i++)
                {
                    BucketDef def = levelData.buckets[i];
                    bool isMystery = def.isMystery;
                    int cId = isMystery ? mysteryColorAssignments[myIdx++] : def.colorId;
                    string cStr = ColorIdToStr(cId);

                    var bucket = new BucketData($"p{i}", def.col, def.row, cId, cStr, isMystery);
                    allBuckets.Add(bucket);
                }
            }
            else
            {
                // Fallback to hardcoded LAYOUT_DEFS
                dynamicValidCells.Clear(); // empty = use static VALID_CELLS_SET

                int totalPots = LAYOUT_DEFS.Length;
                Dictionary<string, int> assignedTypes = new Dictionary<string, int>
                {
                    { "BL", 0 }, { "WH", 0 }, { "RE", 0 }, { "OR", 0 }
                };

                foreach (var def in LAYOUT_DEFS)
                {
                    if (def.type != "MY" && assignedTypes.ContainsKey(def.type))
                        assignedTypes[def.type]++;
                }

                List<string> mysteryAssignments = new List<string>();
                int totalPixels = SandSimulator.GRID_SIZE * SandSimulator.GRID_SIZE;
                string[] colorNames = { "", "BL", "WH", "RE", "OR" };

                for (int c = 1; c <= 4; c++)
                {
                    string cStr = colorNames[c];
                    int count = colorCounts.ContainsKey(c) ? colorCounts[c] : 0;
                    int idealNum = Mathf.RoundToInt((float)count / totalPixels * totalPots);
                    int needed = Mathf.Max(0, idealNum - assignedTypes[cStr]);
                    for (int i = 0; i < needed; i++)
                        mysteryAssignments.Add(cStr);
                }

                while (mysteryAssignments.Count < 6) mysteryAssignments.Add("BL");
                while (mysteryAssignments.Count > 6) mysteryAssignments.RemoveAt(mysteryAssignments.Count - 1);

                for (int i = mysteryAssignments.Count - 1; i > 0; i--)
                {
                    int j = Random.Range(0, i + 1);
                    (mysteryAssignments[i], mysteryAssignments[j]) = (mysteryAssignments[j], mysteryAssignments[i]);
                }

                int myIdx = 0;
                foreach (var def in LAYOUT_DEFS)
                {
                    bool isMystery = def.type == "MY";
                    string cStr = isMystery ? mysteryAssignments[myIdx++] : def.type;
                    int cId = ColorStrToId(cStr);

                    var bucket = new BucketData(def.id, def.col, def.row, cId, cStr, isMystery);
                    allBuckets.Add(bucket);
                }
            }

            // Calculate quotas
            int maxColorId = 4;
            if (levelData != null && levelData.palette != null)
                maxColorId = levelData.palette.Count;

            Dictionary<int, List<BucketData>> potsByColor = new Dictionary<int, List<BucketData>>();
            for (int c = 1; c <= maxColorId; c++) potsByColor[c] = new List<BucketData>();

            foreach (var b in allBuckets)
            {
                if (!potsByColor.ContainsKey(b.trueColorId))
                    potsByColor[b.trueColorId] = new List<BucketData>();
                potsByColor[b.trueColorId].Add(b);
            }

            foreach (var kvp in potsByColor)
            {
                int req = colorCounts.ContainsKey(kvp.Key) ? colorCounts[kvp.Key] : 0;
                var pList = kvp.Value;
                if (pList.Count == 0) continue;

                int baseQuota = req / pList.Count;
                int rem = req % pList.Count;
                for (int i = 0; i < pList.Count; i++)
                {
                    pList[i].quota = baseQuota + (i < rem ? 1 : 0);
                    pList[i].maxQuota = pList[i].quota;
                }
            }

            // Create 3D visuals
            CreateGridBackgrounds();
            CreateBucketVisuals();
            UpdateAllBucketVisuals();
        }

        private void CreateGridBackgrounds()
        {
            // Clear existing
            for (int i = gridCellBackgrounds.childCount - 1; i >= 0; i--)
                Destroy(gridCellBackgrounds.GetChild(i).gameObject);

            int cols = activeLevelData != null ? activeLevelData.bucketColumns : 5;
            int rows = activeLevelData != null ? activeLevelData.bucketRows : 5;
            float centerCol = (cols - 1) / 2f;
            float halfW = cellWidth / 2f;
            float halfH = cellHeight / 2f;

            // --- 1) Individual tiles under each bucket (enabled cells) using ground.fbx ---
            const float tileY = -0.08f;
            Material tileMat = CreateLitMaterial(new Color(0.33f, 0.39f, 0.48f, 0.55f));
            SetMaterialTransparent(tileMat);
            tileMat.SetFloat("_Metallic", 1f);
            tileMat.SetFloat("_Smoothness", 1f);

            foreach (var cellKey in ActiveValidCells)
            {
                string[] parts = cellKey.Split(',');
                int c = int.Parse(parts[0]);
                int r = int.Parse(parts[1]);

                float cx = (c - centerCol) * (cellWidth + cellSpacingX);
                float cz = gridOriginZ + ((rows - 1) - r) * (cellHeight + cellSpacingY);

                GameObject tile;
                if (groundMesh != null)
                {
                    tile = new GameObject($"Tile_{c}_{r}");
                    tile.AddComponent<MeshFilter>().sharedMesh = groundMesh;
                    tile.AddComponent<MeshRenderer>();
                }
                else
                {
                    tile = GameObject.CreatePrimitive(PrimitiveType.Quad);
                    tile.name = $"Tile_{c}_{r}";
                    var tileCol = tile.GetComponent<Collider>();
                    if (tileCol != null) Destroy(tileCol);
                }
                tile.transform.SetParent(gridCellBackgrounds, false);
                tile.transform.localPosition = new Vector3(cx, tileY, cz);
                tile.transform.localScale = new Vector3(19.8f, 19.8f, 1f);
                tile.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

                MeshRenderer tileRenderer = tile.GetComponent<MeshRenderer>();
                int subCount = groundMesh != null ? groundMesh.subMeshCount : 1;
                Material[] tileMats = new Material[subCount];
                for (int m = 0; m < subCount; m++) tileMats[m] = tileMat;
                tileRenderer.materials = tileMats;
            }

            // --- 2) Merged wall border surrounding all tiles + disabled cells ---
            // Wall = 1-cell border around the grid on all sides + any disabled cells within
            HashSet<string> wallCells = new HashSet<string>();

            // Border ring: 1 cell outside grid on left, right, and front (bottom). No wall on top (back, r=-1).
            for (int r = 0; r <= rows; r++)
                for (int c = -1; c <= cols; c++)
                {
                    bool isInGrid = r >= 0 && r < rows && c >= 0 && c < cols;
                    if (!isInGrid)
                    {
                        // Outside the grid = always wall
                        wallCells.Add($"{c},{r}");
                    }
                    else if (!ActiveValidCells.Contains($"{c},{r}"))
                    {
                        // Inside grid but disabled = wall
                        wallCells.Add($"{c},{r}");
                    }
                }

            if (wallCells.Count == 0) return;

            const float wallTopY = 0.10f;
            const float wallBottomY = -0.12f;
            const float bR = 0.045f; // bevel radius
            const int bSegs = 8;     // arc segments

            Material wallMat = CreateLitMaterial(new Color(0.553f, 0.627f, 0.745f, 1f)); // #8DA0BE
            wallMat.SetFloat("_Metallic", 0.5f);
            wallMat.SetFloat("_Smoothness", 0f);

            // Darker edge material for bevels
            Material wallEdgeMat = CreateLitMaterial(new Color(0.44f, 0.50f, 0.60f, 1f));
            wallEdgeMat.SetFloat("_Metallic", 0.5f);
            wallEdgeMat.SetFloat("_Smoothness", 0f);

            // Flood-fill connected wall groups
            var wallGroups = FloodFillGroups(wallCells);

            int groupIdx = 0;
            foreach (var group in wallGroups)
            {
                var verts = new List<Vector3>();
                var norms = new List<Vector3>();
                var bodyTris = new List<int>();  // submesh 0: flat faces (top + sides)
                var bevelTris = new List<int>(); // submesh 1: bevel faces (darker)

                foreach (var cellKey in group)
                {
                    string[] parts = cellKey.Split(',');
                    int c = int.Parse(parts[0]);
                    int r = int.Parse(parts[1]);

                    float cx = (c - centerCol) * (cellWidth + cellSpacingX);
                    float cz = gridOriginZ + ((rows - 1) - r) * (cellHeight + cellSpacingY);

                    bool hasLeft  = group.Contains($"{c - 1},{r}");
                    bool hasRight = group.Contains($"{c + 1},{r}");
                    bool hasFront = group.Contains($"{c},{r + 1}");
                    bool hasBack  = group.Contains($"{c},{r - 1}");

                    float eL = cx - halfW;
                    float eR = cx + halfW;
                    float eF = cz - halfH;
                    float eB = cz + halfH;

                    // Inset top face (pulled in by bR on exposed edges)
                    float tL = hasLeft  ? eL : eL + bR;
                    float tR = hasRight ? eR : eR - bR;
                    float tF = hasFront ? eF : eF + bR;
                    float tB = hasBack  ? eB : eB - bR;

                    AddDoubleSidedQuad(verts, norms, bodyTris,
                        new Vector3(tL, wallTopY, tF),
                        new Vector3(tR, wallTopY, tF),
                        new Vector3(tR, wallTopY, tB),
                        new Vector3(tL, wallTopY, tB),
                        Vector3.up);

                    float sideTopY = wallTopY - bR;

                    // Edge bevels + side faces
                    if (!hasLeft)
                    {
                        float zMin = !hasFront ? eF + bR : eF;
                        float zMax = !hasBack  ? eB - bR : eB;
                        AddBevelEdgeX(verts, norms, bevelTris, eL, bR, wallTopY, zMin, zMax, bSegs, true);
                        AddSideQuadX(verts, norms, bodyTris, eL, sideTopY, wallBottomY, zMin, zMax, true);
                    }
                    if (!hasRight)
                    {
                        float zMin = !hasFront ? eF + bR : eF;
                        float zMax = !hasBack  ? eB - bR : eB;
                        AddBevelEdgeX(verts, norms, bevelTris, eR, bR, wallTopY, zMin, zMax, bSegs, false);
                        AddSideQuadX(verts, norms, bodyTris, eR, sideTopY, wallBottomY, zMin, zMax, false);
                    }
                    if (!hasFront)
                    {
                        float xMin = !hasLeft  ? eL + bR : eL;
                        float xMax = !hasRight ? eR - bR : eR;
                        AddBevelEdgeZ(verts, norms, bevelTris, eF, bR, wallTopY, xMin, xMax, bSegs, true);
                        AddSideQuadZ(verts, norms, bodyTris, eF, sideTopY, wallBottomY, xMin, xMax, true);
                    }
                    if (!hasBack)
                    {
                        float xMin = !hasLeft  ? eL + bR : eL;
                        float xMax = !hasRight ? eR - bR : eR;
                        AddBevelEdgeZ(verts, norms, bevelTris, eB, bR, wallTopY, xMin, xMax, bSegs, false);
                        AddSideQuadZ(verts, norms, bodyTris, eB, sideTopY, wallBottomY, xMin, xMax, false);
                    }

                    // Convex corners: vertical cylinder bevel + sphere cap
                    if (!hasLeft && !hasFront)
                    {
                        AddVerticalBevel(verts, norms, bevelTris, eL + bR, eF + bR, bR, sideTopY, wallBottomY, bSegs, -1f, -1f);
                        AddBevelCorner(verts, norms, bevelTris, eL + bR, wallTopY - bR, eF + bR, bR, bSegs, -1f, -1f);
                    }
                    if (!hasRight && !hasFront)
                    {
                        AddVerticalBevel(verts, norms, bevelTris, eR - bR, eF + bR, bR, sideTopY, wallBottomY, bSegs, 1f, -1f);
                        AddBevelCorner(verts, norms, bevelTris, eR - bR, wallTopY - bR, eF + bR, bR, bSegs, 1f, -1f);
                    }
                    if (!hasLeft && !hasBack)
                    {
                        AddVerticalBevel(verts, norms, bevelTris, eL + bR, eB - bR, bR, sideTopY, wallBottomY, bSegs, -1f, 1f);
                        AddBevelCorner(verts, norms, bevelTris, eL + bR, wallTopY - bR, eB - bR, bR, bSegs, -1f, 1f);
                    }
                    if (!hasRight && !hasBack)
                    {
                        AddVerticalBevel(verts, norms, bevelTris, eR - bR, eB - bR, bR, sideTopY, wallBottomY, bSegs, 1f, 1f);
                        AddBevelCorner(verts, norms, bevelTris, eR - bR, wallTopY - bR, eB - bR, bR, bSegs, 1f, 1f);
                    }

                    // Concave corners: fill gaps where bevels from adjacent cells don't meet
                    bool hasDiagFL = group.Contains($"{c - 1},{r + 1}");
                    bool hasDiagFR = group.Contains($"{c + 1},{r + 1}");
                    bool hasDiagBL = group.Contains($"{c - 1},{r - 1}");
                    bool hasDiagBR = group.Contains($"{c + 1},{r - 1}");

                    if (hasLeft && hasFront && !hasDiagFL)
                        AddConcaveBevelCorner(verts, norms, bevelTris, eL, wallTopY, eF, bR, bSegs, 1f, 1f);
                    if (hasRight && hasFront && !hasDiagFR)
                        AddConcaveBevelCorner(verts, norms, bevelTris, eR, wallTopY, eF, bR, bSegs, -1f, 1f);
                    if (hasLeft && hasBack && !hasDiagBL)
                        AddConcaveBevelCorner(verts, norms, bevelTris, eL, wallTopY, eB, bR, bSegs, 1f, -1f);
                    if (hasRight && hasBack && !hasDiagBR)
                        AddConcaveBevelCorner(verts, norms, bevelTris, eR, wallTopY, eB, bR, bSegs, -1f, -1f);
                }

                Mesh mesh = new Mesh();
                mesh.SetVertices(verts);
                mesh.SetNormals(norms);
                mesh.subMeshCount = 2;
                mesh.SetTriangles(bodyTris, 0);
                mesh.SetTriangles(bevelTris, 1);
                mesh.RecalculateBounds();

                GameObject go = new GameObject($"WallGroup_{groupIdx}");
                go.transform.SetParent(gridCellBackgrounds, false);
                go.transform.localPosition = Vector3.zero;
                go.transform.localRotation = Quaternion.identity;
                go.transform.localScale = Vector3.one;
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                MeshRenderer mr = go.AddComponent<MeshRenderer>();
                mr.materials = new Material[] { wallMat, wallEdgeMat };

                groupIdx++;
            }
        }

        private List<HashSet<string>> FloodFillGroups(HashSet<string> cells)
        {
            var remaining = new HashSet<string>(cells);
            var groups = new List<HashSet<string>>();

            while (remaining.Count > 0)
            {
                string seed = null;
                foreach (var s in remaining) { seed = s; break; }
                remaining.Remove(seed);

                var group = new HashSet<string> { seed };
                var queue = new Queue<string>();
                queue.Enqueue(seed);

                while (queue.Count > 0)
                {
                    string current = queue.Dequeue();
                    string[] parts = current.Split(',');
                    int cx = int.Parse(parts[0]);
                    int cy = int.Parse(parts[1]);

                    TryEnqueueCell(remaining, group, queue, cx - 1, cy);
                    TryEnqueueCell(remaining, group, queue, cx + 1, cy);
                    TryEnqueueCell(remaining, group, queue, cx, cy - 1);
                    TryEnqueueCell(remaining, group, queue, cx, cy + 1);
                }

                groups.Add(group);
            }
            return groups;
        }

        private void TryEnqueueCell(HashSet<string> remaining, HashSet<string> group, Queue<string> queue, int x, int y)
        {
            string key = $"{x},{y}";
            if (remaining.Contains(key))
            {
                remaining.Remove(key);
                group.Add(key);
                queue.Enqueue(key);
            }
        }

        private static void AddSideQuadX(List<Vector3> verts, List<Vector3> norms, List<int> tris,
            float xPos, float topY, float bottomY, float zMin, float zMax, bool isLeft)
        {
            int vi = verts.Count;
            Vector3 normal = isLeft ? Vector3.left : Vector3.right;
            if (isLeft)
            {
                verts.Add(new Vector3(xPos, bottomY, zMax));
                verts.Add(new Vector3(xPos, bottomY, zMin));
                verts.Add(new Vector3(xPos, topY, zMin));
                verts.Add(new Vector3(xPos, topY, zMax));
            }
            else
            {
                verts.Add(new Vector3(xPos, bottomY, zMin));
                verts.Add(new Vector3(xPos, bottomY, zMax));
                verts.Add(new Vector3(xPos, topY, zMax));
                verts.Add(new Vector3(xPos, topY, zMin));
            }
            for (int n = 0; n < 4; n++) norms.Add(normal);
            // Both windings for double-sided rendering
            tris.AddRange(new[] { vi, vi + 1, vi + 2, vi, vi + 2, vi + 3 });
            tris.AddRange(new[] { vi, vi + 2, vi + 1, vi, vi + 3, vi + 2 });
        }

        private static void AddSideQuadZ(List<Vector3> verts, List<Vector3> norms, List<int> tris,
            float zPos, float topY, float bottomY, float xMin, float xMax, bool isFront)
        {
            int vi = verts.Count;
            Vector3 normal = isFront ? Vector3.back : Vector3.forward;
            if (isFront)
            {
                verts.Add(new Vector3(xMin, bottomY, zPos));
                verts.Add(new Vector3(xMax, bottomY, zPos));
                verts.Add(new Vector3(xMax, topY, zPos));
                verts.Add(new Vector3(xMin, topY, zPos));
            }
            else
            {
                verts.Add(new Vector3(xMax, bottomY, zPos));
                verts.Add(new Vector3(xMin, bottomY, zPos));
                verts.Add(new Vector3(xMin, topY, zPos));
                verts.Add(new Vector3(xMax, topY, zPos));
            }
            for (int n = 0; n < 4; n++) norms.Add(normal);
            // Both windings for double-sided rendering
            tris.AddRange(new[] { vi, vi + 1, vi + 2, vi, vi + 2, vi + 3 });
            tris.AddRange(new[] { vi, vi + 2, vi + 1, vi, vi + 3, vi + 2 });
        }

        private static void AddDoubleSidedQuad(List<Vector3> verts, List<Vector3> norms, List<int> tris,
            Vector3 v0, Vector3 v1, Vector3 v2, Vector3 v3, Vector3 normal)
        {
            int vi = verts.Count;
            verts.Add(v0); verts.Add(v1); verts.Add(v2); verts.Add(v3);
            for (int n = 0; n < 4; n++) norms.Add(normal);
            tris.AddRange(new[] { vi, vi + 1, vi + 2, vi, vi + 2, vi + 3 });
            tris.AddRange(new[] { vi, vi + 2, vi + 1, vi, vi + 3, vi + 2 });
        }

        /// <summary>Bevel arc strip along an X-perpendicular edge (left or right).</summary>
        private static void AddBevelEdgeX(List<Vector3> verts, List<Vector3> norms, List<int> tris,
            float edgeX, float bR, float topY, float zMin, float zMax, int segs, bool isLeft)
        {
            float sign = isLeft ? 1f : -1f;
            float arcCX = edgeX + bR * sign;
            float arcCY = topY - bR;

            for (int i = 0; i < segs; i++)
            {
                float a0 = Mathf.PI * 0.5f * i / segs;
                float a1 = Mathf.PI * 0.5f * (i + 1) / segs;

                float x0 = arcCX - sign * bR * Mathf.Sin(a0);
                float y0 = arcCY + bR * Mathf.Cos(a0);
                float x1 = arcCX - sign * bR * Mathf.Sin(a1);
                float y1 = arcCY + bR * Mathf.Cos(a1);

                Vector3 n0 = new Vector3(-sign * Mathf.Sin(a0), Mathf.Cos(a0), 0f).normalized;
                Vector3 n1 = new Vector3(-sign * Mathf.Sin(a1), Mathf.Cos(a1), 0f).normalized;

                int vi = verts.Count;
                verts.Add(new Vector3(x0, y0, zMin)); norms.Add(n0);
                verts.Add(new Vector3(x0, y0, zMax)); norms.Add(n0);
                verts.Add(new Vector3(x1, y1, zMax)); norms.Add(n1);
                verts.Add(new Vector3(x1, y1, zMin)); norms.Add(n1);
                tris.AddRange(new[] { vi, vi + 1, vi + 2, vi, vi + 2, vi + 3 });
                tris.AddRange(new[] { vi, vi + 2, vi + 1, vi, vi + 3, vi + 2 });
            }
        }

        /// <summary>Bevel arc strip along a Z-perpendicular edge (front or back).</summary>
        private static void AddBevelEdgeZ(List<Vector3> verts, List<Vector3> norms, List<int> tris,
            float edgeZ, float bR, float topY, float xMin, float xMax, int segs, bool isFront)
        {
            float sign = isFront ? 1f : -1f;
            float arcCZ = edgeZ + bR * sign;
            float arcCY = topY - bR;

            for (int i = 0; i < segs; i++)
            {
                float a0 = Mathf.PI * 0.5f * i / segs;
                float a1 = Mathf.PI * 0.5f * (i + 1) / segs;

                float z0 = arcCZ - sign * bR * Mathf.Sin(a0);
                float y0 = arcCY + bR * Mathf.Cos(a0);
                float z1 = arcCZ - sign * bR * Mathf.Sin(a1);
                float y1 = arcCY + bR * Mathf.Cos(a1);

                Vector3 n0 = new Vector3(0f, Mathf.Cos(a0), -sign * Mathf.Sin(a0)).normalized;
                Vector3 n1 = new Vector3(0f, Mathf.Cos(a1), -sign * Mathf.Sin(a1)).normalized;

                int vi = verts.Count;
                verts.Add(new Vector3(xMin, y0, z0)); norms.Add(n0);
                verts.Add(new Vector3(xMax, y0, z0)); norms.Add(n0);
                verts.Add(new Vector3(xMax, y1, z1)); norms.Add(n1);
                verts.Add(new Vector3(xMin, y1, z1)); norms.Add(n1);
                tris.AddRange(new[] { vi, vi + 1, vi + 2, vi, vi + 2, vi + 3 });
                tris.AddRange(new[] { vi, vi + 2, vi + 1, vi, vi + 3, vi + 2 });
            }
        }

        /// <summary>Quarter-cylinder bevel at a convex vertical corner.</summary>
        private static void AddVerticalBevel(List<Vector3> verts, List<Vector3> norms, List<int> tris,
            float centerX, float centerZ, float bR, float topY, float bottomY, int segs, float xSign, float zSign)
        {
            for (int i = 0; i < segs; i++)
            {
                float a0 = Mathf.PI * 0.5f * i / segs;
                float a1 = Mathf.PI * 0.5f * (i + 1) / segs;

                float x0 = centerX + xSign * bR * Mathf.Cos(a0);
                float z0 = centerZ + zSign * bR * Mathf.Sin(a0);
                float x1 = centerX + xSign * bR * Mathf.Cos(a1);
                float z1 = centerZ + zSign * bR * Mathf.Sin(a1);

                Vector3 n0 = new Vector3(xSign * Mathf.Cos(a0), 0f, zSign * Mathf.Sin(a0)).normalized;
                Vector3 n1 = new Vector3(xSign * Mathf.Cos(a1), 0f, zSign * Mathf.Sin(a1)).normalized;

                int vi = verts.Count;
                verts.Add(new Vector3(x0, bottomY, z0)); norms.Add(n0);
                verts.Add(new Vector3(x0, topY, z0));    norms.Add(n0);
                verts.Add(new Vector3(x1, topY, z1));    norms.Add(n1);
                verts.Add(new Vector3(x1, bottomY, z1)); norms.Add(n1);
                tris.AddRange(new[] { vi, vi + 1, vi + 2, vi, vi + 2, vi + 3 });
                tris.AddRange(new[] { vi, vi + 2, vi + 1, vi, vi + 3, vi + 2 });
            }
        }

        /// <summary>Sphere octant patch at a convex top corner (where top bevel meets vertical bevel).</summary>
        private static void AddBevelCorner(List<Vector3> verts, List<Vector3> norms, List<int> tris,
            float centerX, float centerY, float centerZ, float bR, int segs, float xSign, float zSign)
        {
            int gridSize = segs + 1;
            int baseVert = verts.Count;

            for (int ti = 0; ti <= segs; ti++)
            {
                float theta = Mathf.PI * 0.5f * ti / segs;
                for (int pi = 0; pi <= segs; pi++)
                {
                    float phi = Mathf.PI * 0.5f * pi / segs;

                    float nx = xSign * Mathf.Sin(theta) * Mathf.Cos(phi);
                    float ny = Mathf.Cos(theta);
                    float nz = zSign * Mathf.Sin(theta) * Mathf.Sin(phi);

                    Vector3 normal = new Vector3(nx, ny, nz).normalized;
                    Vector3 pos = new Vector3(centerX + bR * nx, centerY + bR * ny, centerZ + bR * nz);

                    verts.Add(pos);
                    norms.Add(normal);
                }
            }

            for (int ti = 0; ti < segs; ti++)
            {
                for (int pi = 0; pi < segs; pi++)
                {
                    int v00 = baseVert + ti * gridSize + pi;
                    int v10 = baseVert + (ti + 1) * gridSize + pi;
                    int v11 = baseVert + (ti + 1) * gridSize + pi + 1;
                    int v01 = baseVert + ti * gridSize + pi + 1;
                    tris.AddRange(new[] { v00, v01, v11, v00, v11, v10 });
                    tris.AddRange(new[] { v00, v11, v01, v00, v10, v11 });
                }
            }
        }

        /// <summary>Concave fillet patch at an inner corner where two bevels from adjacent cells meet.</summary>
        private static void AddConcaveBevelCorner(List<Vector3> verts, List<Vector3> norms, List<int> tris,
            float cornerX, float topY, float cornerZ, float bR, int segs, float xDir, float zDir)
        {
            // Surface parameterized by theta (arc angle, top→side) and phi (around corner, one bevel→other).
            // At theta=0: traces the top-face edge arc between the two adjacent bevels.
            // At theta=pi/2: collapses to the single corner point where side faces meet.
            int gridSize = segs + 1;
            int baseVert = verts.Count;
            float centerY = topY - bR;

            for (int ti = 0; ti <= segs; ti++)
            {
                float theta = Mathf.PI * 0.5f * ti / segs;
                float sinT = Mathf.Sin(theta);
                float cosT = Mathf.Cos(theta);
                float factor = 1f - sinT;

                for (int pi = 0; pi <= segs; pi++)
                {
                    float phi = Mathf.PI * 0.5f * pi / segs;
                    float sinP = Mathf.Sin(phi);
                    float cosP = Mathf.Cos(phi);

                    float px = cornerX + xDir * bR * sinP * factor;
                    float py = centerY + bR * cosT;
                    float pz = cornerZ + zDir * bR * cosP * factor;

                    Vector3 normal = new Vector3(-xDir * sinT * sinP, cosT, -zDir * sinT * cosP);
                    if (normal.sqrMagnitude > 0.001f) normal.Normalize();
                    else normal = Vector3.up;

                    verts.Add(new Vector3(px, py, pz));
                    norms.Add(normal);
                }
            }

            for (int ti = 0; ti < segs; ti++)
            {
                for (int pi = 0; pi < segs; pi++)
                {
                    int v00 = baseVert + ti * gridSize + pi;
                    int v10 = baseVert + (ti + 1) * gridSize + pi;
                    int v11 = baseVert + (ti + 1) * gridSize + pi + 1;
                    int v01 = baseVert + ti * gridSize + pi + 1;
                    tris.AddRange(new[] { v00, v01, v11, v00, v11, v10 });
                    tris.AddRange(new[] { v00, v11, v01, v00, v10, v11 });
                }
            }
        }

        private void CreateBucketVisuals()
        {
            // Clear existing
            for (int i = bucketsContainer.childCount - 1; i >= 0; i--)
                Destroy(bucketsContainer.GetChild(i).gameObject);

            foreach (var bucket in allBuckets)
            {
                CreateBucket3D(bucket);
            }
        }

        private void CreateBucket3D(BucketData bucket)
        {
            // Root empty GO
            GameObject root = new GameObject($"Bucket_{bucket.id}");
            root.transform.SetParent(bucketsContainer, false);
            bucket.transform3D = root.transform;

            // Body — use custom mesh if available, otherwise fall back to primitive
            GameObject body;
            if (bucketMesh != null)
            {
                body = new GameObject("Body");
                body.AddComponent<MeshFilter>().sharedMesh = bucketMesh;
                body.AddComponent<MeshRenderer>();
                // Add mesh collider for accurate raycast click detection
                MeshCollider mc = body.AddComponent<MeshCollider>();
                mc.sharedMesh = bucketMesh;
            }
            else
            {
                body = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            }
            body.name = "Body";
            body.transform.SetParent(root.transform, false);
            body.transform.localScale = new Vector3(0.306f, 0.306f, 0.306f);
            body.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
            body.transform.localPosition = Vector3.zero;

            bucket.bodyRenderer = body.GetComponent<MeshRenderer>();
            Color baseColor = SandSimulator.PaletteColors[bucket.trueColorId];
            Material baseMat = CreateLitMaterial(baseColor);
            Material darkerMat = CreateLitMaterial(baseColor * 0.7f);
            darkerMat.SetColor("_BaseColor", new Color(baseColor.r * 0.7f, baseColor.g * 0.7f, baseColor.b * 0.7f, baseColor.a));
            bucket.bodyRenderer.materials = new Material[] { baseMat, baseMat, darkerMat };

            // Keep the collider for raycast; store in map
            bucket.bodyCollider = body.GetComponent<Collider>();
            colliderMap[bucket.bodyCollider] = bucket;

            // Mystery "?" text
            GameObject mysteryGo = new GameObject("MysteryText");
            mysteryGo.transform.SetParent(root.transform, false);
            mysteryGo.transform.localPosition = new Vector3(0f, 0.17f, 0f);
            mysteryGo.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            mysteryGo.transform.localScale = new Vector3(0.037244f, 0.037244f, 0.037244f);

            TextMesh mysteryTm = mysteryGo.AddComponent<TextMesh>();
            mysteryTm.text = "?";
            mysteryTm.fontSize = 64;
            mysteryTm.fontStyle = FontStyle.Bold;
            mysteryTm.color = Color.white;
            mysteryTm.anchor = TextAnchor.MiddleCenter;
            mysteryTm.alignment = TextAlignment.Center;
            bucket.mysteryText3D = mysteryTm;

            PositionBucketOnGrid(bucket);
        }

        private void PositionBucketOnGrid(BucketData bucket)
        {
            if (bucket.transform3D == null) return;

            int cols = activeLevelData != null ? activeLevelData.bucketColumns : 5;
            int rows = activeLevelData != null ? activeLevelData.bucketRows : 5;
            float centerCol = (cols - 1) / 2f;
            float worldX = (bucket.col - centerCol) * (cellWidth + cellSpacingX);
            float worldZ = gridOriginZ + ((rows - 1) - bucket.row) * (cellHeight + cellSpacingY);
            float worldY = 0.12f;
            bucket.transform3D.localPosition = new Vector3(worldX, worldY, worldZ);
        }

        public bool TryGetBucketFromCollider(Collider col, out BucketData bucket)
        {
            return colliderMap.TryGetValue(col, out bucket);
        }

        /// <summary>
        /// A bucket is clickable if there is a clear path toward the belt (decreasing row)
        /// through empty valid cells to the grid edge facing the conveyor belt.
        /// BFS moves forward (row-1), left, and right through unoccupied cells.
        /// </summary>
        public bool CheckIsClickable(BucketData pot)
        {
            if (pot.status != BucketStatus.Grid || pot.isHidden) return false;

            HashSet<string> occupied = new HashSet<string>();
            foreach (var p in allBuckets)
            {
                if (p.status == BucketStatus.Grid && p.id != pot.id)
                    occupied.Add($"{p.col},{p.row}");
            }

            // BFS: can we reach the front edge (toward belt)?
            // Row 0 is nearest belt. Forward = row-1. Exit when row-1 is not a valid cell.
            Queue<Vector2Int> queue = new Queue<Vector2Int>();
            HashSet<string> visited = new HashSet<string>();
            string startKey = $"{pot.col},{pot.row}";

            queue.Enqueue(new Vector2Int(pot.col, pot.row));
            visited.Add(startKey);

            while (queue.Count > 0)
            {
                Vector2Int curr = queue.Dequeue();

                // Exit condition: the cell one step toward belt (row-1) is not a valid grid cell
                string forwardKey = $"{curr.x},{curr.y - 1}";
                if (!ActiveValidCells.Contains(forwardKey)) return true;

                // Neighbors: forward (row-1), left, right
                Vector2Int[] neighbors = new Vector2Int[]
                {
                    new Vector2Int(curr.x, curr.y - 1),
                    new Vector2Int(curr.x - 1, curr.y),
                    new Vector2Int(curr.x + 1, curr.y),
                };

                foreach (var n in neighbors)
                {
                    string nKey = $"{n.x},{n.y}";
                    if (!ActiveValidCells.Contains(nKey)) continue;
                    if (occupied.Contains(nKey)) continue;
                    if (visited.Contains(nKey)) continue;

                    visited.Add(nKey);
                    queue.Enqueue(n);
                }
            }

            return false;
        }

        public void MoveBucketToBelt(BucketData bucket)
        {
            bucket.status = BucketStatus.Belt;
            bucket.isHidden = false;

            // Remove collider from map
            if (bucket.bodyCollider != null)
                colliderMap.Remove(bucket.bodyCollider);

            // Reveal hidden neighbors
            foreach (var other in allBuckets)
            {
                if (other.status != BucketStatus.Grid || !other.isHidden) continue;
                bool isNeighbor =
                    (Mathf.Abs(other.col - bucket.col) == 1 && other.row == bucket.row) ||
                    (Mathf.Abs(other.row - bucket.row) == 1 && other.col == bucket.col);
                if (isNeighbor) other.isHidden = false;
            }

            // Grid 3D stays visible for fly-to-belt animation; GameManager destroys it on landing

            UpdateAllBucketVisuals();
        }

        public void UpdateAllBucketVisuals()
        {
            foreach (var bucket in allBuckets)
            {
                if (bucket.status != BucketStatus.Grid) continue;
                if (bucket.transform3D == null) continue;

                PositionBucketOnGrid(bucket);

                bool isClickable = CheckIsClickable(bucket);
                bool isMagicCandidate = magicWandActive && !isClickable;

                // Color
                Color bodyColor;
                if (bucket.isHidden)
                {
                    bodyColor = new Color(0.2f, 0.2f, 0.2f, 1f); // dark gray for mystery
                }
                else
                {
                    bodyColor = SandSimulator.PaletteColors[bucket.trueColorId];
                }

                // Detect transition to naturally clickable — trigger jump + flip
                if (isClickable && !bucket.prevClickable)
                {
                    bucket.jumpVy = 3.5f;
                    bucket.jumpY = 0f;
                }
                bucket.prevClickable = isClickable;

                // Color assignment (magic candidates get full color — flash is in UpdateGridAnimations)
                if (bucket.bodyRenderer != null)
                {
                    Color displayColor;
                    if (!isClickable && !isMagicCandidate)
                    {
                        // Non-clickable: desaturate toward gray
                        float gray = (bodyColor.r + bodyColor.g + bodyColor.b) / 3f;
                        displayColor = Color.Lerp(bodyColor, new Color(gray, gray, gray, 1f), 0.5f);
                    }
                    else
                    {
                        // Clickable or magic candidate: full color
                        displayColor = bodyColor;
                    }

                    Material[] mats = bucket.bodyRenderer.materials;
                    Color darkerColor = new Color(displayColor.r * 0.7f, displayColor.g * 0.7f, displayColor.b * 0.7f, 1f);
                    for (int m = 0; m < mats.Length; m++)
                    {
                        mats[m].SetColor("_BaseColor", m < 2 ? displayColor : darkerColor);
                    }
                    bucket.bodyRenderer.materials = mats;
                }

                // Mystery text
                if (bucket.mysteryText3D != null)
                    bucket.mysteryText3D.gameObject.SetActive(bucket.isHidden);

                // Outline: only for naturally clickable (not magic candidates)
                if (bucket.bodyRenderer != null)
                {
                    if (isClickable && !bucket.isHidden)
                        SetOutline(bucket.bodyRenderer, true, null);
                    else
                        SetOutline(bucket.bodyRenderer, false, null);
                }
            }
        }

        /// <summary>
        /// Per-frame animation update for grid buckets: jump, land squeeze, flip, magic flash.
        /// Called from GameManager's Update().
        /// </summary>
        public void UpdateGridAnimations()
        {
            float dt = Time.deltaTime;

            // --- Shuffle animation: all buckets fly simultaneously ---
            if (shuffleAnimActive)
            {
                shuffleAnimT += dt / SHUFFLE_ANIM_DURATION;

                if (shuffleAnimT >= 1f)
                {
                    // Snap all to final positions
                    foreach (var kvp in shuffleToPos)
                    {
                        if (kvp.Key.transform3D != null)
                            kvp.Key.transform3D.localPosition = kvp.Value;
                    }
                    shuffleAnimActive = false;
                    shuffleFromPos.Clear();
                    shuffleToPos.Clear();
                    UpdateAllBucketVisuals();
                }
                else
                {
                    float t = Mathf.SmoothStep(0f, 1f, shuffleAnimT);
                    float arc = Mathf.Sin(t * Mathf.PI) * 0.2f;

                    foreach (var kvp in shuffleFromPos)
                    {
                        BucketData b = kvp.Key;
                        if (b.transform3D == null) continue;
                        if (!shuffleToPos.TryGetValue(b, out Vector3 to)) continue;

                        Vector3 pos = Vector3.Lerp(kvp.Value, to, t);
                        pos.y += arc;
                        b.transform3D.localPosition = pos;
                    }
                }

                return; // Skip normal grid animations during shuffle
            }

            foreach (var bucket in allBuckets)
            {
                if (bucket.status != BucketStatus.Grid) continue;
                if (bucket.transform3D == null) continue;

                bool isClickable = CheckIsClickable(bucket);
                bool isMagicCandidate = magicWandActive && !isClickable;

                // --- Jump animation ---
                if (bucket.jumpY > 0f || bucket.jumpVy > 0f)
                {
                    bucket.jumpVy -= 12f * dt; // gravity
                    bucket.jumpY += bucket.jumpVy * dt;

                    if (bucket.jumpY <= 0f)
                    {
                        // Landed — trigger squeeze
                        bucket.jumpY = 0f;
                        bucket.jumpVy = 0f;
                        bucket.squeezeTime = 0.3f;
                    }
                }

                // Apply Y offset from jump
                Vector3 pos = bucket.transform3D.localPosition;
                float baseY = 0.12f;
                pos.y = baseY + bucket.jumpY;
                bucket.transform3D.localPosition = pos;

                // --- Squeeze animation (on landing) ---
                Vector3 scale = Vector3.one;
                if (bucket.squeezeTime > 0f)
                {
                    bucket.squeezeTime -= dt;
                    float t = Mathf.Clamp01(bucket.squeezeTime / 0.3f);
                    float squash = Mathf.Sin(t * Mathf.PI * 2.5f) * t * 0.25f;
                    scale = new Vector3(1f + squash, 1f - squash * 0.6f, 1f + squash);
                }

                // --- Magic hover highlight: scale up the aimed bucket ---
                if (isMagicCandidate && bucket == hoveredMagicBucket)
                {
                    float pulse = 1.12f + Mathf.Sin(Time.time * 6f) * 0.03f;
                    scale *= pulse;
                }

                bucket.transform3D.localScale = scale;

                // --- Smooth body flip rotation ---
                // Clickable and magic candidates face up; others face down
                if (bucket.bodyRenderer != null)
                {
                    float targetXRot = (isClickable || isMagicCandidate) ? -90f : 90f;
                    Quaternion target = Quaternion.Euler(targetXRot, 0f, 0f);
                    bucket.bodyRenderer.transform.localRotation = Quaternion.Lerp(
                        bucket.bodyRenderer.transform.localRotation, target, dt * 8f);
                }

                // --- Magic wand candidate flash ---
                if (isMagicCandidate && bucket.bodyRenderer != null)
                {
                    // Pulsing brightness; hovered bucket glows stronger
                    float flash = (Mathf.Sin(Time.time * 5.95f) + 1f) * 0.5f; // 0..1 pulse
                    bool isHovered = bucket == hoveredMagicBucket;
                    float blendStrength = isHovered ? 0.6f : 0.35f;
                    Color baseColor = bucket.isHidden
                        ? new Color(0.2f, 0.2f, 0.2f, 1f)
                        : SandSimulator.PaletteColors[bucket.trueColorId];
                    Color brightColor = Color.Lerp(baseColor, Color.white, flash * blendStrength);
                    Color brightDarker = new Color(brightColor.r * 0.7f, brightColor.g * 0.7f, brightColor.b * 0.7f, 1f);

                    Material[] mats = bucket.bodyRenderer.materials;
                    for (int m = 0; m < mats.Length; m++)
                    {
                        mats[m].SetColor("_BaseColor", m < 2 ? brightColor : brightDarker);
                    }
                    bucket.bodyRenderer.materials = mats;
                }
            }
        }

        public void SetMagicWandActive(bool active)
        {
            magicWandActive = active;
            if (!active) hoveredMagicBucket = null;
            UpdateAllBucketVisuals();
        }

        public bool IsMagicWandActive()
        {
            return magicWandActive;
        }

        /// <summary>
        /// Per-frame hover detection for magic wand mode.
        /// Highlights the unclickable bucket the player is aiming at.
        /// </summary>
        public void UpdateMagicHover(Ray ray)
        {
            if (!magicWandActive)
            {
                hoveredMagicBucket = null;
                return;
            }

            BucketData newHover = null;
            if (Physics.Raycast(ray, out RaycastHit hit))
            {
                if (TryGetBucketFromCollider(hit.collider, out BucketData bucket))
                {
                    if (bucket.status == BucketStatus.Grid && !CheckIsClickable(bucket))
                        newHover = bucket;
                }
            }
            hoveredMagicBucket = newHover;
        }

        public void ShuffleBuckets()
        {
            List<BucketData> gridBuckets = allBuckets.FindAll(b => b.status == BucketStatus.Grid);
            if (gridBuckets.Count < 2) return;

            // Record starting positions for each bucket
            shuffleFromPos.Clear();
            shuffleToPos.Clear();
            foreach (var b in gridBuckets)
            {
                if (b.transform3D != null)
                    shuffleFromPos[b] = b.transform3D.localPosition;
            }

            // Fisher-Yates shuffle on positions
            List<Vector2Int> positions = new List<Vector2Int>();
            foreach (var b in gridBuckets)
                positions.Add(new Vector2Int(b.col, b.row));

            for (int i = positions.Count - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                (positions[i], positions[j]) = (positions[j], positions[i]);
            }

            // Assign new col/row immediately, then compute target world positions
            for (int i = 0; i < gridBuckets.Count; i++)
            {
                gridBuckets[i].col = positions[i].x;
                gridBuckets[i].row = positions[i].y;
            }

            int cols = activeLevelData != null ? activeLevelData.bucketColumns : 5;
            int rows = activeLevelData != null ? activeLevelData.bucketRows : 5;
            float centerCol = (cols - 1) / 2f;
            foreach (var b in gridBuckets)
            {
                float worldX = (b.col - centerCol) * (cellWidth + cellSpacingX);
                float worldZ = gridOriginZ + ((rows - 1) - b.row) * (cellHeight + cellSpacingY);
                shuffleToPos[b] = new Vector3(worldX, 0.12f, worldZ);
            }

            // Start simultaneous fly animation
            shuffleAnimActive = true;
            shuffleAnimT = 0f;
        }

        public bool IsShuffling() => shuffleAnimActive;

        public bool HasGridBuckets()
        {
            foreach (var b in allBuckets)
                if (b.status == BucketStatus.Grid) return true;
            return false;
        }

        private int ColorStrToId(string colorStr)
        {
            switch (colorStr)
            {
                case "BL": return SandSimulator.BLUE;
                case "WH": return SandSimulator.WHITE;
                case "RE": return SandSimulator.RED;
                case "OR": return SandSimulator.ORANGE;
                default: return SandSimulator.BLUE;
            }
        }

        private string ColorIdToStr(int colorId)
        {
            switch (colorId)
            {
                case 1: return "BL";
                case 2: return "WH";
                case 3: return "RE";
                case 4: return "OR";
                default: return $"C{colorId}";
            }
        }

        /// <summary>
        /// Toggles outline on a bucket by enabling/disabling a dedicated outline child object.
        /// The outline child uses the same mesh but with the outline material on ALL sub-mesh slots,
        /// so every part of the mesh gets outlined (not just the last sub-mesh).
        /// Pass overrideMat to use a different material (e.g. cyan for magic candidates).
        /// </summary>
        private void SetOutline(MeshRenderer renderer, bool show, Material overrideMat = null)
        {
            if (outlineMat == null) return;
            Material mat = overrideMat ?? outlineMat;

            Transform bodyTransform = renderer.transform;
            Transform outlineChild = bodyTransform.Find("Outline");

            if (show && outlineChild == null)
            {
                // Create outline child: same mesh, all slots use outline material
                var mesh = renderer.GetComponent<MeshFilter>().sharedMesh;
                if (mesh == null) return;

                GameObject outlineGo = new GameObject("Outline");
                outlineGo.transform.SetParent(bodyTransform, false);
                outlineGo.transform.localPosition = Vector3.zero;
                outlineGo.transform.localRotation = Quaternion.identity;
                outlineGo.transform.localScale = Vector3.one;

                outlineGo.AddComponent<MeshFilter>().sharedMesh = mesh;
                MeshRenderer outlineRenderer = outlineGo.AddComponent<MeshRenderer>();

                Material[] outlineMats = new Material[mesh.subMeshCount];
                for (int i = 0; i < mesh.subMeshCount; i++)
                    outlineMats[i] = mat;
                outlineRenderer.sharedMaterials = outlineMats;
            }
            else if (!show && outlineChild != null)
            {
                Destroy(outlineChild.gameObject);
            }
            else if (show && outlineChild != null)
            {
                outlineChild.gameObject.SetActive(true);
                // Update material if it changed (e.g. switching between normal and magic outline)
                MeshRenderer outlineRenderer = outlineChild.GetComponent<MeshRenderer>();
                if (outlineRenderer != null && outlineRenderer.sharedMaterial != mat)
                {
                    Material[] outlineMats = new Material[outlineRenderer.sharedMaterials.Length];
                    for (int i = 0; i < outlineMats.Length; i++)
                        outlineMats[i] = mat;
                    outlineRenderer.sharedMaterials = outlineMats;
                }
            }
        }

        private static Material CreateLitMaterial(Color color)
        {
            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            mat.SetColor("_BaseColor", color);
            return mat;
        }

        private static void SetMaterialTransparent(Material mat)
        {
            mat.SetFloat("_Surface", 1f);
            mat.SetFloat("_Blend", 0f);
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite", 0);
            mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.renderQueue = 3000;
        }

        /// <summary>
        /// Sets alpha on the body material. The custom shader already blends via
        /// Blend SrcAlpha OneMinusSrcAlpha, so we just update _BaseColor.a.
        /// For URP/Lit fallback materials we also flip the surface type keywords.
        /// </summary>
        private static void SetMaterialAlpha(MeshRenderer renderer, float alpha)
        {
            if (renderer == null) return;
            Material mat = renderer.material;
            Color c = mat.GetColor("_BaseColor");
            c.a = alpha;
            mat.SetColor("_BaseColor", c);

            // URP/Lit fallback needs explicit surface type toggling
            if (mat.shader.name == "Universal Render Pipeline/Lit")
            {
                if (alpha < 1f) SetMaterialTransparent(mat);
                else
                {
                    mat.SetFloat("_Surface", 0f);
                    mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
                    mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.Zero);
                    mat.SetInt("_ZWrite", 1);
                    mat.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
                    mat.renderQueue = -1;
                }
            }
        }
    }
}
