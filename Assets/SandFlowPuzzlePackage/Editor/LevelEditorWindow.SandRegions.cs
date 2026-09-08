using System.Collections.Generic;
using SandFlowPuzzle.BlockAuthoring;
using SandFlowPuzzle.BlockAuthoring.EditorTools;
using UnityEditor;
using UnityEngine;

namespace SandFlowPuzzle
{
    public partial class LevelEditorWindow
    {
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

        // ───────────────────────────────────────────────────────────────────
        // Selected Cells Painter — the right-panel paint tile canvas
        // ───────────────────────────────────────────────────────────────────

        /// <summary>
        /// Draws the paint canvas for the currently selected cells.
        /// Each selected cell is rendered as an 11x11 tile area. The canvas shows the
        /// sand picture content for those cells, and the user can paint directly on it.
        /// </summary>
        private void DrawSelectedCellsPainter(LevelData level, RectInt cellBounds)
        {
            int count = level.sandPictures != null && level.sandPictures.Count > 0 ? level.sandPictures.Count : 1;
            string[] options = new string[count];
            for (int i = 0; i < count; i++) options[i] = $"Picture {i + 1}: {(level.sandPictures != null && i < level.sandPictures.Count ? level.sandPictures[i].levelName : level.levelName)}";
            int next = EditorGUILayout.Popup("Paint for picture", Mathf.Clamp(selectedPictureIndex, 0, count - 1), options);
            if (next != selectedPictureIndex)
            {
                selectedPictureIndex = next;
                PrepareSelectedTiles();
                RegeneratePreview();
            }

            SandPictureData picture = GetSelectedPicture();
            if (picture.sandGrid == null || picture.sandGrid.Count != picture.gridSize * picture.gridSize)
            {
                EditorGUILayout.HelpBox("Selected picture has invalid grid data.", MessageType.Warning);
                return;
            }

            // Ensure palette
            if (picture.palette == null || picture.palette.Count == 0)
            {
                picture.palette = CreateDefaultTilePalette();
                dirty = true;
            }

            // ── Paint color palette ────────────────────────────────────────
            selectedTileColorId = Mathf.Clamp(selectedTileColorId, 0, picture.palette.Count);
            DrawTilePalette(picture);
            tileBrushRadius = EditorGUILayout.IntSlider("Brush Radius", tileBrushRadius, 0, 5);

            EditorGUILayout.HelpBox(
                "Paint on the canvas below. Left-drag paints, right-drag erases. The canvas shows each selected cell as an 11x11 tile.",
                MessageType.Info);

            // ── Calculate canvas size ──────────────────────────────────────
            // Each cell = 11x11 display pixels; total canvas matches cell aspect ratio
            int tilesPerCell = SandSimulator.GRID_SIZE; // 35
            int canvasCellW = cellBounds.width;
            int canvasCellH = cellBounds.height;
            int sandW = canvasCellW * tilesPerCell;
            int sandH = canvasCellH * tilesPerCell;

            // Cap the display size so it fits in the panel
            float maxDisplaySize = Mathf.Min(440f, Mathf.Max(240f, position.width - 260f));
            float displayW, displayH;
            if (sandW >= sandH)
            {
                displayW = maxDisplaySize;
                displayH = maxDisplaySize * sandH / sandW;
            }
            else
            {
                displayH = maxDisplaySize;
                displayW = maxDisplaySize * sandW / sandH;
            }

            // ── Draw the paint canvas ──────────────────────────────────────
            Rect canvasRect = GUILayoutUtility.GetRect(displayW, displayH, GUILayout.Width(displayW), GUILayout.Height(displayH));
            EditorGUI.DrawRect(canvasRect, new Color(0.18f, 0.18f, 0.18f));

            // Build the preview texture for the selected cells
            Texture2D cellPreview = BuildSelectedCellsPreview(picture, cellBounds, sandW, sandH);
            if (cellPreview != null)
            {
                EditorGUI.DrawPreviewTexture(canvasRect, cellPreview, null, ScaleMode.StretchToFill);
                DestroyImmediate(cellPreview);
            }

            // Draw cell grid lines on the canvas
            float cellDisplayW = displayW / canvasCellW;
            float cellDisplayH = displayH / canvasCellH;
            Color gridColor = new Color(1f, 1f, 1f, 0.25f);
            for (int cx = 1; cx < canvasCellW; cx++)
            {
                float x = canvasRect.x + cx * cellDisplayW;
                EditorGUI.DrawRect(new Rect(x, canvasRect.y, 1f, canvasRect.height), gridColor);
            }
            for (int cy = 1; cy < canvasCellH; cy++)
            {
                float y = canvasRect.y + cy * cellDisplayH;
                EditorGUI.DrawRect(new Rect(canvasRect.x, y, canvasRect.width, 1f), gridColor);
            }

            // Draw sub-cell grid lines (every 11 pixels within each cell)
            Color subGridColor = new Color(0.5f, 0.5f, 0.5f, 0.12f);
            float pxW = displayW / sandW;
            float pxH = displayH / sandH;
            for (int sx = 1; sx < sandW; sx++)
            {
                if (sx % tilesPerCell == 0) continue; // skip cell boundaries (already drawn)
                float x = canvasRect.x + sx * pxW;
                EditorGUI.DrawRect(new Rect(x, canvasRect.y, 1f, canvasRect.height), subGridColor);
            }
            for (int sy = 1; sy < sandH; sy++)
            {
                if (sy % tilesPerCell == 0) continue;
                float y = canvasRect.y + sy * pxH;
                EditorGUI.DrawRect(new Rect(canvasRect.x, y, canvasRect.width, 1f), subGridColor);
            }

            LevelEditorGridRenderer.DrawRectBorder(canvasRect, Color.gray, 1f);

            // ── Handle painting on the canvas ──────────────────────────────
            HandleSelectedCellsPainting(picture, cellBounds, canvasRect, sandW, sandH);

            // ── Action buttons ─────────────────────────────────────────────
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Apply Sand Placement"))
            {
                ApplySelectedCellsSandRegion(level, cellBounds);
            }
            if (GUILayout.Button("Edit Existing Region"))
            {
                // Load an existing region's cells into the selection
                var existingRegion = SandBoardUtility.FindRegion(blockDocument.Data, selectedPictureIndex);
                if (existingRegion != null)
                {
                    blockState.ClearCellSelection();
                    foreach (var c in existingRegion.occupiedCells)
                        blockState.AddCell(new Vector2Int(c.x, c.y));
                    PrepareSelectedTiles();
                    RegeneratePreview();
                }
                else
                {
                    blockError = "No sand region found for this picture.";
                }
            }
            EditorGUILayout.EndHorizontal();

            if (GUILayout.Button("Clear Sand in Selection"))
            {
                if (EditorUtility.DisplayDialog("Clear Sand", "Clear all sand in the selected cells for this picture?", "Clear", "Cancel"))
                {
                    ClearSandInSelection(picture, cellBounds);
                }
            }
        }

