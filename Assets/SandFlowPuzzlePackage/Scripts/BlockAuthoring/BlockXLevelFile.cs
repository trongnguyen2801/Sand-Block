using System;
using System.Collections.Generic;

namespace SandFlowPuzzle.BlockAuthoring
{
    public static class BlockXLevelFormat
    {
        public const int Version = 1;
        public const int MinRows = 1;
        public const int MaxRows = 50;
        public const int MinColumns = 1;
        public const int MaxColumns = 50;
        public const int DefaultRows = 8;
        public const int DefaultColumns = 8;
    }

    [Serializable]
    public sealed class BlockXLevelFile
    {
        public int version = BlockXLevelFormat.Version;
        public string levelId = string.Empty;
        public LevelGridFile grid = new LevelGridFile();
        public List<LevelBlockFile> blocks = new List<LevelBlockFile>();
        public bool sandInBoard;
        public List<SandRegionFile> sandRegions = new List<SandRegionFile>();
    }

    [Serializable]
    public sealed class SandRegionFile
    {
        public int pictureIndex;
        public List<LevelCellCoord> occupiedCells = new List<LevelCellCoord>();
    }

    [Serializable]
    public sealed class LevelGridFile
    {
        public int rows = 1;
        public int columns = 1;
        public int[] cells = { 1 };
    }

    [Serializable]
    public sealed class LevelBlockFile
    {
        public int colorId;

        // Optional per-color quotas: colorId -> how many grains THIS block should collect.
        // When empty/absent, the runtime auto-splits the color's total grain count
        // evenly among blocks of that color (legacy behavior).
        public List<BlockColorQuota> colorQuotas = new List<BlockColorQuota>();

        public List<LevelCellCoord> occupiedCells = new List<LevelCellCoord>();
    }

    [Serializable]
    public sealed class BlockColorQuota
    {
        public int colorId;
        public int quota;
    }

    [Serializable]
    public struct LevelCellCoord
    {
        public int x;
        public int y;

        public LevelCellCoord(int x, int y)
        {
            this.x = x;
            this.y = y;
        }
    }
}
