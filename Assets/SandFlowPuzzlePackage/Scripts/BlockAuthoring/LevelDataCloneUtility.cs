using System;
using System.Collections.Generic;

namespace SandFlowPuzzle.BlockAuthoring
{
    public static class LevelDataCloneUtility
    {
        public static BlockXLevelFile DeepClone(BlockXLevelFile source)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));

            var clone = new BlockXLevelFile
            {
                version = source.version,
                sandInBoard = source.sandInBoard,
                levelId = source.levelId,
                grid = source.grid == null
                    ? null
                    : new LevelGridFile
                    {
                        rows = source.grid.rows,
                        columns = source.grid.columns,
                        cells = source.grid.cells == null ? null : (int[])source.grid.cells.Clone()
                    },
                blocks = source.blocks == null ? null : new List<LevelBlockFile>(source.blocks.Count)
            };

            if (source.blocks != null)
            {
                foreach (LevelBlockFile block in source.blocks)
                {
                    if (block == null)
                    {
                        clone.blocks.Add(null);
                        continue;
                    }

                    clone.blocks.Add(new LevelBlockFile
                    {
                        colorId = block.colorId,
                        occupiedCells = block.occupiedCells == null
                            ? null
                            : new List<LevelCellCoord>(block.occupiedCells),
                        colorQuotas = block.colorQuotas == null
                            ? null
                            : new List<BlockColorQuota>(block.colorQuotas)
                    });
                }
            }

            if (source.sandRegions != null)
                foreach (var region in source.sandRegions)
                    clone.sandRegions.Add(region == null ? null : new SandRegionFile
                    {
                        pictureIndex = region.pictureIndex,
                        occupiedCells = region.occupiedCells == null ? null : new List<LevelCellCoord>(region.occupiedCells)
                    });
            return clone;
        }
    }
}