        // ───────────────────────────────────────────────────────────────────
        // Build preview texture for selected cells
        // ───────────────────────────────────────────────────────────────────

        private Texture2D BuildSelectedCellsPreview(SandPictureData picture, RectInt cellBounds, int sandW, int sandH)
        {
            int tilesPerCell = picture.gridSize;
            int gs = picture.sandGrid.Count == tilesPerCell * tilesPerCell ? tilesPerCell : SandSimulator.GRID_SIZE;

            var tex = new Texture2D(sandW, sandH, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Point;
            tex.wrapMode = TextureWrapMode.Clamp;
            var pixels = new Color32[sandW * sandH];
            Color32 empty = new Color32(85, 85, 85, 255);

            for (int canvasY = 0; canvasY < sandH; canvasY++)
            {
                for (int canvasX = 0; canvasX < sandW; canvasX++)
                {
                    // Map canvas pixel to cell + local position
                    int cellLocalX = canvasX % tilesPerCell;
                    int cellLocalY = canvasY % tilesPerCell;
                    int cellX = cellBounds.x + canvasX / tilesPerCell;
                    int cellY = cellBounds.y + canvasY / tilesPerCell;

                    // Map local cell position to sandGrid position
                    // The sandGrid is split into a grid of cells; each cell gets a sub-area
                    int gridW = cellBounds.width;
                    int gridH = cellBounds.height;
                    int cellIndexX = cellX - cellBounds.x;
                    int cellIndexY = cellY - cellBounds.y;

                    // Sub-area bounds in the sandGrid (Y-flipped for bottom-left convention)
                    int subW = Mathf.CeilToInt((float)gs * (cellIndexX + 1) / gridW) - Mathf.CeilToInt((float)gs * cellIndexX / gridW);
                    int subH = Mathf.CeilToInt((float)gs * (cellIndexY + 1) / gridH) - Mathf.CeilToInt((float)gs * cellIndexY / gridH);
                    int subStartX = Mathf.CeilToInt((float)gs * cellIndexX / gridW);
                    int subStartY = Mathf.CeilToInt((float)gs * cellIndexY / gridH);

                    // Local pixel within the sub-area (Y-flipped)
                    int localX = subW > 0 ? Mathf.Clamp(Mathf.FloorToInt((float)cellLocalX * subW / tilesPerCell), 0, subW - 1) : 0;
                    int localY = subH > 0 ? Mathf.Clamp(Mathf.FloorToInt((float)cellLocalY * subH / tilesPerCell), 0, subH - 1) : 0;

                    // sandGrid Y is bottom-up, canvas Y is top-down
                    int gridX = subStartX + localX;
                    int gridY = (gs - 1) - (subStartY + localY);
                    gridX = Mathf.Clamp(gridX, 0, gs - 1);
                    gridY = Mathf.Clamp(gridY, 0, gs - 1);

                    int gridIdx = gridY * gs + gridX;
                    byte colorId = gridIdx >= 0 && gridIdx < picture.sandGrid.Count ? picture.sandGrid[gridIdx] : (byte)0;

                    // Top-down pixel index
                    int dstIdx = (sandH - 1 - canvasY) * sandW + canvasX;
                    if (colorId == 0 || picture.palette == null || colorId > picture.palette.Count)
                        pixels[dstIdx] = empty;
                    else
                    {
                        SerializableColor sc = picture.palette[colorId - 1];
                        pixels[dstIdx] = new Color32(
                            (byte)(sc.r * 255f), (byte)(sc.g * 255f), (byte)(sc.b * 255f), 255);
                    }
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply();
            return tex;
        }

        // ───────────────────────────────────────────────────────────────────
        // Handle painting on the selected-cells canvas
        // ───────────────────────────────────────────────────────────────────

        private void HandleSelectedCellsPainting(SandPictureData picture, RectInt cellBounds, Rect canvasRect, int sandW, int sandH)
        {
            Event currentEvent = Event.current;
            if (currentEvent.type == EventType.MouseUp) previousPaintCell = null;

            bool isPaintEvent = currentEvent.type == EventType.MouseDown || currentEvent.type == EventType.MouseDrag;
            if (!isPaintEvent || !canvasRect.Contains(currentEvent.mousePosition)) return;
            if (currentEvent.button != 0 && currentEvent.button != 1) return;

            int tilesPerCell = picture.gridSize;
            int gs = picture.sandGrid.Count == tilesPerCell * tilesPerCell ? tilesPerCell : SandSimulator.GRID_SIZE;

            Vector2 local = currentEvent.mousePosition - canvasRect.position;
            int canvasX = Mathf.Clamp(Mathf.FloorToInt(local.x / canvasRect.width * sandW), 0, sandW - 1);
            int canvasY = Mathf.Clamp(Mathf.FloorToInt(local.y / canvasRect.height * sandH), 0, sandH - 1);

            byte colorId = currentEvent.button == 1
                ? (byte)0
                : (byte)Mathf.Clamp(selectedTileColorId, 0, byte.MaxValue);

            // Interpolate for smooth strokes
            Vector2Int endCell = new Vector2Int(canvasX, canvasY);
            Vector2Int startCell = currentEvent.type == EventType.MouseDrag && previousPaintCell.HasValue
                ? previousPaintCell.Value : endCell;
            int steps = Mathf.Max(Mathf.Abs(endCell.x - startCell.x), Mathf.Abs(endCell.y - startCell.y));

            for (int step = 0; step <= steps; step++)
            {
                float t = steps == 0 ? 0f : step / (float)steps;
                int cx = Mathf.RoundToInt(Mathf.Lerp(startCell.x, endCell.x, t));
                int cy = Mathf.RoundToInt(Mathf.Lerp(startCell.y, endCell.y, t));

                // Apply brush radius
                for (int offY = -tileBrushRadius; offY <= tileBrushRadius; offY++)
                {
                    for (int offX = -tileBrushRadius; offX <= tileBrushRadius; offX++)
                    {
                        if (offX * offX + offY * offY > tileBrushRadius * tileBrushRadius) continue;
                        int px = cx + offX;
                        int py = cy + offY;
                        if (px < 0 || px >= sandW || py < 0 || py >= sandH) continue;

                        PaintSandPixel(picture, cellBounds, px, py, sandW, sandH, gs, tilesPerCell, colorId);
                    }
                }
            }

            previousPaintCell = endCell;
            TilesChanged(currentEvent);
            currentEvent.Use();
            Repaint();
        }

        private void PaintSandPixel(SandPictureData picture, RectInt cellBounds,
            int canvasX, int canvasY, int sandW, int sandH, int gs, int tilesPerCell, byte colorId)
        {
            int cellLocalX = canvasX % tilesPerCell;
            int cellLocalY = canvasY % tilesPerCell;
            int cellX = cellBounds.x + canvasX / tilesPerCell;
            int cellY = cellBounds.y + canvasY / tilesPerCell;

            int gridW = cellBounds.width;
            int gridH = cellBounds.height;
            int cellIndexX = cellX - cellBounds.x;
            int cellIndexY = cellY - cellBounds.y;

            int subStartX = Mathf.CeilToInt((float)gs * cellIndexX / gridW);
            int subStartY = Mathf.CeilToInt((float)gs * cellIndexY / gridH);
            int subW = Mathf.CeilToInt((float)gs * (cellIndexX + 1) / gridW) - subStartX;
            int subH = Mathf.CeilToInt((float)gs * (cellIndexY + 1) / gridH) - subStartY;

            int localX = subW > 0 ? Mathf.Clamp(Mathf.FloorToInt((float)cellLocalX * subW / tilesPerCell), 0, subW - 1) : 0;
            int localY = subH > 0 ? Mathf.Clamp(Mathf.FloorToInt((float)cellLocalY * subH / tilesPerCell), 0, subH - 1) : 0;

            int gridX = Mathf.Clamp(subStartX + localX, 0, gs - 1);
            int gridY = Mathf.Clamp((gs - 1) - (subStartY + localY), 0, gs - 1);

            int gridIdx = gridY * gs + gridX;
            if (gridIdx >= 0 && gridIdx < picture.sandGrid.Count)
                picture.sandGrid[gridIdx] = colorId;
        }

        // ───────────────────────────────────────────────────────────────────
        // Apply / Clear sand region from selection
        // ───────────────────────────────────────────────────────────────────

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

        private void ClearSandInSelection(SandPictureData picture, RectInt cellBounds)
        {
            int tilesPerCell = picture.gridSize;
            int gs = picture.sandGrid.Count == tilesPerCell * tilesPerCell ? tilesPerCell : SandSimulator.GRID_SIZE;

            for (int canvasY = 0; canvasY < cellBounds.height * tilesPerCell; canvasY++)
                for (int canvasX = 0; canvasX < cellBounds.width * tilesPerCell; canvasX++)
                    PaintSandPixel(picture, cellBounds, canvasX, canvasY,
                        cellBounds.width * tilesPerCell, cellBounds.height * tilesPerCell,
                        gs, tilesPerCell, 0);

            TilesChanged(Event.current);
        }

        private void TilesChanged(Event evt)
        {
            SandPictureData picture = GetSelectedPicture();
            picture.sandSourceMode = SandSourceMode.Tiles;
            picture.sandPatternResourcePath = "";
            picture.sourceImagePath = "";
            RegeneratePreview();
            dirty = true;
        }
    }
}
