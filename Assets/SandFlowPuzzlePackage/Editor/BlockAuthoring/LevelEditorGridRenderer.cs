using System.Collections.Generic;
using SandFlowPuzzle.BlockAuthoring;
using UnityEditor;
using UnityEngine;

namespace SandFlowPuzzle.BlockAuthoring.EditorTools
{
    /// <summary>
    /// Read-only grid/block renderer for the BlockX level editor (Tasks 7-11).
    ///
    /// Draws the binary ON/OFF grid (gray = ON, black = OFF), its grid lines, the add draft, drag
    /// ghost (translucent candidate placement), the actual blocks (occupied cells, deterministic
    /// fallback color per colorId) with black outlines, a hover outline, and the selected block's outline. The renderer never
    /// reads <see cref="Event.current"/> (all input handling lives in the window) and never mutates
    /// the DTO or editor state. Render order is fixed: grid cells, grid lines, add draft/drag ghost,
    /// blocks, hover outline, selection outline.
    /// </summary>
    public sealed class LevelEditorGridRenderer
    {
        public const float BaseCellSize = 44f;
        public const float MinZoom = 0.5f;
        public const float MaxZoom = 2.0f;

        /// <summary>Inset applied to every block cell so grid lines stay legible behind it.</summary>
        public const float CellInset = 2f;

        private static readonly Color OnCellColor = new Color(0.5f, 0.5f, 0.5f, 1f);
        private static readonly Color OffCellColor = new Color(0f, 0f, 0f, 1f);
        private static readonly Color GridLineColor = new Color(0.15f, 0.15f, 0.15f, 1f);
        internal static Color BlockOutlineColor => Color.black;
        private static readonly Color HoverOutlineColor = new Color(1f, 1f, 1f, 0.9f);
        private static readonly Color SelectionOutlineColor = new Color(1f, 0.85f, 0.2f, 1f);
        private static readonly Color DragGhostValidColor = new Color(0.2f, 1f, 0.4f, 0.35f);
        private static readonly Color DragGhostInvalidColor = new Color(1f, 0.3f, 0.3f, 0.35f);
        private static readonly Color CellSelectionColor = new Color(0.2f, 0.75f, 1f, 0.35f);
        private static readonly Color CellSelectionBorderColor = new Color(0.2f, 0.75f, 1f, 0.9f);

        /// <summary>Pure cell-size computation: base size scaled by zoom, clamped to Min..Max zoom.</summary>
        public static float CellSizeForZoom(float zoom)
        {
            return BaseCellSize * Mathf.Clamp(zoom, MinZoom, MaxZoom);
        }

        /// <summary>
        /// Pure viewport construction. Rows/columns come from the document, cell size from the zoom,
        /// and the returned viewport's <see cref="LevelGridViewport.GridRect"/> is the supplied rect.
        /// </summary>
        public static LevelGridViewport BuildViewport(BlockXLevelFile data, float zoom, Rect gridRect)
        {
            float cellSize = CellSizeForZoom(zoom);
            return new LevelGridViewport(gridRect, cellSize, data.grid.rows, data.grid.columns);
        }

        /// <summary>
        /// Reserves the grid area in the current IMGUI layout, builds the viewport, and draws in
        /// fixed order: grid cells, grid lines, then (when <paramref name="drawBlocks"/> is true)
        /// the optional add draft, drag ghost, blocks, hover outline, and selection outline. Returns
        /// the viewport so the caller can map input through it.
        /// </summary>
        public LevelGridViewport DrawGrid(
            BlockXLevelFile data,
            float zoom,
            bool drawBlocks,
            LevelEditorState state,
            bool drawAddDraft = false)
        {
            if (data == null || data.grid == null)
            {
                return default;
            }

            float cellSize = CellSizeForZoom(zoom);
            Rect gridRect = GUILayoutUtility.GetRect(data.grid.columns * cellSize, data.grid.rows * cellSize);
            LevelGridViewport viewport = BuildViewport(data, zoom, gridRect);

            DrawGridCells(data, viewport);
            DrawGridLines(viewport);

            if (drawBlocks)
            {
                if (drawAddDraft && state != null)
                {
                    DrawAddDraft(data, viewport, state.AddDraft);
                }

                DrawDragGhost(data, viewport, state);
                DrawBlocks(data, viewport, state);
                DrawSelection(data, viewport, state != null ? state.SelectedBlockIndex : -1);
            }

            return viewport;
        }

