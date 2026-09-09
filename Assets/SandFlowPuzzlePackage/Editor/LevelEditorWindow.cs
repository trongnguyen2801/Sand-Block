using UnityEngine;
using UnityEditor;
using SandFlowPuzzle.BlockAuthoring;
using System.Collections.Generic;
namespace SandFlowPuzzle
{
public partial class LevelEditorWindow : EditorWindow
{
    private List<LevelData> levels = new List<LevelData>();
    private int selectedIndex = -1;
    private int selectedPictureIndex;
    private Vector2 leftScroll;
    private Vector2 rightScroll;

    // Editor-only state
    private Texture2D previewTexture;
    private bool dirty;
    private int selectedTileColorId = 1;
    private int tileBrushRadius = 0;
    private Vector2Int? previousPaintCell;

    [MenuItem("Tools/Sand Flow Puzzle/Level Editor")]
    public static void ShowWindow()
    {
        var window = GetWindow<LevelEditorWindow>("Level Editor");
        window.minSize = new Vector2(700, 500);
    }

    private void OnEnable()
    {
        wantsMouseMove = true;
        LoadFromDisk();
    }

    private void OnLostFocus()
    {
        EndTilePaintStroke();
    }

    private void OnDisable()
    {
        EndTilePaintStroke();
        if (tileCanvasTexture != null) DestroyImmediate(tileCanvasTexture);
        CleanupPreview();
        foreach (var texture in boardPictureTextures.Values) if (texture != null) DestroyImmediate(texture);
        boardPictureTextures.Clear();
        boardPictureHashes.Clear();
    }

    private void LoadFromDisk()
    {
        LevelManager.LoadAllLevelsFromDisk();
        levels = LevelManager.GetAllLevels();
        if (levels.Count > 0 && selectedIndex < 0)
            selectedIndex = 0;
        if (selectedIndex >= levels.Count)
            selectedIndex = levels.Count - 1;
        PrepareSelectedTiles();
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
                    selectedPictureIndex = 0;
                    PrepareSelectedTiles();
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
            levels.Add(newLevel);
            selectedIndex = levels.Count - 1;
            selectedPictureIndex = 0;
            PrepareSelectedTiles();
            RegeneratePreview();
            dirty = true;
        }
        if (GUILayout.Button("Duplicate") && selectedIndex >= 0)
        {
            string json = JsonUtility.ToJson(levels[selectedIndex]);
            LevelData copy = JsonUtility.FromJson<LevelData>(json);
            copy.levelName += " (Copy)";
            levels.Insert(selectedIndex + 1, copy);
            selectedIndex++;
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
                dirty = true;
                PrepareSelectedTiles();
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
            try
            {
                foreach (LevelData item in levels)
                {
                    item.PrepareRuntimePictures();
                    SandBoardUtility.EnsureUnified(item);
                    if (!CollectorBoardUtility.TryResolve(item, out _, out string boardError))
                        throw new System.InvalidOperationException($"{item.levelName}: {boardError}");
                }
                LevelManager.SaveAllLevels();
                LoadFromDisk();
                dirty = false;
                Debug.Log($"[LevelEditor] Saved {levels.Count} level(s)");
            }
            catch (System.InvalidOperationException exception)
            {
                EditorUtility.DisplayDialog("Cannot save pictures", exception.Message, "OK");
            }
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
        DrawPicturesSection(level);

        EditorGUILayout.Space(8);
        DrawCollectorBoardSection(level);

        EditorGUILayout.Space(8);
        DrawBoosterSection(level);

        EditorGUILayout.EndScrollView();
        EditorGUILayout.EndVertical();
    }

    // =====================================================================
    // TILE PAINTER (legacy — removed; painting now done via Select Cells mode)
    // =====================================================================

    private SandPictureData GetSelectedPicture()
    {
        LevelData level = levels[selectedIndex];
        if (level.sandPictures == null || level.sandPictures.Count == 0) return level;
        selectedPictureIndex = Mathf.Clamp(selectedPictureIndex, 0, level.sandPictures.Count - 1);
        return level.sandPictures[selectedPictureIndex];
    }

