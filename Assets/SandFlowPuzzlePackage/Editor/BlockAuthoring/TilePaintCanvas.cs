using System;
using System.Collections.Generic;
using UnityEngine;

namespace SandFlowPuzzle.BlockAuthoring.EditorTools
{
    /// <summary>Maps the selected footprint to the same square, top-first domain used by SandBoardUtility.Mask.</summary>
    public sealed class TilePaintCanvas
    {
        private readonly HashSet<Vector2Int> cells;
        public RectInt Bounds { get; }
        public int Size { get; }
        public int Side { get; }
        public int Width { get; }
        public int Height { get; }
        public int RowOffset { get; }
        public int CellCount => cells.Count;
        public int PaintableCount { get; }

        public TilePaintCanvas(IEnumerable<Vector2Int> selection, RectInt bounds)
        {
            cells = new HashSet<Vector2Int>(selection);
            Bounds = bounds;
            Side = Math.Max(bounds.width, bounds.height);
            if (Side < 1) throw new ArgumentOutOfRangeException(nameof(bounds));
            Size = Side * SandBoardUtility.PixelsPerCell;
            Width = Boundary(bounds.width);
            RowOffset = Boundary(Side - bounds.height);
            Height = Size - RowOffset;
            int count = 0;
            for (int y = 0; y < Height; y++)
                for (int x = 0; x < Width; x++)
                    if (TryGetIndex(x, y, out _)) count++;
            PaintableCount = count;
        }

        private int Boundary(int cell) => cell * SandBoardUtility.PixelsPerCell;

        public Vector2Int CellAt(int x, int y) => new Vector2Int(
            Bounds.x + x / SandBoardUtility.PixelsPerCell,
            Bounds.y + Side - 1 - (y + RowOffset) / SandBoardUtility.PixelsPerCell);

        public bool TryGetIndex(int x, int y, out int index)
        {
            index = -1;
            if (x < 0 || y < 0 || x >= Width || y >= Height || !cells.Contains(CellAt(x, y))) return false;
            index = (y + RowOffset) * Size + x;
            return true;
        }

        public RectInt CellPixels(Vector2Int cell)
        {
            int left = cell.x - Bounds.x;
            int top = Side - 1 - (cell.y - Bounds.y);
            return new RectInt(Boundary(left), Boundary(top) - RowOffset,
                Boundary(left + 1) - Boundary(left), Boundary(top + 1) - Boundary(top));
        }

        public bool Matches(IEnumerable<Vector2Int> selection, int size) => Size == size && cells.SetEquals(selection);

        public void Fill(IList<byte> grid, Vector2Int from, Vector2Int to, byte color)
        {
            for (int y = Math.Max(0, Math.Min(from.y, to.y)); y <= Math.Min(Height - 1, Math.Max(from.y, to.y)); y++)
                for (int x = Math.Max(0, Math.Min(from.x, to.x)); x <= Math.Min(Width - 1, Math.Max(from.x, to.x)); x++)
                    if (TryGetIndex(x, y, out int index)) grid[index] = color;
        }
    }
}
