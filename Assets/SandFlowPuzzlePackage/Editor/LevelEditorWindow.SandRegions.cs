using System.Collections.Generic;
using SandFlowPuzzle.BlockAuthoring;
using SandFlowPuzzle.BlockAuthoring.EditorTools;
using UnityEditor;
using UnityEngine;

namespace SandFlowPuzzle
{
    public partial class LevelEditorWindow
    {
        private int tilePaintMode;
        private Vector2Int areaPaintStart;
        private byte[] areaPaintOriginal;
        private int tilePaintControl;
        private int tilePaintButton;
        private byte tileStrokeColor;
        private TilePaintCanvas tileCanvas;
        private SandPictureData tileCanvasPicture;
        private Texture2D tileCanvasTexture;
        private bool tileCanvasDirty = true;
        private bool showTileGrid = true;
        private float tileCanvasZoom = 1f;
        private Vector2 tileCanvasScroll;
        private readonly List<byte[]> tileUndo = new List<byte[]>();
        private readonly List<byte[]> tileRedo = new List<byte[]>();
        private int tilePaletteHash;

        private void EndTilePaintStroke()
        {
            if (areaPaintOriginal != null && tileCanvasPicture != null)
                RememberTileEdit(areaPaintOriginal);
            if (tilePaintControl != 0 && GUIUtility.hotControl == tilePaintControl)
                GUIUtility.hotControl = 0;
            tilePaintControl = 0;
            previousPaintCell = null;
            areaPaintOriginal = null;
        }

        private void RememberTileEdit(byte[] before)
        {
            if (before.Length != tileCanvasPicture.sandGrid.Count) return;
            bool changed = false;
            for (int i = 0; i < before.Length; i++)
                if (before[i] != tileCanvasPicture.sandGrid[i]) { changed = true; break; }
            if (!changed) return;
            tileUndo.Add(before);
            if (tileUndo.Count > 50) tileUndo.RemoveAt(0);
            tileRedo.Clear();
        }

        private void RestoreTilePixels(byte[] pixels)
        {
            for (int i = 0; i < pixels.Length; i++) tileCanvasPicture.sandGrid[i] = pixels[i];
            TilesChanged(Event.current);
        }

        private void StepTileHistory(bool redo)
        {
            var source = redo ? tileRedo : tileUndo;
            var target = redo ? tileUndo : tileRedo;
            if (source.Count == 0) return;
            target.Add(tileCanvasPicture.sandGrid.ToArray());
            byte[] pixels = source[source.Count - 1];
            source.RemoveAt(source.Count - 1);
            RestoreTilePixels(pixels);
        }

        private readonly Dictionary<int, Texture2D> boardPictureTextures = new Dictionary<int, Texture2D>();
        private readonly Dictionary<int, int> boardPictureHashes = new Dictionary<int, int>();

        // ───────────────────────────────────────────────────────────────────
        // Draw existing sand regions on the grid overlay
        // ───────────────────────────────────────────────────────────────────

        private void DrawSandRegions(LevelData level, LevelGridViewport viewport)
        {
            if (blockDocument.Data.sandRegions == null) return;

            var pictures = level.sandPictures != null && level.sandPictures.Count > 0
                ? level.sandPictures : new List<SandPictureData> { level };

            foreach (var region in blockDocument.Data.sandRegions)
            {
                if (region.pictureIndex < 0 || region.pictureIndex >= pictures.Count) continue;
                if (region.occupiedCells == null || region.occupiedCells.Count == 0) continue;

                RectInt bounds = SandBoardUtility.Bounds(region);
                Texture2D texture = GetBoardPictureTexture(region.pictureIndex, pictures[region.pictureIndex]);

                foreach (var cell in region.occupiedCells)
                {
                    Rect rect = viewport.GetCellRect(new Vector2Int(cell.x, cell.y));
                    if (texture != null)
                    {
                        float side = Mathf.Max(bounds.width, bounds.height);
                        GUI.DrawTextureWithTexCoords(rect, texture, new Rect(
                            (cell.x - bounds.x) / side,
                            (cell.y - bounds.y) / side,
                            1f / side, 1f / side));
                    }
                    else EditorGUI.DrawRect(rect, Color.gray);
                    LevelEditorGridRenderer.DrawRectBorder(rect, new Color(0.65f, 0.8f, 1f), 2f);
                }

                // Label
                if (region.occupiedCells.Count > 0)
                {
                    Rect label = viewport.GetCellRect(new Vector2Int(region.occupiedCells[0].x, region.occupiedCells[0].y));
                    GUI.Label(label, $"S{region.pictureIndex + 1}", EditorStyles.whiteBoldLabel);
                }
            }
        }

