using SandFlowPuzzle.BlockAuthoring;
using UnityEngine;

namespace SandFlowPuzzle.BlockAuthoring.EditorTools
{
    /// <summary>
    /// Grid-authoring mutations for the BlockX level editor (Task 7).
    ///
    /// Grid mutations are transactional: a valid mutation clones, validates, swaps, and bumps the
    /// revision. Single-cell toggles and resizes autosave immediately; paint mutations intentionally
    /// skip autosave so the window can save one completed drag stroke at mouse-up. Rejected
    /// operations (out-of-bounds, occupied-cell toggle-off, shrink-through-block, out-of-range
    /// dimensions) leave the document, revision, and save count untouched.
    /// </summary>
    public sealed class GridEditInteraction
    {
        /// <summary>
        /// Toggles a single grid cell between ON (1) and OFF (0).
        ///
        /// Turning a cell OFF is rejected when an existing block occupies it; the block's identity
        /// is surfaced with a user-specific message. Otherwise the mutation commits once through the
        /// document (and thereby autosaves).
        /// </summary>
        public bool TryToggleCell(LevelEditorDocument document, Vector2Int cell, out string error)
        {
            if (!TryReadCell(document, cell, out int currentValue, out error))
            {
                return false;
            }

            return TrySetCell(document, cell, currentValue == 1 ? 0 : 1, saveImmediately: true, out _, out error);
        }

        /// <summary>
        /// Sets one cell to a requested ON/OFF value without saving. The returned <paramref name="changed"/>
        /// is false when the cell already has the requested value. This method is intended for a
        /// drag stroke, whose caller saves once after the mouse button is released.
        /// </summary>
        public bool TryPaintCell(
            LevelEditorDocument document,
            Vector2Int cell,
            int targetValue,
            out bool changed,
            out string error)
        {
            return TrySetCell(document, cell, targetValue, saveImmediately: false, out changed, out error);
        }

        /// <summary>
        /// Resizes the grid, preserving the bottom-left intersection of old and new bounds.
        /// Growing adds ON cells to the right (columns) and top (rows); shrinking removes the
        /// coordinates above/right of the new bounds. Input outside 1..50 is rejected before any
        /// allocation. A shrink that would remove an occupied block cell is rejected by validation
        /// with the document, revision, and data unchanged.
        /// </summary>
        public bool TryResizeGrid(LevelEditorDocument document, int newRows, int newColumns, out string error)
        {
            error = null;

            if (document == null || document.Data == null || document.Data.grid == null)
            {
                error = "No level document is loaded.";
                return false;
            }

            // Reject/clamp input outside 1..50 before entering the transaction so partial IMGUI
            // values never allocate extreme arrays.
            int rows = Mathf.Clamp(newRows, BlockXLevelFormat.MinRows, BlockXLevelFormat.MaxRows);
            int columns = Mathf.Clamp(newColumns, BlockXLevelFormat.MinColumns, BlockXLevelFormat.MaxColumns);
            if (rows != newRows || columns != newColumns)
            {
                error = $"Grid dimensions must be between {BlockXLevelFormat.MinRows} and {BlockXLevelFormat.MaxRows}.";
                return false;
            }

            if (rows == document.Data.grid.rows && columns == document.Data.grid.columns)
            {
                return true;
            }

            return document.TryCommit("Resize grid", candidate =>
            {
                int oldRows = candidate.grid.rows;
                int oldColumns = candidate.grid.columns;
                int[] oldCells = candidate.grid.cells;

                int[] resized = new int[rows * columns];
                for (int i = 0; i < resized.Length; i++)
                {
                    resized[i] = 1;
                }

                int copyRows = Mathf.Min(oldRows, rows);
                int copyColumns = Mathf.Min(oldColumns, columns);
                for (int y = 0; y < copyRows; y++)
                {
                    for (int x = 0; x < copyColumns; x++)
                    {
                        int oldIndex = LevelGridCoordinateUtility.ToIndex(new Vector2Int(x, y), oldRows, oldColumns);
                        int newIndex = LevelGridCoordinateUtility.ToIndex(new Vector2Int(x, y), rows, columns);
                        resized[newIndex] = oldCells[oldIndex];
                    }
                }

                candidate.grid.rows = rows;
                candidate.grid.columns = columns;
                candidate.grid.cells = resized;
            }, out error);
        }

        private static bool TrySetCell(
            LevelEditorDocument document,
            Vector2Int cell,
            int targetValue,
            bool saveImmediately,
            out bool changed,
            out string error)
        {
            changed = false;
            error = null;

            if (targetValue != 0 && targetValue != 1)
            {
                error = "Grid cell value must be 0 (OFF) or 1 (ON).";
                return false;
            }

            if (!TryReadCell(document, cell, out int currentValue, out error))
            {
                return false;
            }

            if (currentValue == targetValue)
            {
                return true;
            }

            if (targetValue == 0)
            {
                int occupyingBlock = FindOccupyingBlock(document.Data, cell);
                if (occupyingBlock >= 0)
                {
                    error = $"Cell ({cell.x},{cell.y}) is occupied by Block #{occupyingBlock + 1} and cannot be disabled.";
                    return false;
                }
            }

            bool committed = saveImmediately
                ? document.TryCommit("Set grid cell", candidate => SetCell(candidate, cell, targetValue), out error)
                : document.TryCommitWithoutSave("Paint grid cell", candidate => SetCell(candidate, cell, targetValue), out error);
            changed = committed;
            return committed;
        }

        private static bool TryReadCell(
            LevelEditorDocument document,
            Vector2Int cell,
            out int value,
            out string error)
        {
            value = 0;
            error = null;

            if (document == null || document.Data == null || document.Data.grid == null)
            {
                error = "No level document is loaded.";
                return false;
            }

            int rows = document.Data.grid.rows;
            int columns = document.Data.grid.columns;
            if (!LevelGridCoordinateUtility.IsInside(cell, rows, columns))
            {
                error = $"Cell ({cell.x},{cell.y}) is outside the grid.";
                return false;
            }

            int index = LevelGridCoordinateUtility.ToIndex(cell, rows, columns);
            value = document.Data.grid.cells[index];
            return true;
        }

        private static void SetCell(BlockXLevelFile data, Vector2Int cell, int value)
        {
            int index = LevelGridCoordinateUtility.ToIndex(cell, data.grid.rows, data.grid.columns);
            data.grid.cells[index] = value;
        }

        /// <summary>
        /// Returns the 0-based index of the first block whose occupied cells contain the cell, or -1.
        /// </summary>
        private static int FindOccupyingBlock(BlockXLevelFile data, Vector2Int cell)
        {
            for (int blockIndex = 0; blockIndex < data.blocks.Count; blockIndex++)
            {
                LevelBlockFile block = data.blocks[blockIndex];
                if (block == null || block.occupiedCells == null)
                {
                    continue;
                }

                for (int cellIndex = 0; cellIndex < block.occupiedCells.Count; cellIndex++)
                {
                    LevelCellCoord occupied = block.occupiedCells[cellIndex];
                    if (occupied.x == cell.x && occupied.y == cell.y)
                    {
                        return blockIndex;
                    }
                }
            }

            return -1;
        }
    }
}