        /// <summary>
        /// Draws transient draft cells below actual blocks. The first draft cell is labeled A to
        /// make the serialized/runtime anchor explicit. This method never changes the draft order.
        /// </summary>
        public void DrawAddDraft(
            BlockXLevelFile data,
            LevelGridViewport viewport,
            AddBlockDraft draft)
        {
            if (data == null || data.grid == null || draft == null || draft.Cells == null)
            {
                return;
            }

            Color previousColor = GUI.color;
            try
            {
                for (int cellIndex = 0; cellIndex < draft.Cells.Count; cellIndex++)
                {
                    Vector2Int cell = draft.Cells[cellIndex];
                    if (!LevelGridCoordinateUtility.IsInside(cell, viewport.Rows, viewport.Columns))
                    {
                        continue;
                    }

                    GUI.color = new Color(0.3f, 0.7f, 1f, 0.45f);
                    Rect cellRect = GetInsetCellRect(viewport, cell, CellInset);
                    GUI.DrawTexture(cellRect, Texture2D.whiteTexture);

                    if (cellIndex == 0)
                    {
                        GUI.color = Color.white;
                        GUI.Label(cellRect, "A", EditorStyles.boldLabel);
                    }
                }
            }
            finally
            {
                GUI.color = previousColor;
            }
        }

        /// <summary>Fills every cell: gray for ON, black for OFF.</summary>
        public void DrawGridCells(BlockXLevelFile data, LevelGridViewport viewport)
        {
            if (data == null || data.grid == null || data.grid.cells == null)
            {
                return;
            }

            Color previous = GUI.color;
            try
            {
                for (int y = 0; y < viewport.Rows; y++)
                {
                    for (int x = 0; x < viewport.Columns; x++)
                    {
                        var cell = new Vector2Int(x, y);
                        int index = LevelGridCoordinateUtility.ToIndex(cell, viewport.Rows, viewport.Columns);
                        bool on = data.grid.cells[index] == 1;
                        GUI.color = on ? OnCellColor : OffCellColor;
                        GUI.DrawTexture(viewport.GetCellRect(cell), Texture2D.whiteTexture);
                    }
                }
            }
            finally
            {
                GUI.color = previous;
            }
        }

        /// <summary>Draws the horizontal and vertical grid lines inside Handles.BeginGUI/EndGUI.</summary>
        public void DrawGridLines(LevelGridViewport viewport)
        {
            Handles.BeginGUI();
            try
            {
                Color previous = Handles.color;
                Handles.color = GridLineColor;

                float left = viewport.GridRect.x;
                float right = viewport.GridRect.xMax;
                float top = viewport.GridRect.y;
                float bottom = viewport.GridRect.yMax;

                for (int row = 0; row <= viewport.Rows; row++)
                {
                    float y = top + row * viewport.CellSize;
                    Handles.DrawLine(new Vector2(left, y), new Vector2(right, y));
                }

                for (int column = 0; column <= viewport.Columns; column++)
                {
                    float x = left + column * viewport.CellSize;
                    Handles.DrawLine(new Vector2(x, top), new Vector2(x, bottom));
                }

                Handles.color = previous;
            }
            finally
            {
                Handles.EndGUI();
            }
        }