        // ───────────────────────────────────────────────────────────────────
        // Texture cache for sand region previews on the grid
        // ───────────────────────────────────────────────────────────────────

        private Texture2D GetBoardPictureTexture(int index, SandPictureData picture)
        {
            if (picture.sandGrid == null || picture.gridSize < 1
                || picture.sandGrid.Count != picture.gridSize * picture.gridSize) return null;

            int hash = picture.gridSize;
            unchecked
            {
                foreach (byte cell in picture.sandGrid) hash = hash * 31 + cell;
                foreach (var color in picture.palette) hash = hash * 31 + CollectorBoardUtility.ColorKey(color);
            }

            if (boardPictureTextures.TryGetValue(index, out var texture) && texture != null
                && boardPictureHashes.TryGetValue(index, out int oldHash) && hash == oldHash) return texture;

            if (texture != null) DestroyImmediate(texture);

            int size = picture.gridSize;
            texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    int id = picture.sandGrid[y * size + x];
                    var color = id > 0 && id <= picture.palette.Count ? picture.palette[id - 1] : null;
                    pixels[(size - 1 - y) * size + x] = color == null
                        ? (Color32)new Color(.25f, .25f, .25f)
                        : (Color32)new Color(color.r, color.g, color.b);
                }
            texture.SetPixels32(pixels);
            texture.Apply(false);
            boardPictureTextures[index] = texture;
            boardPictureHashes[index] = hash;
            return texture;
        }

        private void DrawSelectedCellsPainter(LevelData level, RectInt cellBounds)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Paint Tiles", EditorStyles.boldLabel);
            SandPictureData picture = GetSelectedPicture();
            if (picture.gridSize < 1 || picture.sandGrid == null || picture.sandGrid.Count != picture.gridSize * picture.gridSize)
            {
                EditorGUILayout.HelpBox("Selected picture has invalid grid data.", MessageType.Warning);
                EditorGUILayout.EndVertical();
                return;
            }
            if (picture.palette == null || picture.palette.Count == 0)
            {
                picture.palette = CreateDefaultTilePalette();
                dirty = true;
            }
            if (!ReferenceEquals(tileCanvasPicture, picture) || tileCanvas == null
                || !tileCanvas.Matches(blockState.SelectedCells, picture.gridSize))
            {
                EndTilePaintStroke();
                tileCanvasPicture = picture;
                tileCanvas = new TilePaintCanvas(blockState.SelectedCells, cellBounds);
                if (picture.gridSize != tileCanvas.Size)
                {
                    SandBoardUtility.ResizePicture(picture, tileCanvas.Size);
                    TilesChanged(Event.current);
                }
                tileUndo.Clear();
                tileRedo.Clear();
                tileCanvasDirty = true;
            }
            EditorGUILayout.LabelField($"{tileCanvas.CellCount} cells • 11 × 11 pixels/cell • {tileCanvas.Width} × {tileCanvas.Height} canvas • {tileCanvas.PaintableCount} paintable pixels", EditorStyles.wordWrappedMiniLabel);
            selectedTileColorId = Mathf.Clamp(selectedTileColorId, 0, picture.palette.Count);
            DrawTilePalette(picture);
            int paletteHash = 17;
            unchecked { foreach (var color in picture.palette) paletteHash = paletteHash * 31 + CollectorBoardUtility.ColorKey(color); }
            if (tilePaletteHash != paletteHash)
            {
                tilePaletteHash = paletteHash;
                tileUndo.Clear();
                tileRedo.Clear();
                tileCanvasDirty = true;
            }
            tilePaintMode = GUILayout.Toolbar(tilePaintMode, new[] { "Fill Area", "Brush", "Eraser" }, GUILayout.Height(28));
            if (tilePaintMode != 0)
                tileBrushRadius = EditorGUILayout.IntSlider("Brush radius", tileBrushRadius, 0, 5);

            EditorGUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(tileUndo.Count == 0))
                if (GUILayout.Button("Undo")) StepTileHistory(false);
            using (new EditorGUI.DisabledScope(tileRedo.Count == 0))
                if (GUILayout.Button("Redo")) StepTileHistory(true);
            if (GUILayout.Button("Fill selected")) FillTileSelection((byte)selectedTileColorId);
            if (GUILayout.Button("Clear selected")) FillTileSelection(0);
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.HelpBox("Drag to paint; right-drag to erase. Fill Area fills a rectangle. Dark cells are not selected. Esc cancels the current stroke.", MessageType.None);

            EditorGUILayout.BeginHorizontal();
            showTileGrid = GUILayout.Toggle(showTileGrid, "Tile grid", GUILayout.Width(75));
            GUILayout.Label("Zoom", GUILayout.Width(35));
            tileCanvasZoom = GUILayout.HorizontalSlider(tileCanvasZoom, 1f, 3f);
            if (GUILayout.Button("Fit", GUILayout.Width(40))) tileCanvasZoom = 1f;
            EditorGUILayout.EndHorizontal();

            float available = Mathf.Max(180f, position.width - 270f);
            float pixelSize = Mathf.Min(available / tileCanvas.Width, 400f / tileCanvas.Height) * tileCanvasZoom;
            float displayW = tileCanvas.Width * pixelSize;
            float displayH = tileCanvas.Height * pixelSize;
            tileCanvasScroll = EditorGUILayout.BeginScrollView(tileCanvasScroll, GUILayout.Height(Mathf.Min(displayH + 22f, 440f)));
            Rect rect = GUILayoutUtility.GetRect(displayW, displayH, GUILayout.Width(displayW), GUILayout.Height(displayH));
            if (Event.current.type == EventType.Repaint)
            {
                UpdateTileCanvasTexture(picture);
                GUI.DrawTexture(rect, tileCanvasTexture, ScaleMode.StretchToFill);
                if (showTileGrid && pixelSize >= 7f)
                {
                    Color line = new Color(0, 0, 0, .2f);
                    for (int x = 1; x < tileCanvas.Width; x++) EditorGUI.DrawRect(new Rect(rect.x + x * pixelSize, rect.y, 1, rect.height), line);
                    for (int y = 1; y < tileCanvas.Height; y++) EditorGUI.DrawRect(new Rect(rect.x, rect.y + y * pixelSize, rect.width, 1), line);
                }
                foreach (var cell in blockState.SelectedCells)
                {
                    RectInt pixels = tileCanvas.CellPixels(cell);
                    Rect cellRect = new Rect(rect.x + pixels.x * pixelSize, rect.y + pixels.y * pixelSize, pixels.width * pixelSize, pixels.height * pixelSize);
                    LevelEditorGridRenderer.DrawRectBorder(cellRect, new Color(.4f, .8f, 1f), 2f);
                }
            }
            EditorGUIUtility.AddCursorRect(rect, MouseCursor.ArrowPlus);
            if (Event.current.type == EventType.MouseMove && rect.Contains(Event.current.mousePosition)) Repaint();
            if (Event.current.type == EventType.Repaint && rect.Contains(Event.current.mousePosition))
            {
                int x = Mathf.FloorToInt((Event.current.mousePosition.x - rect.x) / pixelSize);
                int y = Mathf.FloorToInt((Event.current.mousePosition.y - rect.y) / pixelSize);
                if (tileCanvas.TryGetIndex(x, y, out _))
                    LevelEditorGridRenderer.DrawRectBorder(new Rect(rect.x + x * pixelSize, rect.y + y * pixelSize, pixelSize, pixelSize), Color.white, 2f);
            }
            HandleSelectedCellsPainting(picture, rect);
            EditorGUILayout.EndScrollView();
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Apply to board", GUILayout.Height(28))) ApplySelectedCellsSandRegion(level, cellBounds);
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
        }

        private void UpdateTileCanvasTexture(SandPictureData picture)
        {
            if (!tileCanvasDirty && tileCanvasTexture != null) return;
            if (tileCanvasTexture == null || tileCanvasTexture.width != tileCanvas.Width || tileCanvasTexture.height != tileCanvas.Height)
            {
                if (tileCanvasTexture != null) DestroyImmediate(tileCanvasTexture);
                tileCanvasTexture = new Texture2D(tileCanvas.Width, tileCanvas.Height, TextureFormat.RGBA32, false)
                { filterMode = FilterMode.Point, hideFlags = HideFlags.HideAndDontSave };
            }
            var colors = new Color32[tileCanvas.Width * tileCanvas.Height];
            for (int y = 0; y < tileCanvas.Height; y++)
                for (int x = 0; x < tileCanvas.Width; x++)
                {
                    Color color = new Color(.12f, .12f, .12f);
                    if (tileCanvas.TryGetIndex(x, y, out int index))
                    {
                        byte id = picture.sandGrid[index];
                        color = (x + y) % 2 == 0 ? new Color(.31f, .31f, .31f) : new Color(.35f, .35f, .35f);
                        if (id > 0 && id <= picture.palette.Count)
                        {
                            var c = picture.palette[id - 1];
                            color = new Color(c.r, c.g, c.b);
                        }
                    }
                    colors[(tileCanvas.Height - 1 - y) * tileCanvas.Width + x] = color;
                }
            tileCanvasTexture.SetPixels32(colors);
            tileCanvasTexture.Apply(false);
            tileCanvasDirty = false;
        }

        private void FillTileSelection(byte color)
        {
            byte[] before = tileCanvasPicture.sandGrid.ToArray();
            tileCanvas.Fill(tileCanvasPicture.sandGrid, Vector2Int.zero, new Vector2Int(tileCanvas.Width - 1, tileCanvas.Height - 1), color);
            RememberTileEdit(before);
            TilesChanged(Event.current);
        }

        private void HandleSelectedCellsPainting(SandPictureData picture, Rect rect)
        {
            Event e = Event.current;
            int id = GUIUtility.GetControlID(FocusType.Passive, rect);
            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape && tilePaintControl != 0)
            {
                RestoreTilePixels(areaPaintOriginal);
                areaPaintOriginal = null;
                EndTilePaintStroke();
                e.Use();
                return;
            }
            if (e.rawType == EventType.MouseUp && tilePaintControl != 0 && e.button == tilePaintButton)
            {
                EndTilePaintStroke();
                e.Use();
                Repaint();
                return;
            }
            bool down = e.type == EventType.MouseDown;
            if (!down && e.type != EventType.MouseDrag) return;
            if (e.button != 0 && e.button != 1) return;
            Vector2 local = e.mousePosition - rect.position;
            var end = new Vector2Int(Mathf.Clamp(Mathf.FloorToInt(local.x / rect.width * tileCanvas.Width), 0, tileCanvas.Width - 1),
                Mathf.Clamp(Mathf.FloorToInt(local.y / rect.height * tileCanvas.Height), 0, tileCanvas.Height - 1));
            if (down)
            {
                if (!rect.Contains(e.mousePosition) || GUIUtility.hotControl != 0 || !tileCanvas.TryGetIndex(end.x, end.y, out _)) return;
                tilePaintControl = id;
                tilePaintButton = e.button;
                tileStrokeColor = e.button == 1 || tilePaintMode == 2 ? (byte)0 : (byte)selectedTileColorId;
                GUIUtility.hotControl = id;
                areaPaintStart = end;
                areaPaintOriginal = picture.sandGrid.ToArray();
            }
            else if (tilePaintControl != id || GUIUtility.hotControl != id || e.button != tilePaintButton) return;
            if (tilePaintMode == 0)
            {
                for (int i = 0; i < areaPaintOriginal.Length; i++) picture.sandGrid[i] = areaPaintOriginal[i];
                tileCanvas.Fill(picture.sandGrid, areaPaintStart, end, tileStrokeColor);
            }
            else
            {
                Vector2Int start = previousPaintCell ?? end;
                int steps = Mathf.Max(Mathf.Abs(end.x - start.x), Mathf.Abs(end.y - start.y));
                for (int step = 0; step <= steps; step++)
                {
                    float t = steps == 0 ? 0 : step / (float)steps;
                    int cx = Mathf.RoundToInt(Mathf.Lerp(start.x, end.x, t));
                    int cy = Mathf.RoundToInt(Mathf.Lerp(start.y, end.y, t));
                    for (int y = -tileBrushRadius; y <= tileBrushRadius; y++)
                        for (int x = -tileBrushRadius; x <= tileBrushRadius; x++)
                            if (x * x + y * y <= tileBrushRadius * tileBrushRadius && tileCanvas.TryGetIndex(cx + x, cy + y, out int index))
                                picture.sandGrid[index] = tileStrokeColor;
                }
            }
            previousPaintCell = end;
            TilesChanged(e);
            e.Use();
        }

        private void ApplySelectedCellsSandRegion(LevelData level, RectInt cellBounds)
        {
            if (blockDocument.Data.sandRegions == null)
                blockDocument.Data.sandRegions = new List<SandRegionFile>();

            var cells = new List<LevelCellCoord>();
            foreach (var selected in blockState.SelectedCells)
                cells.Add(new LevelCellCoord(selected.x, selected.y));

            if (cells.Count == 0)
            {
                blockError = "No cells selected.";
                return;
            }

            int pictureIdx = selectedPictureIndex;
            if (blockDocument.TryCommit("Place sand region", board =>
            {
                board.sandRegions.RemoveAll(r => r.pictureIndex == pictureIdx);
                board.sandRegions.Add(new SandRegionFile { pictureIndex = pictureIdx, occupiedCells = cells });
            }, out blockError))
            {
                blockError = null;
            }
        }

        private void TilesChanged(Event evt)
        {
            SandPictureData picture = GetSelectedPicture();
            picture.sandSourceMode = SandSourceMode.Tiles;
            picture.sandPatternResourcePath = "";
            picture.sourceImagePath = "";
            tileCanvasDirty = true;
            RegeneratePreview();
            dirty = true;
            Repaint();
        }
    }
}
