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
            // Re-validate per-color quotas (if explicitly set) against the block count.
            for (int i = 0; i < resolved.blocks.Count; i++)
            {
                var block = resolved.blocks[i];
                if (block.colorQuotas == null) continue;
                int totalQuota = 0;
                var seen = new HashSet<int>();
                foreach (var q in block.colorQuotas)
                {
                    if (q == null || q.quota <= 0)
                    { error = $"Block #{i + 1} has an invalid color quota."; resolved = null; return false; }
                    if (!seen.Add(q.colorId))
                    { error = $"Block #{i + 1} lists color quota {q.colorId} twice."; resolved = null; return false; }
                    totalQuota += q.quota;
                }
                if (totalQuota > (counts.TryGetValue(block.colorId, out int colorGrains) ? colorGrains : 0))
                { error = $"Block #{i + 1} quota ({totalQuota}) exceeds the {block.colorId} color grain count ({colorGrains})."; resolved = null; return false; }
            }
            // Validate total per-color quotas across blocks do not exceed grain totals.
            var totalByColor = new Dictionary<int, int>();
            for (int i = 0; i < resolved.blocks.Count; i++)
            {
                var block = resolved.blocks[i];
                foreach (var q in block.colorQuotas)
                    totalByColor[q.colorId] = totalByColor.TryGetValue(q.colorId, out int t) ? t + q.quota : q.quota;
            }
            foreach (var entry in totalByColor)
            {
                int total = counts.TryGetValue(entry.Key, out int c) ? c : 0;
                if (entry.Value > total)
                { error = $"Color #{entry.Key} total quota ({entry.Value}) exceeds available grains ({total})."; resolved = null; return false; }
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

        /// <summary>
        /// Returns the per-color quotas for a block. When the block has no explicit colorQuotas,
        /// the color's total grain count is split evenly among all blocks of that color (legacy
        /// auto-split behavior).
        /// </summary>
        public static Dictionary<int, int> ResolveBlockQuotas(BlockXLevelFile board, int blockIndex, Dictionary<int, int> colorGrainCounts)
        {
            var result = new Dictionary<int, int>();
            if (board == null || blockIndex < 0 || blockIndex >= board.blocks.Count) return result;
            var block = board.blocks[blockIndex];
            if (block.colorQuotas != null && block.colorQuotas.Count > 0)
            {
                foreach (var q in block.colorQuotas)
                    if (q != null && q.quota > 0)
                        result[q.colorId] = q.quota;
                return result;
            }
            // Legacy auto-split by block.colorId.
            int colorId = block.colorId;
            int total = colorGrainCounts.TryGetValue(colorId, out int t) ? t : 0;
            int ordinal = 0;
            int count = 0;
            for (int i = 0; i < board.blocks.Count; i++)
            {
                if (board.blocks[i] != null && board.blocks[i].colorId == colorId) count++;
                if (i < blockIndex && board.blocks[i] != null && board.blocks[i].colorId == colorId) ordinal++;
            }
            if (total <= 0) return result;
            int quota = total / count + (ordinal < total % count ? 1 : 0);
            result[colorId] = quota;
            return result;
        }
    }
}