        /// <summary>
        /// Draws every block as its occupied cells (inset so grid lines stay legible) with a
        /// deterministic fallback color per colorId, then a black outline around every block and a
        /// hover outline around the hovered block when it is a valid, non-selected block. Mutates
        /// neither the DTO nor the state.
        /// </summary>
        public void DrawBlocks(BlockXLevelFile data, LevelGridViewport viewport, LevelEditorState state)
        {
            if (data == null || data.blocks == null || viewport.Rows <= 0)
            {
                return;
            }

            Color previous = GUI.color;
            try
            {
                for (int blockIndex = 0; blockIndex < data.blocks.Count; blockIndex++)
                {
                    LevelBlockFile block = data.blocks[blockIndex];
                    if (block == null || block.occupiedCells == null)
                    {
                        continue;
                    }

                    Color blockColor = GetBlockColor(block.colorId);
                    for (int cellIndex = 0; cellIndex < block.occupiedCells.Count; cellIndex++)
                    {
                        LevelCellCoord c = block.occupiedCells[cellIndex];
                        var cell = new Vector2Int(c.x, c.y);
                        if (!LevelGridCoordinateUtility.IsInside(cell, viewport.Rows, viewport.Columns))
                        {
                            continue;
                        }

                        GUI.color = blockColor;
                        GUI.DrawTexture(GetInsetCellRect(viewport, cell, CellInset), Texture2D.whiteTexture);
                    }
                }

                for (int blockIndex = 0; blockIndex < data.blocks.Count; blockIndex++)
                {
                    LevelBlockFile block = data.blocks[blockIndex];
                    if (block == null || block.occupiedCells == null)
                    {
                        continue;
                    }

                    DrawBlockOutline(data, viewport, blockIndex, BlockOutlineColor);
                }

                if (state != null
                    && state.HoveredBlockIndex >= 0
                    && state.HoveredBlockIndex < data.blocks.Count
                    && state.HoveredBlockIndex != state.SelectedBlockIndex)
                {
                    DrawBlockOutline(data, viewport, state.HoveredBlockIndex, HoverOutlineColor);
                }
            }
            finally
            {
                GUI.color = previous;
            }
        }

        /// <summary>
        /// Draws a translucent ghost of the dragged block translated by the candidate delta, below
        /// the actual blocks. Green when <see cref="LevelEditorState.DragCandidateValid"/> is true,
        /// red when false. Reads only the DTO and state; never mutates either. No-op unless a drag
        /// is active with a valid block index.
        /// </summary>
        public void DrawDragGhost(BlockXLevelFile data, LevelGridViewport viewport, LevelEditorState state)
        {
            if (data == null || data.blocks == null || viewport.Rows <= 0 || state == null)
            {
                return;
            }

            if (!state.IsDraggingBlock
                || state.DragBlockIndex < 0
                || state.DragBlockIndex >= data.blocks.Count)
            {
                return;
            }

            LevelBlockFile block = data.blocks[state.DragBlockIndex];
            if (block == null || block.occupiedCells == null || block.occupiedCells.Count == 0)
            {
                return;
            }

            Vector2Int delta = state.DragCandidateAnchor - state.DragOriginalAnchor;
            Color ghostColor = state.DragCandidateValid ? DragGhostValidColor : DragGhostInvalidColor;

            Color previous = GUI.color;
            try
            {
                for (int cellIndex = 0; cellIndex < block.occupiedCells.Count; cellIndex++)
                {
                    LevelCellCoord source = block.occupiedCells[cellIndex];
                    var translated = new Vector2Int(source.x + delta.x, source.y + delta.y);
                    if (!LevelGridCoordinateUtility.IsInside(translated, viewport.Rows, viewport.Columns))
                    {
                        continue;
                    }

                    GUI.color = ghostColor;
                    GUI.DrawTexture(GetInsetCellRect(viewport, translated, CellInset), Texture2D.whiteTexture);
                }
            }
            finally
            {
                GUI.color = previous;
            }
        }

        /// <summary>
        /// Draws a consistent outline around each occupied cell of the selected block. No-op when
        /// the selection is invalid.
        /// </summary>
        public void DrawSelection(BlockXLevelFile data, LevelGridViewport viewport, int selectedBlockIndex)
        {
            if (data == null || data.blocks == null || viewport.Rows <= 0)
            {
                return;
            }

            if (selectedBlockIndex < 0 || selectedBlockIndex >= data.blocks.Count)
            {
                return;
            }

            DrawBlockOutline(data, viewport, selectedBlockIndex, SelectionOutlineColor);
        }

