using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
namespace SandFlowPuzzle
{
public class LevelEditorWindow : EditorWindow
{
    private List<LevelData> levels = new List<LevelData>();
    private int selectedIndex = -1;
    private Vector2 leftScroll;
    private Vector2 rightScroll;

    // Editor-only state
    private Texture2D sourceTexture;
    private Texture2D previewTexture;
    private bool dirty;

    // Bucket selection
    private int selectedBucketIndex = -1;

    // Grid editing mode
    private bool editCellsMode;

    // Cached textures for bucket tile rendering
    private Texture2D bucketTileTexture;
    private Texture2D mysteryTileTexture;
    private Texture2D emptyTileTexture;
    private Texture2D selectedOverlayTexture;

    [MenuItem("Tools/#16 Sand Flow Puzzle/Level Editor")]
    public static void ShowWindow()
    {
        var window = GetWindow<LevelEditorWindow>("Level Editor");
        window.minSize = new Vector2(700, 500);
    }

    private void OnEnable()
    {
        LoadFromDisk();
        CreateTileTextures();
    }

    private void OnDisable()
    {
        CleanupPreview();
        CleanupTileTextures();
    }

    private void LoadFromDisk()
    {
        LevelManager.LoadAllLevelsFromDisk();
        levels = LevelManager.GetAllLevels();
        if (levels.Count > 0 && selectedIndex < 0)
            selectedIndex = 0;
        if (selectedIndex >= levels.Count)
            selectedIndex = levels.Count - 1;
        selectedBucketIndex = -1;
        LoadSourceTextureForSelected();
        RegeneratePreview();
    }

    private void OnGUI()
    {
        EditorGUILayout.BeginHorizontal();
        DrawLeftPanel();
        DrawRightPanel();
        EditorGUILayout.EndHorizontal();
    }

    // =====================================================================
    // LEFT PANEL
    // =====================================================================

