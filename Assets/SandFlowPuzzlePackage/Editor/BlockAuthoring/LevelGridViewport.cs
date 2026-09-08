using System;
using SandFlowPuzzle.BlockAuthoring;
using UnityEngine;

namespace SandFlowPuzzle.BlockAuthoring.EditorTools
{
    /// <summary>
    /// Immutable mapping between IMGUI screen space (Y grows downward) and canonical grid space
    /// (Y grows upward, row 0 at the bottom). Constructed once per grid draw; cell rects are the
    /// exact inverse of <see cref="TryScreenToCell"/>.
    /// </summary>
    public readonly struct LevelGridViewport
    {
        public Rect GridRect { get; }
        public float CellSize { get; }
        public int Rows { get; }
        public int Columns { get; }

        public LevelGridViewport(Rect gridRect, float cellSize, int rows, int columns)
        {
            if (rows <= 0) throw new ArgumentOutOfRangeException(nameof(rows));
            if (columns <= 0) throw new ArgumentOutOfRangeException(nameof(columns));
            if (cellSize <= 0f) throw new ArgumentOutOfRangeException(nameof(cellSize));

            GridRect = gridRect;
            CellSize = cellSize;
            Rows = rows;
            Columns = columns;
        }

        /// <summary>
        /// Converts a screen-space point to a canonical bottom-left grid cell. Returns false for
        /// points outside the grid rect or cells outside the grid.
        /// </summary>
        public bool TryScreenToCell(Vector2 mouse, out Vector2Int cell)
        {
            if (!GridRect.Contains(mouse))
            {
                cell = default;
                return false;
            }

            int x = Mathf.FloorToInt((mouse.x - GridRect.x) / CellSize);
            int visualRowFromTop = Mathf.FloorToInt((mouse.y - GridRect.y) / CellSize);
            int y = Rows - 1 - visualRowFromTop;
            cell = new Vector2Int(x, y);
            return LevelGridCoordinateUtility.IsInside(cell, Rows, Columns);
        }

        /// <summary>
        /// Returns the screen-space rect for a canonical grid cell; the inverse of
        /// <see cref="TryScreenToCell"/>. Throws for cells outside the grid.
        /// </summary>
        public Rect GetCellRect(Vector2Int cell)
        {
            if (!LevelGridCoordinateUtility.IsInside(cell, Rows, Columns))
            {
                throw new ArgumentOutOfRangeException(nameof(cell));
            }

            int visualRowFromTop = Rows - 1 - cell.y;
            float xMin = GridRect.x + cell.x * CellSize;
            float yMin = GridRect.y + visualRowFromTop * CellSize;
            return new Rect(xMin, yMin, CellSize, CellSize);
        }
    }
}