        /// <summary>
        /// Draws the selected cells overlay (translucent fill + border) for the Select Cells mode.
        /// Each selected cell is highlighted with <see cref="CellSelectionColor"/>.
        /// </summary>
        public void DrawCellSelection(BlockXLevelFile data, LevelGridViewport viewport, LevelEditorState state)
        {
            if (data == null || data.grid == null || viewport.Rows <= 0 || state == null)
                return;

            if (state.SelectedCells.Count == 0)
                return;

            Color prev = GUI.color;
            try
            {
                foreach (var cell in state.SelectedCells)
                {
                    if (!LevelGridCoordinateUtility.IsInside(cell, viewport.Rows, viewport.Columns))
                        continue;

                    GUI.color = CellSelectionColor;
                    Rect r = viewport.GetCellRect(cell);
                    GUI.DrawTexture(r, Texture2D.whiteTexture);
                    DrawRectBorder(r, CellSelectionBorderColor, 2f);
                }
            }
            finally
            {
                GUI.color = prev;
            }
        }

        /// <summary>Deterministic fallback color for a block by its colorId.</summary>
        public System.Func<int, Color> ColorResolver;

        private Color GetBlockColor(int colorId)
        {
            if (ColorResolver != null) return ColorResolver(colorId);
            float hue = Mathf.Repeat(colorId * 0.61803398875f, 1f);
            return Color.HSVToRGB(hue, 0.55f, 0.9f);
        }

        private static Rect GetInsetCellRect(LevelGridViewport viewport, Vector2Int cell, float inset)
        {
            Rect r = viewport.GetCellRect(cell);
            return new Rect(r.x + inset, r.y + inset, r.width - inset * 2f, r.height - inset * 2f);
        }

        internal static void DrawRectBorder(Rect rect, Color color, float thickness)
        {
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, thickness), color);
            EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - thickness, rect.width, thickness), color);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, thickness, rect.height), color);
            EditorGUI.DrawRect(new Rect(rect.xMax - thickness, rect.y, thickness, rect.height), color);
        }

        /// <summary>
        /// Draws a single outline around the outer perimeter of the block's shape (not per cell).
        /// For each occupied cell, an edge is drawn only when the neighbor in that direction is not
        /// part of the same block, so internal shared edges vanish and the outline traces the whole
        /// shape (including concave L/T silhouettes). No-op when the block is invalid/empty.
        /// </summary>
        private void DrawBlockOutline(BlockXLevelFile data, LevelGridViewport viewport, int blockIndex, Color color)
        {
            LevelBlockFile block = data.blocks[blockIndex];
            if (block == null || block.occupiedCells == null)
            {
                return;
            }

            // Collect the block's cells that are inside the playable grid.
            var cells = new HashSet<Vector2Int>();
            for (int i = 0; i < block.occupiedCells.Count; i++)
            {
                var cell = new Vector2Int(block.occupiedCells[i].x, block.occupiedCells[i].y);
                if (LevelGridCoordinateUtility.IsInside(cell, viewport.Rows, viewport.Columns))
                {
                    cells.Add(cell);
                }
            }

            Handles.BeginGUI();
            try
            {
                Color previous = Handles.color;
                Handles.color = color;
                foreach (Vector2Int cell in cells)
                {
                    Rect r = GetInsetCellRect(viewport, cell, CellInset);

                    // Top edge: only when no block cell sits above.
                    if (!cells.Contains(cell + Vector2Int.up))
                    {
                        Handles.DrawLine(new Vector2(r.xMin, r.yMin), new Vector2(r.xMax, r.yMin));
                    }

                    // Right edge: only when no block cell sits to the right.
                    if (!cells.Contains(cell + Vector2Int.right))
                    {
                        Handles.DrawLine(new Vector2(r.xMax, r.yMin), new Vector2(r.xMax, r.yMax));
                    }

                    // Bottom edge: only when no block cell sits below.
                    if (!cells.Contains(cell + Vector2Int.down))
                    {
                        Handles.DrawLine(new Vector2(r.xMax, r.yMax), new Vector2(r.xMin, r.yMax));
                    }

                    // Left edge: only when no block cell sits to the left.
                    if (!cells.Contains(cell + Vector2Int.left))
                    {
                        Handles.DrawLine(new Vector2(r.xMin, r.yMax), new Vector2(r.xMin, r.yMin));
                    }
                }

                Handles.color = previous;
            }
            finally
            {
                Handles.EndGUI();
            }
        }
    }
}