    private void DrawPicturesSection(LevelData level)
    {
        if (level.sandPictures == null) level.sandPictures = new List<SandPictureData>();
        EditorGUILayout.LabelField("Picture Tiles", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("Manage sand pictures here. Use Select Cells mode on the board to paint sand for each picture.", MessageType.Info);
        if (level.sandPictures.Count > 0)
        {
            string[] names = new string[level.sandPictures.Count];
            for (int i = 0; i < names.Length; i++) names[i] = $"{i + 1}: {level.sandPictures[i].levelName}";
            int next = EditorGUILayout.Popup("Edit Picture", Mathf.Clamp(selectedPictureIndex, 0, names.Length - 1), names);
            if (next != selectedPictureIndex)
            {
                selectedPictureIndex = next;
                PrepareSelectedTiles();
                RegeneratePreview();
            }
            EditorGUI.BeginChangeCheck();
            GetSelectedPicture().levelName = EditorGUILayout.TextField("Picture Name", GetSelectedPicture().levelName);
            if (EditorGUI.EndChangeCheck()) dirty = true;
        }
        EditorGUILayout.BeginHorizontal();
        using (new EditorGUI.DisabledScope(level.sandPictures.Count >= 4))
        {
            if (GUILayout.Button("Add Blank Picture"))
            {
                SandPictureData copy = JsonUtility.FromJson<SandPictureData>(JsonUtility.ToJson(GetSelectedPicture()));
                copy.gridSize = SandSimulator.GRID_SIZE;
                copy.sandGrid = new List<byte>(new byte[copy.gridSize * copy.gridSize]);
                copy.sourceImagePath = "";
                copy.sandSourceMode = SandSourceMode.Tiles;
                copy.sandPatternResourcePath = "";
                if (level.sandPictures.Count == 0)
                    level.sandPictures.Add(JsonUtility.FromJson<SandPictureData>(JsonUtility.ToJson(level)));
                copy.levelName = $"Picture {level.sandPictures.Count + 1}";
                level.sandPictures.Add(copy);
                selectedPictureIndex = level.sandPictures.Count - 1;
                PrepareSelectedTiles();
                RegeneratePreview();
                dirty = true;
            }
        }
        using (new EditorGUI.DisabledScope(level.sandPictures.Count <= 1))
        {
            if (GUILayout.Button("Remove Selected Picture"))
            {
                int removedPicture = selectedPictureIndex;
                level.sandPictures.RemoveAt(removedPicture);
                if (level.collectorBoard?.sandRegions != null)
                {
                    level.collectorBoard.sandRegions.RemoveAll(region => region.pictureIndex == removedPicture);
                    foreach (var region in level.collectorBoard.sandRegions)
                        if (region.pictureIndex > removedPicture) region.pictureIndex--;
                    blockLevel = null;
                }
                selectedPictureIndex = Mathf.Clamp(selectedPictureIndex, 0, level.sandPictures.Count - 1);
                PrepareSelectedTiles();
                RegeneratePreview();
                dirty = true;
            }
        }
        EditorGUILayout.EndHorizontal();
    }

    private void PrepareSelectedTiles()
    {
        EndTilePaintStroke();
        tileCanvas = null;
        if (selectedIndex < 0 || selectedIndex >= levels.Count) return;
        SandPictureData picture = GetSelectedPicture();
        var region = SandBoardUtility.FindRegion(levels[selectedIndex].collectorBoard, selectedPictureIndex);
        int targetSize = region != null && region.occupiedCells.Count > 0
            ? SandBoardUtility.GridSize(region) : Mathf.Max(1, picture.gridSize);
        if (picture.gridSize != targetSize || picture.sandGrid == null || picture.sandGrid.Count != targetSize * targetSize)
        {
            SandBoardUtility.ResizePicture(picture, targetSize);
            dirty = true;
        }
        if (picture.palette == null || picture.palette.Count == 0)
        {
            picture.palette = CreateDefaultTilePalette();
            dirty = true;
        }
        // Existing image/pattern pixels become an editable snapshot in the level JSON.
        if (picture.sandSourceMode != SandSourceMode.Tiles || !string.IsNullOrEmpty(picture.sandPatternResourcePath)
            || !string.IsNullOrEmpty(picture.sourceImagePath))
        {
            picture.sandSourceMode = SandSourceMode.Tiles;
            picture.sandPatternResourcePath = "";
            picture.sourceImagePath = "";
            dirty = true;
        }
    }

    private void DrawTilePalette(SandPictureData level)
    {
        EditorGUILayout.Space(4f);
        EditorGUILayout.LabelField("Paint Color:");
        EditorGUILayout.BeginHorizontal();

        Color previousBackground = GUI.backgroundColor;
        GUI.backgroundColor = selectedTileColorId == 0 ? Color.white : new Color(0.65f, 0.65f, 0.65f);
        if (GUILayout.Button("Eraser", GUILayout.Width(58f), GUILayout.Height(26f)))
            selectedTileColorId = 0;
        GUI.backgroundColor = previousBackground;

        for (int i = 0; i < level.palette.Count; i++)
        {
            if (i > 0 && i % Mathf.Max(3, Mathf.FloorToInt((position.width - 360f) / 32f)) == 0) { EditorGUILayout.EndHorizontal(); EditorGUILayout.BeginHorizontal(); }
            SerializableColor serializable = level.palette[i];
            Color color = new Color(serializable.r, serializable.g, serializable.b, 1f);
            Rect swatch = GUILayoutUtility.GetRect(28f, 26f, GUILayout.Width(28f), GUILayout.Height(26f));
            EditorGUI.DrawRect(swatch, color);
            if (selectedTileColorId == i + 1)
                DrawRectBorder(swatch, Color.white, 3f);
            if (GUI.Button(swatch, GUIContent.none, GUIStyle.none))
                selectedTileColorId = i + 1;
        }

        GUI.enabled = level.palette.Count < byte.MaxValue;
        if (GUILayout.Button("+", GUILayout.Width(28f), GUILayout.Height(26f)))
        {
            var candidate = new SerializableColor(1f, 1f, 1f);
            int existing = level.palette.FindIndex(c => CollectorBoardUtility.ColorKey(c) == CollectorBoardUtility.ColorKey(candidate));
            if (existing >= 0)
            {
                selectedTileColorId = existing + 1;
            }
            else
            {
                level.palette.Add(candidate);
                selectedTileColorId = level.palette.Count;
                TilesChanged(Event.current);
            }
        }
        GUI.enabled = true;
        EditorGUILayout.EndHorizontal();

        if (selectedTileColorId > 0 && selectedTileColorId <= level.palette.Count)
        {
            SerializableColor current = level.palette[selectedTileColorId - 1];
            Color edited = new Color(current.r, current.g, current.b, 1f);
            EditorGUI.BeginChangeCheck();
            edited = EditorGUILayout.ColorField("Selected Color", edited);
            if (EditorGUI.EndChangeCheck())
            {
                var editedColor = new SerializableColor(edited.r, edited.g, edited.b);
                int duplicate = -1;
                for (int k = 0; k < level.palette.Count; k++)
                {
                    if (k == selectedTileColorId - 1) continue;
                    if (CollectorBoardUtility.ColorKey(level.palette[k]) == CollectorBoardUtility.ColorKey(editedColor))
                    { duplicate = k; break; }
                }
                if (duplicate >= 0)
                {
                    // Merge: repaint pixels of the edited swatch to the existing one,
                    // then remove the edited swatch and shift ids.
                    int from = selectedTileColorId - 1;
                    for (int p = 0; p < level.sandGrid.Count; p++)
                    {
                        if (level.sandGrid[p] == selectedTileColorId)
                            level.sandGrid[p] = (byte)(duplicate + 1);
                        else if (level.sandGrid[p] > selectedTileColorId)
                            level.sandGrid[p]--;
                    }
                    level.palette.RemoveAt(from);
                    selectedTileColorId = duplicate + 1;
                    TilesChanged(Event.current);
                }
                else
                {
                    level.palette[selectedTileColorId - 1] = editedColor;
                    TilesChanged(Event.current);
                }
            }
        }
    }

    // =====================================================================
    // HELPERS
    // =====================================================================

    private void DrawRectBorder(Rect rect, Color color, float thickness)
    {
        EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, thickness), color); // top
        EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - thickness, rect.width, thickness), color); // bottom
        EditorGUI.DrawRect(new Rect(rect.x, rect.y, thickness, rect.height), color); // left
        EditorGUI.DrawRect(new Rect(rect.xMax - thickness, rect.y, thickness, rect.height), color); // right
    }

    // =====================================================================
    // BELT & BOOSTERS SECTION
    // =====================================================================

    private void DrawBoosterSection(LevelData level)
    {
        EditorGUILayout.LabelField("Boosters", EditorStyles.boldLabel);

        EditorGUI.BeginChangeCheck();

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

    private void RegeneratePreview()
    {
        CleanupPreview();

        if (selectedIndex < 0 || selectedIndex >= levels.Count) return;
        SandPictureData level = GetSelectedPicture();
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

    private static List<SerializableColor> CreateDefaultTilePalette()
    {
        return new List<SerializableColor>
        {
            new SerializableColor(33f / 255f, 150f / 255f, 243f / 255f),
            new SerializableColor(253f / 255f, 251f / 255f, 247f / 255f),
            new SerializableColor(244f / 255f, 67f / 255f, 54f / 255f),
            new SerializableColor(255f / 255f, 202f / 255f, 40f / 255f)
        };
    }

}
}