    private void DrawLeftPanel()
    {
        EditorGUILayout.BeginVertical(GUILayout.Width(200));

        EditorGUILayout.LabelField("Levels", EditorStyles.boldLabel);
        EditorGUILayout.LabelField($"{levels.Count} level(s)");
        EditorGUILayout.Space(4);

        leftScroll = EditorGUILayout.BeginScrollView(leftScroll, GUILayout.ExpandHeight(true));

        for (int i = 0; i < levels.Count; i++)
        {
            bool isSelected = (i == selectedIndex);
            GUIStyle style = isSelected ? "selectionRect" : EditorStyles.miniButton;

            string label = $"{i + 1}. {levels[i].levelName}";
            if (GUILayout.Button(label, style))
            {
                if (selectedIndex != i)
                {
                    selectedIndex = i;
                    selectedBucketIndex = -1;
                    LoadSourceTextureForSelected();
                    RegeneratePreview();
                }
            }
        }

        EditorGUILayout.EndScrollView();

        EditorGUILayout.Space(4);

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("+ Add"))
        {
            var newLevel = new LevelData();
            newLevel.levelName = $"Level {levels.Count + 1}";
            InitializeDefaultGrid(newLevel);
            levels.Add(newLevel);
            selectedIndex = levels.Count - 1;
            selectedBucketIndex = -1;
            dirty = true;
        }
        if (GUILayout.Button("Duplicate") && selectedIndex >= 0)
        {
            string json = JsonUtility.ToJson(levels[selectedIndex]);
            LevelData copy = JsonUtility.FromJson<LevelData>(json);
            copy.levelName += " (Copy)";
            levels.Insert(selectedIndex + 1, copy);
            selectedIndex++;
            selectedBucketIndex = -1;
            dirty = true;
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Delete") && selectedIndex >= 0 && levels.Count > 1)
        {
            if (EditorUtility.DisplayDialog("Delete Level",
                $"Delete '{levels[selectedIndex].levelName}'?", "Delete", "Cancel"))
            {
                levels.RemoveAt(selectedIndex);
                if (selectedIndex >= levels.Count) selectedIndex = levels.Count - 1;
                selectedBucketIndex = -1;
                dirty = true;
                LoadSourceTextureForSelected();
                RegeneratePreview();
            }
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();
        GUI.enabled = selectedIndex > 0;
        if (GUILayout.Button("Move Up"))
        {
            (levels[selectedIndex], levels[selectedIndex - 1]) = (levels[selectedIndex - 1], levels[selectedIndex]);
            selectedIndex--;
            dirty = true;
        }
        GUI.enabled = selectedIndex < levels.Count - 1 && selectedIndex >= 0;
        if (GUILayout.Button("Move Down"))
        {
            (levels[selectedIndex], levels[selectedIndex + 1]) = (levels[selectedIndex + 1], levels[selectedIndex]);
            selectedIndex++;
            dirty = true;
        }
        GUI.enabled = true;
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(8);

        GUI.backgroundColor = new Color(0.3f, 0.85f, 0.3f);
        if (GUILayout.Button("Save All", GUILayout.Height(30)))
        {
            LevelManager.SaveAllLevels();
            LevelManager.ForceReload();
            dirty = false;
            Debug.Log($"[LevelEditor] Saved {levels.Count} level(s)");
        }
        GUI.backgroundColor = Color.white;

        if (GUILayout.Button("Reload from Disk"))
        {
            if (!dirty || EditorUtility.DisplayDialog("Reload",
                "Discard unsaved changes?", "Reload", "Cancel"))
            {
                LoadFromDisk();
                dirty = false;
            }
        }

        EditorGUILayout.EndVertical();
    }

    // =====================================================================
    // RIGHT PANEL
    // =====================================================================

    private void DrawRightPanel()
    {
        EditorGUILayout.BeginVertical();

        if (selectedIndex < 0 || selectedIndex >= levels.Count)
        {
            EditorGUILayout.LabelField("No level selected");
            EditorGUILayout.EndVertical();
            return;
        }

        LevelData level = levels[selectedIndex];

        rightScroll = EditorGUILayout.BeginScrollView(rightScroll);

        EditorGUILayout.LabelField($"Editing: Level_{selectedIndex + 1}.json", EditorStyles.boldLabel);
        EditorGUILayout.Space(4);

        EditorGUI.BeginChangeCheck();
        level.levelName = EditorGUILayout.TextField("Level Name", level.levelName);
        if (EditorGUI.EndChangeCheck()) dirty = true;

        EditorGUILayout.Space(8);
        DrawImageSection(level);

        EditorGUILayout.Space(8);
        DrawBucketGridSection(level);

        EditorGUILayout.Space(8);
        DrawBeltBoosterSection(level);

        EditorGUILayout.EndScrollView();
        EditorGUILayout.EndVertical();
    }

    // =====================================================================
    // IMAGE SECTION
    // =====================================================================

    private void DrawImageSection(LevelData level)
    {
        EditorGUILayout.LabelField("Image Source", EditorStyles.boldLabel);

        EditorGUI.BeginChangeCheck();
        sourceTexture = (Texture2D)EditorGUILayout.ObjectField("Source Image", sourceTexture, typeof(Texture2D), false);
        if (EditorGUI.EndChangeCheck())
        {
            if (sourceTexture != null)
                level.sourceImagePath = AssetDatabase.GetAssetPath(sourceTexture);
            else
                level.sourceImagePath = "";
            dirty = true;
        }

        EditorGUI.BeginChangeCheck();
        level.desiredColorCount = EditorGUILayout.IntSlider("Color Count", level.desiredColorCount, 2, 10);
        if (EditorGUI.EndChangeCheck()) dirty = true;

        if (GUILayout.Button("Generate / Re-roll Colors") && sourceTexture != null)
        {
            level.quantizationSeed = Random.Range(0, 999999);
            QuantizeImage(level);
            RegeneratePreview();
            dirty = true;
        }

        if (previewTexture != null)
        {
            EditorGUILayout.Space(4);
            float previewSize = 200f;
            Rect r = GUILayoutUtility.GetRect(previewSize, previewSize, GUILayout.ExpandWidth(false));
            EditorGUI.DrawPreviewTexture(r, previewTexture, null, ScaleMode.ScaleToFit);
        }

        if (level.palette != null && level.palette.Count > 0)
        {
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Palette:");
            EditorGUILayout.BeginHorizontal();
            for (int i = 0; i < level.palette.Count; i++)
            {
                SerializableColor sc = level.palette[i];
                Color c = new Color(sc.r, sc.g, sc.b, 1f);
                EditorGUI.BeginChangeCheck();
                c = EditorGUILayout.ColorField(GUIContent.none, c, false, false, false, GUILayout.Width(30), GUILayout.Height(20));
                if (EditorGUI.EndChangeCheck())
                {
                    level.palette[i] = new SerializableColor(c.r, c.g, c.b);
                    RegeneratePreview();
                    dirty = true;
                }
            }
            EditorGUILayout.EndHorizontal();
        }
    }

    // =====================================================================
    // BUCKET GRID SECTION
    // =====================================================================

    private void DrawBucketGridSection(LevelData level)
    {
        EditorGUILayout.LabelField("Bucket Grid", EditorStyles.boldLabel);

        EditorGUI.BeginChangeCheck();
        level.bucketRows = EditorGUILayout.IntSlider("Rows", level.bucketRows, 1, 10);
        level.bucketColumns = EditorGUILayout.IntSlider("Columns", level.bucketColumns, 1, 10);
        level.bucketSpacingX = EditorGUILayout.Slider("Spacing X", level.bucketSpacingX, 0f, 0.5f);
        level.bucketSpacingY = EditorGUILayout.Slider("Spacing Y", level.bucketSpacingY, 0f, 0.5f);
        if (EditorGUI.EndChangeCheck())
        {
            EnsureGridCellList(level);
            selectedBucketIndex = -1;
            dirty = true;
        }

        EnsureGridCellList(level);

        EditorGUILayout.Space(6);

        // --- Mode toolbar ---
        EditorGUILayout.BeginHorizontal();
        Color prevBg = GUI.backgroundColor;

        GUI.backgroundColor = editCellsMode ? Color.white : new Color(0.6f, 0.85f, 1f);
        if (GUILayout.Button("Select Buckets", EditorStyles.miniButtonLeft))
        {
            editCellsMode = false;
        }
        GUI.backgroundColor = editCellsMode ? new Color(1f, 0.75f, 0.35f) : Color.white;
        if (GUILayout.Button("Edit Cells", EditorStyles.miniButtonRight))
        {
            editCellsMode = true;
            selectedBucketIndex = -1;
        }
        GUI.backgroundColor = prevBg;
        EditorGUILayout.EndHorizontal();

        if (editCellsMode)
            EditorGUILayout.HelpBox("Click tiles to enable/disable grid cells.", MessageType.Info);

        EditorGUILayout.Space(4);

        // --- Visual bucket grid ---
        DrawVisualBucketGrid(level);

        EditorGUILayout.Space(4);

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Auto-distribute"))
        {
            AutoDistributeBuckets(level);
            selectedBucketIndex = -1;
            dirty = true;
        }
        if (GUILayout.Button("Randomize Colors"))
        {
            RandomizeBucketPositions(level);
            dirty = true;
        }
        EditorGUILayout.EndHorizontal();

        // --- Selected bucket inspector ---
        if (!editCellsMode && selectedBucketIndex >= 0 && level.buckets != null && selectedBucketIndex < level.buckets.Count)
        {
            EditorGUILayout.Space(8);
            DrawSelectedBucketInspector(level);
        }
    }

    private void DrawVisualBucketGrid(LevelData level)
    {
        const float tileSize = 38f;

        // Build a lookup: (row,col) -> bucket index
        Dictionary<string, int> bucketLookup = new Dictionary<string, int>();
        if (level.buckets != null)
        {
            for (int i = 0; i < level.buckets.Count; i++)
            {
                string key = $"{level.buckets[i].row},{level.buckets[i].col}";
                bucketLookup[key] = i;
            }
        }

        for (int r = 0; r < level.bucketRows; r++)
        {
            EditorGUILayout.BeginHorizontal();
            for (int c = 0; c < level.bucketColumns; c++)
            {
                int cellIdx = r * level.bucketColumns + c;
                bool cellEnabled = level.gridCellEnabled[cellIdx];
                string key = $"{r},{c}";
                bool hasBucket = bucketLookup.TryGetValue(key, out int bucketIdx);

                Rect tileRect = GUILayoutUtility.GetRect(tileSize, tileSize, GUILayout.Width(tileSize), GUILayout.Height(tileSize));

                // --- Edit Cells mode: all clicks toggle enable/disable ---
                if (editCellsMode)
                {
                    if (cellEnabled)
                    {
                        EditorGUI.DrawRect(tileRect, new Color(0.35f, 0.55f, 0.35f, 1f));
                        GUI.Label(tileRect, "ON", GetCenteredBoldStyle(Color.white, 10));
                    }
                    else
                    {
                        EditorGUI.DrawRect(tileRect, new Color(0.18f, 0.18f, 0.18f, 1f));
                        GUI.Label(tileRect, "OFF", GetCenteredBoldStyle(new Color(0.5f, 0.5f, 0.5f), 10));
                    }

                    if (GUI.Button(tileRect, GUIContent.none, GUIStyle.none))
                    {
                        level.gridCellEnabled[cellIdx] = !cellEnabled;

                        // Recalculate all buckets on enabled cells to ensure solvability
                        AutoDistributeBuckets(level);
                        selectedBucketIndex = -1;
                        dirty = true;
                    }
                    continue;
                }

                // --- Select Buckets mode ---
                if (!cellEnabled)
                {
                    // Disabled cell — dark
                    EditorGUI.DrawRect(tileRect, new Color(0.15f, 0.15f, 0.15f, 1f));
                }
                else if (hasBucket)
                {
                    BucketDef bucket = level.buckets[bucketIdx];
                    bool isSelected = (bucketIdx == selectedBucketIndex);

                    if (bucket.isMystery)
                    {
                        EditorGUI.DrawRect(tileRect, new Color(0.25f, 0.25f, 0.25f, 1f));
                        DrawBucketShape(tileRect, new Color(0.4f, 0.4f, 0.4f, 1f));
                        GUI.Label(tileRect, "?", GetCenteredBoldStyle(Color.white, 16));
                    }
                    else
                    {
                        Color bucketColor = GetBucketColor(level, bucket.colorId);
                        EditorGUI.DrawRect(tileRect, new Color(0.22f, 0.22f, 0.22f, 1f));
                        DrawBucketShape(tileRect, bucketColor);
                    }

                    if (isSelected)
                        DrawRectBorder(tileRect, new Color(1f, 0.9f, 0.2f, 1f), 2f);

                    if (GUI.Button(tileRect, GUIContent.none, GUIStyle.none))
                    {
                        selectedBucketIndex = (selectedBucketIndex == bucketIdx) ? -1 : bucketIdx;
                        Repaint();
                    }
                }
                else
                {
                    // Enabled cell with no bucket
                    EditorGUI.DrawRect(tileRect, new Color(0.35f, 0.42f, 0.50f, 0.55f));
                }
            }
            EditorGUILayout.EndHorizontal();
        }
    }

    private void DrawBucketShape(Rect tileRect, Color color)
    {
        // Draw a trapezoid-ish bucket shape (wider at top, narrower at bottom)
        float inset = 4f;
        float topInset = inset;
        float bottomInset = inset + 5f;
        float topY = tileRect.y + 4f;
        float bottomY = tileRect.yMax - 4f;
        float height = bottomY - topY;

        // Approximate bucket with 3 stacked rects (top wider, bottom narrower)
        int slices = 8;
        for (int i = 0; i < slices; i++)
        {
            float t = (float)i / slices;
            float tNext = (float)(i + 1) / slices;
            float currentInset = Mathf.Lerp(topInset, bottomInset, t);
            float y = topY + t * height;
            float h = (tNext - t) * height;
            Rect sliceRect = new Rect(tileRect.x + currentInset, y, tileRect.width - currentInset * 2f, h);
            EditorGUI.DrawRect(sliceRect, color);
        }

        // Darker inner top (opening)
        float openingInset = topInset + 3f;
        Rect openingRect = new Rect(tileRect.x + openingInset, topY, tileRect.width - openingInset * 2f, 4f);
        Color darkerColor = new Color(color.r * 0.5f, color.g * 0.5f, color.b * 0.5f, 1f);
        EditorGUI.DrawRect(openingRect, darkerColor);
    }

    private void DrawRectBorder(Rect rect, Color color, float thickness)
    {
        EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, thickness), color); // top
        EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - thickness, rect.width, thickness), color); // bottom
        EditorGUI.DrawRect(new Rect(rect.x, rect.y, thickness, rect.height), color); // left
        EditorGUI.DrawRect(new Rect(rect.xMax - thickness, rect.y, thickness, rect.height), color); // right
    }

    private void DrawSelectedBucketInspector(LevelData level)
    {
        BucketDef bucket = level.buckets[selectedBucketIndex];

        EditorGUILayout.BeginVertical("box");
        EditorGUILayout.LabelField($"Selected Bucket [{bucket.row}, {bucket.col}]", EditorStyles.boldLabel);

        // Mystery toggle
        EditorGUI.BeginChangeCheck();
        bucket.isMystery = EditorGUILayout.Toggle("Mystery Bucket", bucket.isMystery);
        if (EditorGUI.EndChangeCheck()) dirty = true;

        // Color picker (only for non-mystery)
        if (!bucket.isMystery && level.palette != null && level.palette.Count > 0)
        {
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Color:");

            EditorGUILayout.BeginHorizontal();
            for (int i = 0; i < level.palette.Count; i++)
            {
                int colorId = i + 1; // 1-based
                SerializableColor sc = level.palette[i];
                Color c = new Color(sc.r, sc.g, sc.b, 1f);

                bool isCurrentColor = (bucket.colorId == colorId);

                // Draw color swatch button
                Rect swatchRect = GUILayoutUtility.GetRect(28, 28, GUILayout.Width(28), GUILayout.Height(28));
                EditorGUI.DrawRect(swatchRect, c);

                if (isCurrentColor)
                    DrawRectBorder(swatchRect, Color.white, 2f);

                if (GUI.Button(swatchRect, GUIContent.none, GUIStyle.none))
                {
                    bucket.colorId = colorId;
                    dirty = true;
                }
            }
            EditorGUILayout.EndHorizontal();
        }

        EditorGUILayout.EndVertical();
    }

    // =====================================================================
    // BELT & BOOSTERS SECTION
    // =====================================================================

    private void DrawBeltBoosterSection(LevelData level)
    {
        EditorGUILayout.LabelField("Belt & Boosters", EditorStyles.boldLabel);

        EditorGUI.BeginChangeCheck();
        level.maxBeltSlots = EditorGUILayout.IntField("Max Belt Slots", level.maxBeltSlots);

        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("Booster Counts:");
        level.slotBoosterCount = EditorGUILayout.IntField("  Slot Boosters", level.slotBoosterCount);
        level.shuffleBoosterCount = EditorGUILayout.IntField("  Shuffle Boosters", level.shuffleBoosterCount);
        level.magicBoosterCount = EditorGUILayout.IntField("  Magic Boosters", level.magicBoosterCount);

        if (EditorGUI.EndChangeCheck()) dirty = true;
    }

    // =====================================================================
    // HELPERS
    // =====================================================================

    private Color GetBucketColor(LevelData level, int colorId)
    {
        if (level.palette != null && colorId >= 1 && colorId <= level.palette.Count)
        {
            SerializableColor sc = level.palette[colorId - 1];
            return new Color(sc.r, sc.g, sc.b, 1f);
        }
        return new Color(0.5f, 0.5f, 0.5f, 1f);
    }

    private GUIStyle GetCenteredBoldStyle(Color textColor, int fontSize)
    {
        GUIStyle style = new GUIStyle(GUI.skin.label);
        style.alignment = TextAnchor.MiddleCenter;
        style.fontStyle = FontStyle.Bold;
        style.fontSize = fontSize;
        style.normal.textColor = textColor;
        return style;
    }

    private void QuantizeImage(LevelData level)
    {
        if (sourceTexture == null) return;

        var (grid, palette) = ImageQuantizer.Quantize(
            sourceTexture, level.gridSize, level.desiredColorCount, level.quantizationSeed);

        level.sandGrid = new List<byte>(grid);
        level.palette = new List<SerializableColor>();
        foreach (var c in palette)
            level.palette.Add(new SerializableColor(c.r, c.g, c.b));

        AutoDistributeBuckets(level);
    }

    private void RegeneratePreview()
    {
        CleanupPreview();

        if (selectedIndex < 0 || selectedIndex >= levels.Count) return;
        LevelData level = levels[selectedIndex];
        if (level.sandGrid == null || level.sandGrid.Count == 0) return;

        int gs = level.gridSize;
        if (level.sandGrid.Count != gs * gs) return;

        previewTexture = new Texture2D(gs, gs, TextureFormat.RGBA32, false);
        previewTexture.filterMode = FilterMode.Point;
        previewTexture.wrapMode = TextureWrapMode.Clamp;

        Color32[] pixels = new Color32[gs * gs];
        Color32 empty = new Color32(85, 85, 85, 255);

        for (int y = 0; y < gs; y++)
        {
            for (int x = 0; x < gs; x++)
            {
                int srcIdx = y * gs + x;
                int dstIdx = (gs - 1 - y) * gs + x;
                byte colorId = level.sandGrid[srcIdx];

                if (colorId == 0 || level.palette == null || colorId > level.palette.Count)
                {
                    pixels[dstIdx] = empty;
                }
                else
                {
                    SerializableColor sc = level.palette[colorId - 1];
                    pixels[dstIdx] = new Color32(
                        (byte)(sc.r * 255f),
                        (byte)(sc.g * 255f),
                        (byte)(sc.b * 255f), 255);
                }
            }
        }

        previewTexture.SetPixels32(pixels);
        previewTexture.Apply();
    }

    private void CleanupPreview()
    {
        if (previewTexture != null)
        {
            DestroyImmediate(previewTexture);
            previewTexture = null;
        }
    }

    private void CleanupTileTextures()
    {
        if (bucketTileTexture != null) DestroyImmediate(bucketTileTexture);
        if (mysteryTileTexture != null) DestroyImmediate(mysteryTileTexture);
        if (emptyTileTexture != null) DestroyImmediate(emptyTileTexture);
        if (selectedOverlayTexture != null) DestroyImmediate(selectedOverlayTexture);
    }

    private void CreateTileTextures()
    {
        // Simple 1x1 textures for tinting
        bucketTileTexture = MakeSolidTexture(Color.white);
        mysteryTileTexture = MakeSolidTexture(new Color(0.3f, 0.3f, 0.3f));
        emptyTileTexture = MakeSolidTexture(new Color(0.15f, 0.15f, 0.15f));
        selectedOverlayTexture = MakeSolidTexture(new Color(1f, 1f, 0f, 0.3f));
    }

    private Texture2D MakeSolidTexture(Color color)
    {
        Texture2D tex = new Texture2D(1, 1);
        tex.SetPixel(0, 0, color);
        tex.Apply();
        return tex;
    }

    private void LoadSourceTextureForSelected()
    {
        sourceTexture = null;
        if (selectedIndex < 0 || selectedIndex >= levels.Count) return;
        string path = levels[selectedIndex].sourceImagePath;
        if (!string.IsNullOrEmpty(path))
            sourceTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    private void EnsureGridCellList(LevelData level)
    {
        int total = level.bucketRows * level.bucketColumns;
        while (level.gridCellEnabled.Count < total)
            level.gridCellEnabled.Add(true);
        while (level.gridCellEnabled.Count > total)
            level.gridCellEnabled.RemoveAt(level.gridCellEnabled.Count - 1);
    }

    private void InitializeDefaultGrid(LevelData level)
    {
        level.bucketRows = 5;
        level.bucketColumns = 5;
        EnsureGridCellList(level);
    }

    private void AutoDistributeBuckets(LevelData level)
    {
        if (level.palette == null || level.palette.Count == 0) return;

        // Count pixels per color
        Dictionary<int, int> colorCounts = new Dictionary<int, int>();
        for (int c = 1; c <= level.palette.Count; c++)
            colorCounts[c] = 0;

        if (level.sandGrid != null)
        {
            foreach (byte b in level.sandGrid)
            {
                if (b >= 1 && b <= level.palette.Count)
                    colorCounts[b]++;
            }
        }

        // Only consider colors that actually have pixels
        List<int> activeColors = new List<int>();
        int totalPixels = 0;
        foreach (var kvp in colorCounts)
        {
            if (kvp.Value > 0)
            {
                activeColors.Add(kvp.Key);
                totalPixels += kvp.Value;
            }
        }

        if (activeColors.Count == 0) return;

        // Collect enabled cells
        List<(int row, int col)> enabledCells = new List<(int, int)>();
        EnsureGridCellList(level);
        for (int r = 0; r < level.bucketRows; r++)
        {
            for (int c = 0; c < level.bucketColumns; c++)
            {
                int idx = r * level.bucketColumns + c;
                if (level.gridCellEnabled[idx])
                    enabledCells.Add((r, c));
            }
        }

        if (enabledCells.Count == 0) return;

        int totalBuckets = enabledCells.Count;

        // Guarantee at least 1 bucket per active color, then distribute the rest proportionally
        List<int> colorAssignments = new List<int>();
        foreach (int color in activeColors)
            colorAssignments.Add(color);

        int remaining = totalBuckets - colorAssignments.Count;
        if (remaining > 0 && totalPixels > 0)
        {
            // Distribute remaining slots proportionally
            List<(int color, float share)> shares = new List<(int, float)>();
            foreach (int color in activeColors)
                shares.Add((color, (float)colorCounts[color] / totalPixels));

            for (int i = 0; i < remaining; i++)
            {
                // Pick the color that is most under-represented
                int bestColor = activeColors[0];
                float bestDeficit = float.MinValue;
                foreach (var s in shares)
                {
                    int current = 0;
                    foreach (int a in colorAssignments)
                        if (a == s.color) current++;
                    float ideal = s.share * totalBuckets;
                    float deficit = ideal - current;
                    if (deficit > bestDeficit)
                    {
                        bestDeficit = deficit;
                        bestColor = s.color;
                    }
                }
                colorAssignments.Add(bestColor);
            }
        }

        // If fewer cells than colors, trim but keep one per color as much as possible
        while (colorAssignments.Count > totalBuckets)
        {
            // Remove the color with the most assignments (that has more than 1)
            Dictionary<int, int> assignCounts = new Dictionary<int, int>();
            foreach (int a in colorAssignments)
                assignCounts[a] = assignCounts.ContainsKey(a) ? assignCounts[a] + 1 : 1;

            int removeColor = -1;
            int maxCount = 0;
            foreach (var kvp in assignCounts)
            {
                if (kvp.Value > 1 && kvp.Value > maxCount)
                {
                    maxCount = kvp.Value;
                    removeColor = kvp.Key;
                }
            }

            if (removeColor == -1)
            {
                // All colors have exactly 1 — remove the one with fewest pixels
                int fewestPixels = int.MaxValue;
                foreach (var kvp in assignCounts)
                {
                    int px = colorCounts.ContainsKey(kvp.Key) ? colorCounts[kvp.Key] : 0;
                    if (px < fewestPixels)
                    {
                        fewestPixels = px;
                        removeColor = kvp.Key;
                    }
                }
            }

            // Remove last occurrence of removeColor
            for (int i = colorAssignments.Count - 1; i >= 0; i--)
            {
                if (colorAssignments[i] == removeColor)
                {
                    colorAssignments.RemoveAt(i);
                    break;
                }
            }
        }

        // Shuffle
        for (int i = colorAssignments.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (colorAssignments[i], colorAssignments[j]) = (colorAssignments[j], colorAssignments[i]);
        }

        // Create bucket defs
        level.buckets = new List<BucketDef>();
        for (int i = 0; i < enabledCells.Count; i++)
        {
            level.buckets.Add(new BucketDef
            {
                row = enabledCells[i].row,
                col = enabledCells[i].col,
                colorId = colorAssignments[i],
                isMystery = false
            });
        }
    }

    private void RandomizeBucketPositions(LevelData level)
    {
        if (level.buckets == null || level.buckets.Count < 2) return;

        List<int> colors = new List<int>();
        List<bool> mysteries = new List<bool>();
        foreach (var b in level.buckets)
        {
            colors.Add(b.colorId);
            mysteries.Add(b.isMystery);
        }

        for (int i = colors.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (colors[i], colors[j]) = (colors[j], colors[i]);
            (mysteries[i], mysteries[j]) = (mysteries[j], mysteries[i]);
        }

        for (int i = 0; i < level.buckets.Count; i++)
        {
            level.buckets[i].colorId = colors[i];
            level.buckets[i].isMystery = mysteries[i];
        }
    }
}
}
