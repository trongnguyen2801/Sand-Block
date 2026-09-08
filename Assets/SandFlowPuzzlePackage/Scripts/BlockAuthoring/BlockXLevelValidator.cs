

namespace SandFlowPuzzle.BlockAuthoring
{
    /// <summary>
    /// Validates the structural and gameplay rules for a versioned BlockX level file.
    /// </summary>
    public static class BlockXLevelValidator
    {
        public static bool TryValidate(BlockXLevelFile data, out string error)
        {
            error = null;

            if (data == null)
            {
                error = "Level data cannot be null.";
                return false;
            }

            if (data.version != BlockXLevelFormat.Version)
            {
                error = $"Unsupported level version: {data.version}. This editor supports version {BlockXLevelFormat.Version}.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(data.levelId))
            {
                error = "Level ID cannot be empty.";
                return false;
            }

            if (data.grid == null)
            {
                error = "Grid data cannot be null.";
                return false;
            }

            if (data.grid.rows < BlockXLevelFormat.MinRows || data.grid.rows > BlockXLevelFormat.MaxRows)
            {
                error = $"Grid rows must be between {BlockXLevelFormat.MinRows} and {BlockXLevelFormat.MaxRows}.";
                return false;
            }

            if (data.grid.columns < BlockXLevelFormat.MinColumns || data.grid.columns > BlockXLevelFormat.MaxColumns)
            {
                error = $"Grid columns must be between {BlockXLevelFormat.MinColumns} and {BlockXLevelFormat.MaxColumns}.";
                return false;
            }

            if (data.grid.cells == null)
            {
                error = "Grid cells cannot be null.";
                return false;
            }

            if (data.grid.cells.Length != data.grid.rows * data.grid.columns)
            {
                error = $"Grid cell array length must be {data.grid.rows * data.grid.columns}.";
                return false;
            }

            for (int cellIndex = 0; cellIndex < data.grid.cells.Length; cellIndex++)
            {
                int cellValue = data.grid.cells[cellIndex];
                if (cellValue != 0 && cellValue != 1)
                {
                    error = $"Editor version 1 grid cells must be 0 or 1. Invalid value {cellValue} at serialized index {cellIndex}.";
                    return false;
                }
            }

            if (data.blocks == null)
            {
                error = "Blocks list cannot be null.";
                return false;
            }

            for (int blockIndex = 0; blockIndex < data.blocks.Count; blockIndex++)
            {
                LevelBlockFile block = data.blocks[blockIndex];
                if (block == null)
                {
                    error = $"Block at index {blockIndex} cannot be null.";
                    return false;
                }

                if (block.occupiedCells == null)
                {
                    error = $"Block at index {blockIndex} occupied cells cannot be null.";
                    return false;
                }
            }

            // Same occupancy rules as BlockX GameBoardValidator, applied directly to the DTO.
            var occupied = new System.Collections.Generic.HashSet<UnityEngine.Vector2Int>();
            var pictureIds = new System.Collections.Generic.HashSet<int>();
            if (data.sandInBoard && data.sandRegions != null)
                foreach (var region in data.sandRegions)
                {
                    if (region == null || region.pictureIndex < 0 || !pictureIds.Add(region.pictureIndex)
                        || region.occupiedCells == null || region.occupiedCells.Count == 0)
                    { error = "Sand regions need a unique picture and at least one cell."; return false; }
                    foreach (var value in region.occupiedCells)
                    {
                        var cell = new UnityEngine.Vector2Int(value.x, value.y);
                        if (!LevelGridCoordinateUtility.IsInside(cell, data.grid.rows, data.grid.columns)
                            || data.grid.cells[LevelGridCoordinateUtility.ToIndex(cell, data.grid.rows, data.grid.columns)] != 1
                            || !occupied.Add(cell))
                        { error = $"Sand region overlaps or uses a disabled/outside cell {cell}."; return false; }
                    }
                }
            for (int i = 0; i < data.blocks.Count; i++)
            {
                var block = data.blocks[i];
                if (block.occupiedCells.Count == 0) { error = $"Block #{i + 1} is empty."; return false; }
                foreach (var value in block.occupiedCells)
                {
                    var cell = new UnityEngine.Vector2Int(value.x, value.y);
                    if (!LevelGridCoordinateUtility.IsInside(cell, data.grid.rows, data.grid.columns)
                        || data.grid.cells[LevelGridCoordinateUtility.ToIndex(cell, data.grid.rows, data.grid.columns)] != 1)
                    { error = $"Block #{i + 1}: cell {cell} is outside the playable grid."; return false; }
                    if (!occupied.Add(cell))
                    { error = $"Block #{i + 1}: duplicate or overlapping cell {cell}."; return false; }
                }
            }
            return true;
        }
    }
}
