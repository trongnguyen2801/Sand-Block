using System;
using UnityEngine;

namespace SandFlowPuzzle.BlockAuthoring
{
    public static class LevelGridCoordinateUtility
    {
        public static bool IsInside(Vector2Int cell, int rows, int columns)
        {
            return rows > 0 && columns > 0
                && cell.x >= 0 && cell.x < columns
                && cell.y >= 0 && cell.y < rows;
        }

        public static int ToIndex(Vector2Int cell, int rows, int columns)
        {
            ValidateDimensions(rows, columns);
            if (!IsInside(cell, rows, columns))
                throw new ArgumentOutOfRangeException(nameof(cell));

            int row = rows - 1 - cell.y;
            return row * columns + cell.x;
        }

        public static Vector2Int FromIndex(int index, int rows, int columns)
        {
            ValidateDimensions(rows, columns);
            int length = checked(rows * columns);
            if (index < 0 || index >= length)
                throw new ArgumentOutOfRangeException(nameof(index));

            int row = index / columns;
            int x = index % columns;
            int y = rows - 1 - row;
            return new Vector2Int(x, y);
        }

        private static void ValidateDimensions(int rows, int columns)
        {
            if (rows <= 0) throw new ArgumentOutOfRangeException(nameof(rows));
            if (columns <= 0) throw new ArgumentOutOfRangeException(nameof(columns));
        }
    }
}
