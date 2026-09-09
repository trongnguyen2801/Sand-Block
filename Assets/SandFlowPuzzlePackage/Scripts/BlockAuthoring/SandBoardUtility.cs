using System.Collections.Generic;
using UnityEngine;

namespace SandFlowPuzzle.BlockAuthoring
{
    public static class SandBoardUtility
    {
        public const int PixelsPerCell = 11;

        public static int GridSize(SandRegionFile region)
        {
            RectInt bounds = Bounds(region);
            return Mathf.Max(bounds.width, bounds.height) * PixelsPerCell;
        }

        // Preserve existing artwork when upgrading older fixed-resolution pictures.
        public static void ResizePicture(SandPictureData picture, int size)
        {
            int oldSize = picture.sandGrid == null ? 0 : (int)System.Math.Sqrt(picture.sandGrid.Count);
            if (picture.gridSize == size && oldSize == size && picture.sandGrid.Count == size * size) return;
            var pixels = new List<byte>(new byte[size * size]);
            if (oldSize > 0 && oldSize * oldSize == picture.sandGrid.Count)
                for (int y = 0; y < size; y++)
                    for (int x = 0; x < size; x++)
                    {
                        int sx = System.Math.Min((int)((x + .5) * oldSize / size), oldSize - 1);
                        int sy = System.Math.Min((int)((y + .5) * oldSize / size), oldSize - 1);
                        pixels[y * size + x] = picture.sandGrid[sy * oldSize + sx];
                    }
            picture.gridSize = size;
            picture.sandGrid = pixels;
        }

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
            int dx, int dy, byte colorId, int maxDepth, int budget, List<Vector2Int> extracted)
        {
            if (Mathf.Abs(dx) + Mathf.Abs(dy) != 1 || colorId == 0 || maxDepth <= 0 || budget <= 0) return 0;
            int size = (int)System.Math.Sqrt(grid.Length);
            if (size * size != grid.Length || (shapeMask != null && shapeMask.Length != grid.Length))
                throw new System.ArgumentException("Sand grid and mask dimensions must agree.");
            int count = 0;
            int depth = 0;
            while (x >= 0 && y >= 0 && x < size && y < size && depth < maxDepth && count < budget)
            {
                int index = y * size + x;
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
                depth++;
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
            int n = side * PixelsPerCell;
            var mask = new bool[n * n];
            for (int y = 0; y < n; y++) for (int x = 0; x < n; x++)
                mask[y * n + x] = cells.Contains(new Vector2Int(
                    bounds.x + x / PixelsPerCell,
                    bounds.y + side - 1 - y / PixelsPerCell));
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
