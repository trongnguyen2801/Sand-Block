using System.Collections.Generic;
using UnityEngine;

namespace SandFlowPuzzle.BlockAuthoring
{
    public static class SandBoardUtility
    {
        public static void EnsureUnified(LevelData level)
        {
            if (!level.useAuthoredCollectorBoard || level.collectorBoard == null
                || string.IsNullOrEmpty(level.collectorBoard.levelId))
            {
                level.collectorBoard = CollectorBoardUtility.CreateDefault(level);
                level.useAuthoredCollectorBoard = true;
            }
            else if (!level.collectorBoard.sandInBoard)
                level.collectorBoard = EmbedPictures(level.collectorBoard, level.PrepareRuntimePictures().Count);
        }

        public static int ExtractFromEdge(byte[] grid, bool[] shapeMask, int x, int y,
            int dx, int dy, byte colorId, int budget, List<Vector2Int> extracted)
        {
            if (Mathf.Abs(dx) + Mathf.Abs(dy) != 1 || colorId == 0 || budget <= 0) return 0;
            int count = 0;
            while (x >= 0 && y >= 0 && x < SandSimulator.GRID_SIZE && y < SandSimulator.GRID_SIZE && count < budget)
            {
                int index = y * SandSimulator.GRID_SIZE + x;
                if (shapeMask != null && !shapeMask[index]) break;
                byte grain = grid[index];
                if (grain != 0)
                {
                    if (grain != colorId) break;
                    grid[index] = 0;
                    extracted.Add(new Vector2Int(x, y));
                    count++;
                }
                x += dx;
                y += dy;
            }
            return count;
        }

        public static RectInt Bounds(SandRegionFile region)
        {
            int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
            foreach (var c in region.occupiedCells)
            { minX = Mathf.Min(minX, c.x); minY = Mathf.Min(minY, c.y); maxX = Mathf.Max(maxX, c.x); maxY = Mathf.Max(maxY, c.y); }
            return new RectInt(minX, minY, maxX - minX + 1, maxY - minY + 1);
        }

        // A square simulation domain keeps grains circular; cells outside the footprint are solid.
        public static bool[] Mask(SandRegionFile region)
        {
            RectInt bounds = Bounds(region);
            int side = Mathf.Max(bounds.width, bounds.height);
            var cells = new HashSet<Vector2Int>();
            foreach (var c in region.occupiedCells) cells.Add(new Vector2Int(c.x, c.y));
            int n = SandSimulator.GRID_SIZE;
            var mask = new bool[n * n];
            for (int y = 0; y < n; y++) for (int x = 0; x < n; x++)
                mask[y * n + x] = cells.Contains(new Vector2Int(
                    bounds.x + Mathf.FloorToInt((x + 0.5f) * side / n),
                    bounds.y + side - 1 - Mathf.FloorToInt((y + 0.5f) * side / n)));
            return mask;
        }

        public static SandRegionFile FindRegion(BlockXLevelFile board, int picture)
        {
            return board?.sandRegions?.Find(r => r != null && r.pictureIndex == picture);
        }

        public static bool IsSandCell(BlockXLevelFile board, int x, int y)
        {
            if (board == null || !board.sandInBoard || board.sandRegions == null) return false;
            foreach (var region in board.sandRegions)
                foreach (var c in region.occupiedCells) if (c.x == x && c.y == y) return true;
            return false;
        }

        // Migration preserves old block coordinates and adds room above them for the pictures.
        public static BlockXLevelFile EmbedPictures(BlockXLevelFile source, int pictureCount)
        {
            var board = LevelDataCloneUtility.DeepClone(source);
            if (board.sandInBoard) return board;
            int oldRows = board.grid.rows, cols = Mathf.Max(board.grid.columns, pictureCount * 2);
            int rows = oldRows + 4;
            if (rows > BlockXLevelFormat.MaxRows) throw new System.InvalidOperationException("Leave four rows available when converting the board.");
            int[] cells = new int[rows * cols];
            for (int i = 0; i < cells.Length; i++) cells[i] = 1;
            for (int y = 0; y < oldRows; y++) for (int x = 0; x < board.grid.columns; x++)
                cells[LevelGridCoordinateUtility.ToIndex(new Vector2Int(x, y), rows, cols)] =
                    board.grid.cells[LevelGridCoordinateUtility.ToIndex(new Vector2Int(x, y), oldRows, board.grid.columns)];
            board.grid = new LevelGridFile { rows = rows, columns = cols, cells = cells };
            board.sandInBoard = true;
            board.sandRegions = new List<SandRegionFile>();
            for (int i = 0; i < pictureCount; i++)
            {
                var region = new SandRegionFile { pictureIndex = i };
                int width = Mathf.Min(3, cols / pictureCount);
                for (int y = oldRows + 1; y < oldRows + 1 + width; y++) for (int x = i * cols / pictureCount; x < i * cols / pictureCount + width; x++)
                    region.occupiedCells.Add(new LevelCellCoord(x, y));
                board.sandRegions.Add(region);
            }
            return board;
        }
    }
}
