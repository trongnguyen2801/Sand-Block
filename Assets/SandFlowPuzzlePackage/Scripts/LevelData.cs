using System;
using System.Collections.Generic;

namespace SandFlowPuzzle
{
    public enum SandSourceMode
    {
        Image = 0,
        Pattern = 1
    }

    [Serializable]
    public class LevelData
    {
        public string levelName = "New Level";

        // Sand grid (gridSize x gridSize flattened, color indices 0=empty, 1..N = palette colors)
        public List<byte> sandGrid = new List<byte>();
        public int gridSize = SandSimulator.GRID_SIZE;

        // Color palette (RGB, variable length)
        public List<SerializableColor> palette = new List<SerializableColor>();
        public int desiredColorCount = 4;

        // Source image (editor-only, path relative to Assets/)
        public string sourceImagePath = "";
        public int quantizationSeed = 0;

        // Optional hand-authored pattern stored below a Resources folder.
        // Existing levels default to Image, so their JSON remains compatible.
        public SandSourceMode sandSourceMode = SandSourceMode.Image;
        public string sandPatternResourcePath = "";

        // Bucket grid
        public int bucketRows = 5;
        public int bucketColumns = 5;
        public float bucketSpacingX = 0f;
        public float bucketSpacingY = 0f;
        public List<bool> gridCellEnabled = new List<bool>();

        // Bucket assignments
        public List<BucketDef> buckets = new List<BucketDef>();

        // Belt
        public int maxBeltSlots = 5;

        // Boosters
        public int slotBoosterCount = 3;
        public int shuffleBoosterCount = 3;
        public int magicBoosterCount = 3;
    }

    [Serializable]
    public class SerializableColor
    {
        public float r, g, b;

        public SerializableColor() { }

        public SerializableColor(float r, float g, float b)
        {
            this.r = r;
            this.g = g;
            this.b = b;
        }
    }

    [Serializable]
    public class BucketDef
    {
        public int row, col;
        public int colorId;    // palette index (1-based)
        public bool isMystery;
    }
}
