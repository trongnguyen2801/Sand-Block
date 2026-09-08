using System.Collections.Generic;
using UnityEngine;

namespace SandFlowPuzzle.BlockAuthoring
{
    public static class CollectorBoardUtility
    {
        public static int ColorKey(SerializableColor color)
        {
            Color32 rgb = new Color(color.r, color.g, color.b);
            return (rgb.r << 16) | (rgb.g << 8) | rgb.b;
        }

        public static Dictionary<int, int> CountSand(List<SandPictureData> pictures, BlockXLevelFile board = null)
        {
            var counts = new Dictionary<int, int>();
            for (int pictureIndex = 0; pictureIndex < pictures.Count; pictureIndex++)
            {
                var picture = pictures[pictureIndex];
                var region = board != null && board.sandInBoard ? SandBoardUtility.FindRegion(board, pictureIndex) : null;
                bool[] mask = region == null ? null : SandBoardUtility.Mask(region);
                int size = Mathf.RoundToInt(Mathf.Sqrt(picture.sandGrid.Count));
                if (size <= 0 || size * size != picture.sandGrid.Count) continue;
                for (int y = 0; y < SandSimulator.GRID_SIZE; y++)
                    for (int x = 0; x < SandSimulator.GRID_SIZE; x++)
                    {
                        if (mask != null && !mask[y * SandSimulator.GRID_SIZE + x]) continue;
                        int sx = Mathf.Min(Mathf.FloorToInt((x + 0.5f) * size / SandSimulator.GRID_SIZE), size - 1);
                        int sy = Mathf.Min(Mathf.FloorToInt((y + 0.5f) * size / SandSimulator.GRID_SIZE), size - 1);
                        int id = picture.sandGrid[sy * size + sx];
                        if (id > 0) counts[id] = counts.TryGetValue(id, out int count) ? count + 1 : 1;
                    }
            }
            return counts;
        }

        public static BlockXLevelFile CreateDefault(LevelData level)
        {
            var pictures = level.PrepareRuntimePictures();
            level.collectorPalette = new List<SerializableColor>(pictures[0].palette);
            var counts = CountSand(pictures);
            int columns = Mathf.Max(6, Mathf.CeilToInt(Mathf.Sqrt(counts.Count)) * 2);
            int blocksPerRow = columns / 2;
            int rows = Mathf.Max(8, Mathf.CeilToInt(counts.Count / (float)blocksPerRow) * 3 + 2);
            var board = new BlockXLevelFile { levelId = level.levelName,
                grid = new LevelGridFile { rows = rows, columns = columns, cells = new int[rows * columns] } };
            for (int i = 0; i < board.grid.cells.Length; i++) board.grid.cells[i] = 1;
            int index = 0;
            foreach (var entry in counts)
            {
                int x = (index % blocksPerRow) * 2;
                int y = (index / blocksPerRow) * 3;
                board.blocks.Add(new LevelBlockFile { colorId = entry.Key, occupiedCells = new List<LevelCellCoord>
                    { new LevelCellCoord(x, y), new LevelCellCoord(x, y + 1) } });
                index++;
            }
            return SandBoardUtility.EmbedPictures(board, pictures.Count);
        }

        public static bool TryResolve(LevelData level, out BlockXLevelFile resolved, out string error)
        {
            resolved = null;
            error = null;
            if (!level.useAuthoredCollectorBoard) return true;
            if (!BlockXLevelValidator.TryValidate(level.collectorBoard, out error)) return false;
            var pictures = level.PrepareRuntimePictures();
            if (level.collectorBoard.sandInBoard)
            {
                if (level.collectorBoard.sandRegions == null || level.collectorBoard.sandRegions.Count != pictures.Count)
                { error = "Place every picture exactly once in the board."; return false; }
                foreach (var region in level.collectorBoard.sandRegions)
                    if (region.pictureIndex >= pictures.Count)
                    { error = "A sand region references a removed picture."; return false; }
            }
            var palette = pictures[0].palette;
            var ids = new Dictionary<int, int>();
            for (int i = 0; i < palette.Count; i++) ids[ColorKey(palette[i])] = i + 1;
            var counts = CountSand(pictures, level.collectorBoard);
            resolved = LevelDataCloneUtility.DeepClone(level.collectorBoard);
            var blocksPerColor = new Dictionary<int, int>();
            for (int i = 0; i < resolved.blocks.Count; i++)
            {
                int sourceId = resolved.blocks[i].colorId;
                if (level.collectorPalette == null || sourceId <= 0 || sourceId > level.collectorPalette.Count
                    || level.collectorPalette[sourceId - 1] == null
                    || !ids.TryGetValue(ColorKey(level.collectorPalette[sourceId - 1]), out int id)
                    || !counts.ContainsKey(id))
                { error = $"Block #{i + 1} needs a color that exists in the pictures."; resolved = null; return false; }
                resolved.blocks[i].colorId = id;
                blocksPerColor[id] = blocksPerColor.TryGetValue(id, out int n) ? n + 1 : 1;
            }
            foreach (var entry in counts)
            {
                if (!blocksPerColor.TryGetValue(entry.Key, out int n))
                { error = $"Picture color #{entry.Key} has {entry.Value} grains but no collector block."; resolved = null; return false; }
                if (n > entry.Value)
                { error = $"Color #{entry.Key} has more blocks than grains; remove a block of that color."; resolved = null; return false; }
            }
            return true;
        }
    }
}
